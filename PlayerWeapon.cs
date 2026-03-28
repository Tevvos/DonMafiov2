using UnityEngine;
using Fusion;
using System.Collections;

#region Weapon Ammo Interface
public interface IWeaponAmmo
{
    // Read-only info
    int AmmoInMag { get; }
    int MagSize { get; }
    bool IsReloading { get; }
    float ReloadSeconds { get; }

    // Server-side control
    void ServerInitAmmo();
    bool ServerCanFire();
    void ServerConsumeOnFire(NetworkRunner runner);
    bool ServerStartReload(NetworkRunner runner);
    void ServerTickReload(NetworkRunner runner);

    // FX accessors
    AudioSource GetAudioSource();
    AudioClip   GetShootSfx();
    AudioClip   GetReloadSfx();
    Animator    GetAnimator();
    string      GetShootTrigger();

    // Optional muzzle
    Animator    GetMuzzleAnimator();
    string      GetMuzzleTrigger();

    // Bullet prefab
    NetworkPrefabRef GetBulletPrefab();
}
#endregion

// ===== NEW: types d'ammo pris en charge par la banque du joueur =====
public enum AmmoKind { Pistol, Thompson, Shotgun }

/// <summary>
/// PlayerWeapon (Fusion 2) — contrôle commun des armes + FX réseau.
/// Les scripts d'armes implémentent IWeaponAmmo (chargeur/reload/sons/anims).
/// </summary>
public class PlayerWeapon : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform weaponHolderRight;
    [SerializeField] private Transform weaponHolderLeft;
    [SerializeField] private GameObject bloodEffectPrefab; // fallback local

    [Header("Blood FX (Network Prefab)")]
    [SerializeField] private NetworkPrefabRef bloodFxNetPrefab;

    [Header("Loot / Drop")]
    [SerializeField] private NetworkPrefabRef lootableWeaponPrefab;
    [System.Serializable] private struct LootPrefabByWeapon { public string weaponName; public NetworkPrefabRef lootPrefab; }
    [SerializeField] private LootPrefabByWeapon[] perWeaponLootPrefabs;

    [SerializeField] private KeyCode dropKey = KeyCode.G;

    [Header("Mount Tuning")]
    [SerializeField] private Vector3 rightLocalOffset = Vector3.zero;
    [SerializeField] private Vector3 leftLocalOffset  = Vector3.zero;
    [SerializeField] private float rightRotationOffsetZ = 0f;
    [SerializeField] private float leftRotationOffsetZ  = 0f;
    [SerializeField] private bool mirrorYOnLeft = true;

    [Header("Reload")]
    [SerializeField] private KeyCode reloadKey = KeyCode.R;

    [Header("Hitscan (fallback)")]
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private float maxHitRange = 40f;
    [SerializeField] private bool useHitscanFallback = false;
    [SerializeField] private int pistolDamage = 20;
    [SerializeField] private int thompsonDamage = 10;
    [SerializeField] private int shotgunPelletDamage = 10;

    // Réseau
    [Networked] private float NetAimZ { get; set; }
    [Networked] public NetworkString<_16> NetWeaponName { get; set; }

    private GameObject currentWeapon;
    private string currentWeaponName;
    private float nextFireTime;
    private PlayerMovement_FusionPro movement;
    private bool usingLeftHolder = false;
    private WeaponNetworkSync currentSync;
    private string _lastAppliedNetWeapon = null;

    private NetworkObject _ownerNO;
    private PlayerRef     _ownerRef;

    private float _lastSentAim;
    private float _lastAimSendTime;

    private Transform CurrentHolder => usingLeftHolder && weaponHolderLeft ? weaponHolderLeft :
                                       (!usingLeftHolder && weaponHolderRight ? weaponHolderRight : transform);

    // ===== NEW: banques de munitions côté joueur (serveur) =====
    private int _bankPistol;
    private int _bankThompson;
    private int _bankShotgun;

    // ===== NEW: API banque =====
    public void ServerAddReserveToBank(AmmoKind kind, int amount)
    {
        if (!Object || !Object.HasStateAuthority) return;
        if (amount <= 0) return;
        switch (kind)
        {
            case AmmoKind.Pistol:   _bankPistol   = Mathf.Clamp(_bankPistol   + amount, 0, 999999); break;
            case AmmoKind.Thompson: _bankThompson = Mathf.Clamp(_bankThompson + amount, 0, 999999); break;
            case AmmoKind.Shotgun:  _bankShotgun  = Mathf.Clamp(_bankShotgun  + amount, 0, 999999); break;
        }
    }

    public int ServerGetBank(AmmoKind kind)
    {
        switch (kind)
        {
            case AmmoKind.Pistol:   return _bankPistol;
            case AmmoKind.Thompson: return _bankThompson;
            case AmmoKind.Shotgun:  return _bankShotgun;
        }
        return 0;
    }

    // ===== BONUS CLASSE : DOG OF WAR =====
    public void AddReserveAmmoAll(int pistol, int shotgun, int thompson)
    {
        if (!Object || !Object.HasStateAuthority) return;

        if (pistol   > 0) ServerAddReserveToBank(AmmoKind.Pistol,   pistol);
        if (shotgun  > 0) ServerAddReserveToBank(AmmoKind.Shotgun,  shotgun);
        if (thompson > 0) ServerAddReserveToBank(AmmoKind.Thompson, thompson);

        // Feedback owner (Fusion 2 : plus de .IsValid → on compare à PlayerRef.None)
        if (Object && Object.InputAuthority != PlayerRef.None)
        {
            if (pistol   > 0) RPC_ShowAmmoToast((int)AmmoKind.Pistol,   pistol);
            if (shotgun  > 0) RPC_ShowAmmoToast((int)AmmoKind.Shotgun,  shotgun);
            if (thompson > 0) RPC_ShowAmmoToast((int)AmmoKind.Thompson, thompson);
        }

        Debug.Log($"[DogOfWar] Bonus ammo: +{pistol} pistol, +{shotgun} shotgun, +{thompson} thompson");
    }

    private void Awake()
    {
        if (weaponHolderRight == null) weaponHolderRight = transform.Find("WeaponHolder_Right");
        if (weaponHolderLeft  == null) weaponHolderLeft  = transform.Find("WeaponHolder_Left");
        movement = GetComponent<PlayerMovement_FusionPro>();
    }

    public override void Spawned()
    {
        _ownerNO  = Object ? Object : GetComponentInParent<NetworkObject>();
        _ownerRef = _ownerNO ? _ownerNO.InputAuthority : default;
        ApplyNetWeaponName();
    }

    public override void FixedUpdateNetwork()
    {
        // OWNER → envoie angle
        if (HasInputAuthority && currentWeapon && Camera.main)
        {
            Transform wp = currentWeapon.transform;
            Vector3 m = Camera.main.ScreenToWorldPoint(Input.mousePosition); m.z = 0f;
            float aim = Mathf.Atan2((m - wp.position).y, (m - wp.position).x) * Mathf.Rad2Deg;

            if (Mathf.Abs(Mathf.DeltaAngle(_lastSentAim, aim)) > 1f || (Time.time - _lastAimSendTime) >= 0.033f)
            {
                _lastSentAim = aim; _lastAimSendTime = Time.time;
                RPC_SetAimZ(aim);
            }
        }

        if (currentWeapon != null)
        {
            UpdateHolderByFacing();
            if (!HasInputAuthority) ApplyAimToWeapon(NetAimZ, false);
        }

        // Tick reload serveur
        if (Object.HasStateAuthority && currentWeapon != null)
        {
            var ammoWpn = GetWeaponAmmo(currentWeapon);
            if (ammoWpn != null) ammoWpn.ServerTickReload(Runner);
        }
    }

    public override void Render()
    {
        ApplyNetWeaponName();
        if (!currentWeapon) return;

        UpdateHolderByFacing();
        if (!HasInputAuthority) ApplyAimToWeapon(NetAimZ, true);
    }

    private void Update()
    {
        if (HasInputAuthority && Input.GetKeyDown(dropKey)) UnequipWeapon(true);
        if (!currentWeapon) return;

        // OWNER: rotation immédiate
        if (HasInputAuthority && Camera.main)
        {
            Transform wp = currentWeapon.transform;
            Vector3 m = Camera.main.ScreenToWorldPoint(Input.mousePosition); m.z = 0f;
            float aim = Mathf.Atan2((m - wp.position).y, (m - wp.position).x) * Mathf.Rad2Deg;
            ApplyAimToWeapon(aim, false);
        }

        if (!HasInputAuthority) return;

        if (Input.GetKeyDown(reloadKey)) { RPC_RequestReload(); }

        bool  isAuto   = false;
        float fireRate = 0.2f;

        var pistol   = currentWeapon.GetComponent<Pistol>();
        var shotgun  = currentWeapon.GetComponent<Shotgun>();
        var thompson = currentWeapon.GetComponent<Thompson>();
        if (pistol   != null) { isAuto = pistol.IsAutomatic;   fireRate = pistol.FireRate;   }
        else if (shotgun  != null) { isAuto = shotgun.IsAutomatic;  fireRate = shotgun.FireRate; }
        else if (thompson != null) { isAuto = thompson.IsAutomatic; fireRate = thompson.FireRate; }

        bool wantsShoot = isAuto ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (!wantsShoot || Time.time < nextFireTime) return;

        Vector2 dir = GetFireDirectionFromWeapon();
        if (shotgun != null)
            RPC_ServerTryShootPellets(dir, shotgun.Pellets, shotgun.SpreadDeg);
        else
            RPC_ServerTryShootBullet(dir);

        nextFireTime = Time.time + fireRate;
    }

    // ====== AIM ======
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, InvokeLocal = true)]
    private void RPC_SetAimZ(float angleDeg) { NetAimZ = angleDeg; }

    private void ApplyAimToWeapon(float angleDeg, bool smooth)
    {
        if (!currentWeapon) return;
        if (!currentSync) currentSync = currentWeapon.GetComponent<WeaponNetworkSync>() ?? currentWeapon.AddComponent<WeaponNetworkSync>();
        currentSync.SetUseSpriteFlip(false);
        currentSync.SetExtraZOffset(usingLeftHolder ? leftRotationOffsetZ : rightRotationOffsetZ);
        currentSync.ApplyAngle(angleDeg, smooth);
    }

    private void UpdateHolderByFacing()
    {
        bool faceRight = movement ? movement.FacingRight : true;
        bool shouldUseLeft = !faceRight;
        if (shouldUseLeft != usingLeftHolder)
        {
            usingLeftHolder = shouldUseLeft;
            ReparentAndTune(CurrentHolder);
        }
    }

    private void ReparentAndTune(Transform holder)
    {
        if (!currentWeapon || !holder) return;
        float ang = currentWeapon.transform.eulerAngles.z;

        currentWeapon.transform.SetParent(holder, false);
        if (usingLeftHolder)
        {
            currentWeapon.transform.localPosition = leftLocalOffset;
            currentWeapon.transform.localRotation = Quaternion.identity;
            currentWeapon.transform.localScale    = new Vector3(1f, mirrorYOnLeft ? -1f : 1f, 1f);
            currentSync?.SetExtraZOffset(leftRotationOffsetZ);
        }
        else
        {
            currentWeapon.transform.localPosition = rightLocalOffset;
            currentWeapon.transform.localRotation = Quaternion.identity;
            currentWeapon.transform.localScale    = Vector3.one;
            currentSync?.SetExtraZOffset(rightRotationOffsetZ);
        }
        currentWeapon.transform.rotation = Quaternion.Euler(0, 0, ang);
    }

    private Vector2 GetFireDirectionFromWeapon()
    {
        Transform fp = currentWeapon ? currentWeapon.transform.Find("FirePoint") : null;
        Transform basis = fp ? fp : (currentWeapon ? currentWeapon.transform : transform);
        Vector2 d = basis.right;
        if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
        return d.normalized;
    }

    private Vector3 GetServerFirePointPosition()
    {
        if (currentWeapon)
        {
            var fp = currentWeapon.transform.Find("FirePoint");
            if (fp) return fp.position;
        }
        return CurrentHolder ? CurrentHolder.position : transform.position;
    }

    // ---------- EQUIP ----------
    public void ServerEquipWeaponForAll(string weaponName)
    {
        if (Object && Object.HasStateAuthority) NetWeaponName = weaponName;
        else RPC_RequestEquip(weaponName);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestEquip(string weaponName) { NetWeaponName = weaponName; }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, InvokeLocal = true)]
    private void RPC_EquipWeaponForAll(string weaponName) { NetWeaponName = weaponName; }

    public void EquipWeaponByName(string weaponName)
    {
        GameObject prefab = Resources.Load<GameObject>("Weapons/" + weaponName);
        if (!prefab) { Debug.LogError("❌ Weapon prefab not found: " + weaponName); return; }

        RemoveExistingEquippedWeapons();
        currentWeaponName = weaponName;
        var instance = GameObject.Instantiate(prefab);
        EquipWeaponObject(instance);
    }

    public void EquipWeaponObject(GameObject weaponObject)
    {
        if (!weaponObject) return;
        RemoveExistingEquippedWeapons();

        currentWeapon = weaponObject;
        currentSync = weaponObject.GetComponent<WeaponNetworkSync>() ?? weaponObject.AddComponent<WeaponNetworkSync>();
        currentSync.SetUseSpriteFlip(false);
        currentSync.SetExtraZOffset(usingLeftHolder ? leftRotationOffsetZ : rightRotationOffsetZ);

        ReparentAndTune(CurrentHolder);
        if (movement) movement.SetArmed(true);

        StartCoroutine(SetupWeaponNextFrame(weaponObject));
    }

    private IEnumerator SetupWeaponNextFrame(GameObject weaponObject)
    {
        yield return null;
        if (!weaponObject) yield break;

        var pistol   = weaponObject.GetComponent<Pistol>();
        var shotgun  = weaponObject.GetComponent<Shotgun>();
        var thompson = weaponObject.GetComponent<Thompson>();
        pistol   ?.AssignReferences();
        shotgun  ?.AssignReferences();
        thompson ?.AssignReferences();

        // Init ammo serveur + ===== NEW: transfert BANQUE → ARME =====
        if (Object.HasStateAuthority)
        {
            var ammoWpn = GetWeaponAmmo(weaponObject);
            if (ammoWpn != null) ammoWpn.ServerInitAmmo();

            if (weaponObject.GetComponent<Pistol>() != null && ammoWpn != null)
            {
                if (_bankPistol > 0) { (ammoWpn as Pistol)?.ServerAddReserve(_bankPistol); _bankPistol = 0; }
            }
            else if (weaponObject.GetComponent<Thompson>() != null && ammoWpn != null)
            {
                if (_bankThompson > 0) { (ammoWpn as Thompson)?.ServerAddReserve(_bankThompson); _bankThompson = 0; }
            }
            else if (weaponObject.GetComponent<Shotgun>() != null && ammoWpn != null)
            {
                if (_bankShotgun > 0) { (ammoWpn as Shotgun)?.ServerAddReserve(_bankShotgun); _bankShotgun = 0; }
            }
        }

        var sr = weaponObject.GetComponentInChildren<SpriteRenderer>(true);
        if (sr) sr.enabled = true;
    }

    public GameObject GetCurrentWeapon() => currentWeapon;

    // ---------- DROP / UNEQUIP ----------
    public void UnequipWeapon(bool drop = true)
    {
        if (drop)
        {
            if (Object.HasStateAuthority) ServerDropCurrentWeapon();
            else RequestDropCurrentWeapon();
            return;
        }

        // ===== NEW: rapatrie la réserve de l'arme vers la banque AVANT destruction
        BankReserveFromCurrentWeapon();

        SafeDestroyWeapon(currentWeapon);
        currentWeapon = null; currentWeaponName = null; currentSync = null;
        if (movement) movement.SetArmed(false);
        PurgeHolder(weaponHolderRight); PurgeHolder(weaponHolderLeft);
    }

    private void RequestDropCurrentWeapon()
    {
        if (string.IsNullOrEmpty(currentWeaponName) || !currentWeapon) return;
        RPC_ServerDropCurrent(currentWeaponName);
    }

    private void ServerDropCurrentWeapon()
    {
        if (string.IsNullOrEmpty(currentWeaponName) || !currentWeapon)
        { NetWeaponName = default; RPC_UnequipForAll(); return; }

        if (lootableWeaponPrefab.Equals(default(NetworkPrefabRef)))
        { Debug.LogWarning("[PlayerWeapon] lootableWeaponPrefab non assigné."); RPC_UnequipForAll(); return; }

        Vector3 pos = GetDropPosition();
pos.y -= 0.25f;
        var selected = GetLootPrefabFor(currentWeaponName);
        var loot = Runner.Spawn(selected, pos, Quaternion.identity);
        var lw = loot.GetComponent<LootableWeapon>();
        if (lw) { lw.InitByName(currentWeaponName); lw.InitDropper(Object.InputAuthority, 0.5f, Runner); }

        NetWeaponName = default;
        RPC_UnequipForAll();
    }

    private NetworkPrefabRef GetLootPrefabFor(string weaponName)
    {
        if (perWeaponLootPrefabs != null)
        {
            for (int i = 0; i < perWeaponLootPrefabs.Length; i++)
            {
                var n = perWeaponLootPrefabs[i].weaponName;
                if (!string.IsNullOrEmpty(n) && string.Equals(n.Trim(), weaponName?.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    return perWeaponLootPrefabs[i].lootPrefab;
            }
        }
        return lootableWeaponPrefab;
    }

    private Vector3 GetDropPosition()
    {
        Vector3 basePos = CurrentHolder ? CurrentHolder.position : transform.position;
        Vector3 forward = (Vector3)GetFireDirectionFromWeapon();
        return basePos + forward * 0.5f + Vector3.down * 0.05f;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ServerDropCurrent(string weaponName) => ServerDropCurrentWeapon();

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, InvokeLocal = true)]
    private void RPC_UnequipForAll()
    {
        // ===== NEW: rapatrie la réserve de l'arme vers la banque AVANT destruction
        BankReserveFromCurrentWeapon();

        SafeDestroyWeapon(currentWeapon);
        currentWeapon = null; currentWeaponName = null; currentSync = null;
        if (movement) movement.SetArmed(false);
        PurgeHolder(weaponHolderRight); PurgeHolder(weaponHolderLeft);
        if (Object.HasStateAuthority) NetWeaponName = default;
    }

    // ===== NEW: utilitaire banque ← arme =====
    private void BankReserveFromCurrentWeapon()
    {
        if (!(Object && Object.HasStateAuthority) || !currentWeapon) return;

        var pistol = currentWeapon.GetComponent<Pistol>();
        var thompson = currentWeapon.GetComponent<Thompson>();
        var shotgun = currentWeapon.GetComponent<Shotgun>();

        if (pistol != null)   _bankPistol   += pistol.ServerGetReserve();
        if (thompson != null) _bankThompson += thompson.ServerGetReserve();
        if (shotgun != null)  _bankShotgun  += shotgun.ServerGetReserve();
    }

    // ====== Blood FX fallback ======
    public void SpawnBloodEffect() => SpawnBloodEffect(GetFireDirectionFromWeapon());
    public void SpawnBloodEffect(Vector2 direction)
    {
        if (!bloodEffectPrefab) return;
        var go = GameObject.Instantiate(bloodEffectPrefab, transform.position, Quaternion.identity);
        go.transform.right = direction; Destroy(go, 2f);
    }

    // ====== RPCs Tir & Reload ======
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ServerTryShootBullet(Vector2 clientDir)
    {
        var ammoWpn = GetWeaponAmmo(currentWeapon);
        if (ammoWpn == null) return;

        // ❌ PAS de réinit ici ! On laisse ServerCanFire() bloquer le tir si ammo == 0
        if (!ammoWpn.ServerCanFire())
        {
            if (ammoWpn.ServerStartReload(Runner))
                RPC_PlayReloadSfx();
            return;
        }

        UpdateHolderByFacing();
        ApplyAimToWeapon(NetAimZ, false);

        Vector3 spawnPos = GetServerFirePointPosition();
        Vector2 dir = clientDir.sqrMagnitude < 0.0001f ? Vector2.right : clientDir.normalized;
        if (!TryGetBulletPrefab(out var prefab)) return;

        float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, 0f, angleDeg);

        var bullet = Runner.Spawn(prefab, spawnPos, rot, _ownerRef);
        var b = bullet.GetComponent<Bullet>();
        if (b != null) b.Init(dir, this.gameObject);
        if (bullet && bullet.transform) bullet.transform.right = dir;

        bool wasReloading = ammoWpn.IsReloading;
        ammoWpn.ServerConsumeOnFire(Runner);
        if (!wasReloading && ammoWpn.IsReloading) RPC_PlayReloadSfx();

        if (useHitscanFallback) ServerTryHitScanFromWeapon(GetCurrentWeaponDamage());
        RPC_PlayShootFX();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ServerTryShootPellets(Vector2 clientDir, int pellets, float spreadDeg)
    {
        var ammoWpn = GetWeaponAmmo(currentWeapon);
        if (ammoWpn == null) return;

        // ❌ PAS de réinit ici non plus
        if (!ammoWpn.ServerCanFire())
        {
            if (ammoWpn.ServerStartReload(Runner))
                RPC_PlayReloadSfx();
            return;
        }

        UpdateHolderByFacing();
        ApplyAimToWeapon(NetAimZ, false);

        Vector3 spawnPos = GetServerFirePointPosition();
        Vector2 baseDir = clientDir.sqrMagnitude < 0.0001f ? Vector2.right : clientDir.normalized;
        if (!TryGetBulletPrefab(out var prefab)) return;

        for (int i = 0; i < pellets; i++)
        {
            float angle = Random.Range(-spreadDeg * 0.5f, spreadDeg * 0.5f);
            Vector2 dir = (Quaternion.Euler(0, 0, angle) * baseDir).normalized;

            float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(0f, 0f, angleDeg);

            var bullet = Runner.Spawn(prefab, spawnPos, rot, _ownerRef);
            var b = bullet.GetComponent<Bullet>();
            if (b != null) { b.Init(dir, this.gameObject); b.SetDamage(shotgunPelletDamage); }
            if (bullet && bullet.transform) bullet.transform.right = dir;
        }

        bool wasReloading = ammoWpn.IsReloading;
        ammoWpn.ServerConsumeOnFire(Runner);
        if (!wasReloading && ammoWpn.IsReloading) RPC_PlayReloadSfx();

        if (useHitscanFallback) ServerTryHitScanFromWeapon(shotgunPelletDamage);
        RPC_PlayShootFX();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestReload()
    {
        var ammoWpn = GetWeaponAmmo(currentWeapon);
        if (ammoWpn == null) return;
        if (ammoWpn.ServerStartReload(Runner)) RPC_PlayReloadSfx();
    }

    // ====== FX ======
    private bool AnimatorHasTrigger(Animator anim, string trig)
    {
        if (!anim || string.IsNullOrEmpty(trig)) return false;
        for (int i = 0; i < anim.parameterCount; i++)
            if (anim.GetParameter(i).type == AnimatorControllerParameterType.Trigger && anim.GetParameter(i).name == trig) return true;
        return false;
    }

    private string ResolveTriggerName(Animator anim, string requested)
    {
        if (!anim || string.IsNullOrEmpty(requested)) return null;
        for (int i = 0; i < anim.parameterCount; i++)
        {
            var p = anim.GetParameter(i);
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == requested) return p.name;
        }
        for (int i = 0; i < anim.parameterCount; i++)
        {
            var p = anim.GetParameter(i);
            if (p.type == AnimatorControllerParameterType.Trigger &&
                string.Equals(p.name, requested, System.StringComparison.OrdinalIgnoreCase)) return p.name;
        }
        return null;
    }

    private SpriteRenderer FindMuzzleSprite(Transform root)
    {
        if (!root) return null;
        var srs = root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in srs) { var n = sr.gameObject.name.ToLower(); if (n.Contains("muzzle") || n.Contains("flash")) return sr; }
        return null;
    }
    private ParticleSystem FindMuzzleParticle(Transform root)
    {
        if (!root) return null;
        var ps = root.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var p in ps) { var n = p.gameObject.name.ToLower(); if (n.Contains("muzzle") || n.Contains("flash")) return p; }
        return null;
    }
    private IEnumerator MuzzleBlink(SpriteRenderer sr){ if (!sr) yield break; sr.enabled = true; yield return new WaitForSeconds(0.05f); if (sr) sr.enabled = false; }
    private IEnumerator MuzzleBlinkGO(GameObject go,float s=0.05f){ if(!go)yield break; bool was=go.activeSelf; go.SetActive(true); yield return new WaitForSeconds(s); if(go)go.SetActive(was); }
    private IEnumerator ReTriggerAnimator(Animator anim,string trig,float d){ if(!anim||string.IsNullOrEmpty(trig))yield break; yield return new WaitForSeconds(d); if(anim){ anim.ResetTrigger(trig); anim.SetTrigger(trig);} }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, InvokeLocal = true)]
    private void RPC_PlayReloadSfx()
    {
        if (!currentWeapon) return;
        var ammoWpn = GetWeaponAmmo(currentWeapon); if (ammoWpn == null) return;
        var clip = ammoWpn.GetReloadSfx(); if (!clip) return;
        var src = ammoWpn.GetAudioSource(); if (src) src.PlayOneShot(clip); else AudioSource.PlayClipAtPoint(clip, transform.position);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, InvokeLocal = true)]
    private void RPC_PlayShootFX()
    {
        if (!currentWeapon) return;

        var ammoWpn = GetWeaponAmmo(currentWeapon); if (ammoWpn == null) return;
        var anim = ammoWpn.GetAnimator(); var muzzle = ammoWpn.GetMuzzleAnimator();
        string shootTrig = ResolveTriggerName(anim,   ammoWpn.GetShootTrigger());
        string muzzleTrig= ResolveTriggerName(muzzle, ammoWpn.GetMuzzleTrigger());
        bool sameAnimator = (anim && muzzle && anim == muzzle);

        bool muzzleTriggered = false;
        if (muzzle)
        {
            muzzle.gameObject.SetActive(true);
            if (!string.IsNullOrEmpty(muzzleTrig)) { muzzle.ResetTrigger(muzzleTrig); muzzle.SetTrigger(muzzleTrig); muzzleTriggered = true; }
        }
        if (sameAnimator && muzzleTriggered) StartCoroutine(ReTriggerAnimator(muzzle, muzzleTrig, 0.01f));
        if (anim && !string.IsNullOrEmpty(shootTrig))
            if (!(sameAnimator && muzzleTriggered && shootTrig == muzzleTrig)) anim.SetTrigger(shootTrig);

        var ps = muzzle ? FindMuzzleParticle(muzzle.transform) : null;
        if (!ps) ps = FindMuzzleParticle(currentWeapon.transform);
        if (ps) ps.Play();

        var sr = muzzle ? muzzle.GetComponentInChildren<SpriteRenderer>(true) : null;
        if (!sr) sr = FindMuzzleSprite(currentWeapon.transform);
        if (sr) StartCoroutine(MuzzleBlink(sr));
        if (!ps && !sr && muzzle) StartCoroutine(MuzzleBlinkGO(muzzle.gameObject, 0.05f));

        var flashLight = currentWeapon.GetComponentInChildren<MuzzleFlashLight2D>(true);
        if (flashLight) flashLight.TriggerFlash();

        var clip = ammoWpn.GetShootSfx();
        if (clip != null)
        {
            var src = ammoWpn.GetAudioSource();
            if (src) src.PlayOneShot(clip); else AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        if (HasInputAuthority)
        {
            var cam = FindObjectOfType<CameraFollowFusion>();
            if (cam)
            {
                float intensity = 0.4f;
                if (currentWeapon)
                {
                    if (currentWeapon.GetComponent<Pistol>()) intensity = 0.3f;
                    else if (currentWeapon.GetComponent<Thompson>()) intensity = 0.5f;
                    else if (currentWeapon.GetComponent<Shotgun>()) intensity = 0.8f;
                }
                cam.ShootShake(intensity);
            }
        }
    }

    // ====== Utils ======
    private bool TryGetBulletPrefab(out NetworkPrefabRef prefab)
    {
        prefab = default;
        var ammoWpn = GetWeaponAmmo(currentWeapon);
        if (ammoWpn == null) return false;
        prefab = ammoWpn.GetBulletPrefab();
        return !prefab.Equals(default(NetworkPrefabRef));
    }

    private IWeaponAmmo GetWeaponAmmo(GameObject weaponGo) => weaponGo ? weaponGo.GetComponent<IWeaponAmmo>() : null;

    private void SafeDestroyWeapon(GameObject go){ if (go) Destroy(go); }

    private void RemoveExistingEquippedWeapons()
    {
        SafeDestroyWeapon(currentWeapon); currentWeapon = null; currentSync = null;
        PurgeHolder(weaponHolderRight); PurgeHolder(weaponHolderLeft);
    }

    private void PurgeHolder(Transform holder)
    {
        if (!holder) return;
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            var child = holder.GetChild(i).gameObject;
            if (!child) continue;
            if (child.GetComponent<WeaponNetworkSync>() != null || child.GetComponent<IWeaponAmmo>() != null)
                Destroy(child);
        }
    }

    private void ApplyNetWeaponName()
    {
        string nameNow = NetWeaponName.ToString();
        if (_lastAppliedNetWeapon == nameNow) return;
        _lastAppliedNetWeapon = nameNow;

        if (string.IsNullOrEmpty(nameNow))
        { UnequipWeapon(drop:false); return; }

        EquipWeaponByName(nameNow);
    }

    // ====== Hitscan fallback ======
    private void ServerTryHitScanFromWeapon(int damagePerHit)
    {
        if (!Object || !Object.HasStateAuthority || !currentWeapon) return;

        Vector2 origin = currentWeapon.transform.position;
        Vector2 dir = GetFireDirectionFromWeapon().normalized;
        origin += dir * 0.1f;

        var hit = Physics2D.Raycast(origin, dir, maxHitRange, hitMask);
        if (!hit.collider) return;

        var selfNO  = _ownerNO ? _ownerNO : GetComponentInParent<NetworkObject>();
        var otherNO = hit.collider.GetComponentInParent<NetworkObject>();
        if (selfNO && otherNO && selfNO == otherNO) return;

        var hp = hit.collider.GetComponentInParent<PlayerHealth>();
        if (hp != null)
        {
            hp.TakeDamage(damagePerHit, _ownerNO);
            ServerSpawnBloodAt(hit.point, hit.normal, dir);
        }
    }

    private void ServerSpawnBloodAt(Vector2 pos, Vector2 normal, Vector2 shotDir)
    {
        if (!bloodFxNetPrefab.Equals(default(NetworkPrefabRef)))
        {
            Vector2 basis = (normal.sqrMagnitude > 0.0001f) ? normal.normalized : shotDir.normalized;
            float angleDeg = Mathf.Atan2(basis.y, basis.x) * Mathf.Rad2Deg;
            float jitter = Random.Range(-20f, 20f);
            Runner.Spawn(bloodFxNetPrefab, pos, Quaternion.Euler(0f, 0f, angleDeg + jitter));
            return;
        }
        RPC_SpawnBlood(pos, normal, Mathf.Sign(shotDir.x));
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable, InvokeLocal = true)]
    public void RPC_ShowAmmoToast(int ammoKindInt, int amount)
    {
        var ui = GetComponent<PickupToastUI>();
        if (!ui)
        {
            Debug.LogWarning("[PlayerWeapon] Pas de PickupToastUI sur le PlayerPrefab → toast ignoré.");
            return;
        }
        ui.ShowAmmo((AmmoKind)ammoKindInt, amount);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable, InvokeLocal = true)]
private void RPC_SpawnBlood(Vector2 pos, Vector2 surfaceNormal, float dirXSign)
{
    if (!bloodEffectPrefab) return;

    var go = GameObject.Instantiate(bloodEffectPrefab, pos, Quaternion.identity);
    if (surfaceNormal.sqrMagnitude > 0.0001f) go.transform.up = surfaceNormal.normalized;
    go.transform.Rotate(0f, 0f, Random.Range(-20f, 20f));
    Destroy(go, 2f);
}


    private int GetCurrentWeaponDamage()
    {
        if (string.IsNullOrEmpty(currentWeaponName)) return pistolDamage;
        var name = currentWeaponName.ToLowerInvariant();
        if (name.Contains("thompson")) return thompsonDamage;
        if (name.Contains("shotgun"))  return shotgunPelletDamage;
        return pistolDamage;
    }

    // ======== LOBBY RESET (dé-équipe + nettoie NetWeaponName serveur) ========
    public void ResetForLobby()
    {
        UnequipWeapon(drop: false);
        if (Object && Object.HasStateAuthority) NetWeaponName = default;
    }

    // ======== Utilisé par Loot/AmmoPickup → plein de chargeur serveur ========
    public void SetMagFromLoot(int _)
    {
        if (Object && Object.HasStateAuthority)
        {
            var ammoWpn = GetWeaponAmmo(currentWeapon);
            if (ammoWpn != null) ammoWpn.ServerInitAmmo();
        }
    }
}
