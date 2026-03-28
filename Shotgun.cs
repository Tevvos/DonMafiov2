using UnityEngine;
using Fusion;

public class Shotgun : MonoBehaviour, IWeaponAmmo
{
    [Header("Firing")]
    [SerializeField] private float fireInterval = 0.8f;
    public float FireRate => fireInterval;
    public bool  IsAutomatic => false;

    [Header("Pellets")]
    [SerializeField] private int   pellets   = 6;
    [SerializeField] private float spreadDeg = 20f;
    public int  Pellets  => pellets;
    public float SpreadDeg => spreadDeg;

    [Header("Bullet")]
    [SerializeField] private NetworkPrefabRef bulletPrefab;
    [SerializeField] private Transform firePoint;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string shootTriggerParam = "Shoot";

    [Header("Muzzle Flash (optionnel)")]
    [SerializeField] private Animator muzzleAnimator;
    [SerializeField] private string muzzleTriggerParam = "ShootFlash";

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shootSound;
    [SerializeField] private AudioClip reloadSound;

    [Header("Ammo (server only)")]
    [SerializeField] private int   magSize = 2;
    [SerializeField] private float reloadSeconds = 2.4f;

    [Tooltip("Recharge auto quand chargeur à 0 (seulement s'il reste de la réserve).")]
    [SerializeField] private bool  autoReloadOnEmpty = false;

    [Header("Reserve (server only)")]
    [SerializeField, Tooltip("Nombre max de cartouches en réserve.")]
    private int maxReserve = 24;
    [SerializeField, Tooltip("Réserve initiale au spawn.")]
    private int initialReserve = 0;

    [Header("Camera Recoil")]
    [Range(0f, 2f)] [SerializeField] private float shootShakeIntensity = 0.9f;

    // Runtime (server)
    private int ammoInMag;
    private int reserveAmmo;
    private bool isReloading;
    private TickTimer reloadTimer;

    // IWeaponAmmo
    public int AmmoInMag => ammoInMag;
    public int MagSize => magSize;
    public bool IsReloading => isReloading;
    public float ReloadSeconds => reloadSeconds;
    public AudioSource GetAudioSource() => audioSource;
    public AudioClip   GetShootSfx() => shootSound;
    public AudioClip   GetReloadSfx() => reloadSound;
    public Animator    GetAnimator() => animator;
    public string      GetShootTrigger() => shootTriggerParam;
    public Animator    GetMuzzleAnimator() => muzzleAnimator;
    public string      GetMuzzleTrigger() => muzzleTriggerParam;
    public NetworkPrefabRef GetBulletPrefab() => bulletPrefab;

    public void AssignReferences()
    {
        if (!firePoint)   firePoint   = transform.Find("FirePoint");
        if (!audioSource) audioSource = GetComponent<AudioSource>();
        if (!animator)    animator    = GetComponent<Animator>();
        if (!muzzleAnimator)
        {
            foreach (var a in GetComponentsInChildren<Animator>(true))
            {
                var n = a.gameObject.name.ToLower();
                if (n.Contains("muzzle") || n.Contains("flash")) { muzzleAnimator = a; break; }
            }
        }
    }

    public void Fire(Vector2 dir) { /* FX via RPC après validation serveur */ }

    // ---- Ammo (server-only) ----
    public void ServerInitAmmo()
    {
        ammoInMag   = Mathf.Clamp(magSize, 0, 9999);
        reserveAmmo = Mathf.Clamp(initialReserve, 0, maxReserve);
        isReloading = false;
        reloadTimer = TickTimer.None;
    }

    public bool ServerCanFire()
    {
        if (isReloading) return false;
        if (GetBulletPrefab().Equals(default(NetworkPrefabRef))) return false;
        return ammoInMag > 0;
    }

    public void ServerConsumeOnFire(NetworkRunner runner)
    {
        if (ammoInMag <= 0 || isReloading) return;
        ammoInMag = Mathf.Max(ammoInMag - 1, 0);

        if (ammoInMag == 0 && autoReloadOnEmpty && reserveAmmo > 0)
            ServerStartReload(runner);
    }

    public bool ServerStartReload(NetworkRunner runner)
    {
        if (isReloading) return false;
        if (ammoInMag >= magSize) return false;
        if (reserveAmmo <= 0) return false; // 🔑 pas de recharge infinie
        isReloading = true;
        reloadTimer = TickTimer.CreateFromSeconds(runner, Mathf.Max(0.01f, reloadSeconds));
        return true;
    }

    public void ServerTickReload(NetworkRunner runner)
    {
        if (!isReloading) return;
        if (!reloadTimer.Expired(runner)) return;

        // recharge par transfert depuis la réserve
        int need   = Mathf.Max(0, magSize - ammoInMag);
        int toLoad = Mathf.Min(need, reserveAmmo);
        ammoInMag   = Mathf.Clamp(ammoInMag + toLoad, 0, magSize);
        reserveAmmo = Mathf.Clamp(reserveAmmo - toLoad, 0, maxReserve);

        isReloading = false;
    }

    // ===== API serveur pour loot =====
    public void ServerSetAmmo(int value) => ammoInMag = Mathf.Clamp(value, 0, magSize);
    public void ServerAddReserve(int amount)
    {
        if (amount <= 0) return;
        reserveAmmo = Mathf.Clamp(reserveAmmo + amount, 0, maxReserve);
    }
    public int ServerGetReserve() => reserveAmmo;

    public void LocalShootShake()
    {
        var no = GetComponentInParent<NetworkObject>();
        if (no == null || !no.HasInputAuthority) return;
        var cam = FindObjectOfType<CameraFollowFusion>();
        if (cam) cam.ShootShake(Mathf.Max(0f, shootShakeIntensity));
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        fireInterval  = Mathf.Clamp(fireInterval, 0.1f, 2f);
        pellets       = Mathf.Max(1, pellets);
        spreadDeg     = Mathf.Clamp(spreadDeg, 0f, 50f);
        magSize       = Mathf.Max(1, magSize);
        reloadSeconds = Mathf.Max(0.01f, reloadSeconds);
        maxReserve    = Mathf.Max(0, maxReserve);
        initialReserve= Mathf.Clamp(initialReserve, 0, maxReserve);
        if (bulletPrefab.Equals(default(NetworkPrefabRef)))
            Debug.LogWarning($"[Shotgun] '{name}' n'a pas de bulletPrefab assigné.");
    }
#endif
}
