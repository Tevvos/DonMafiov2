using UnityEngine;
using Fusion;

public class Thompson : MonoBehaviour, IWeaponAmmo
{
    [Header("Firing")]
    [SerializeField, Tooltip("Temps entre tirs (ex: 0.08 ≈ 12.5 cps)")]
    private float fireInterval = 0.13f;
    public float FireRate => fireInterval;
    public bool  IsAutomatic => true;

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
    [SerializeField] private int   magSize = 30;
    [SerializeField] private float reloadSeconds = 2.0f;

    [Tooltip("Recharge automatique quand le chargeur tombe à 0 (désactivé pour éviter les balles infinies).")]
    [SerializeField] private bool  autoReloadOnEmpty = false;

    [Header("Reserve (server only)")]
    [SerializeField, Tooltip("Nombre max de balles en réserve (ramassées via loot).")]
    private int maxReserve = 120;

    [SerializeField, Tooltip("Réserve initiale au spawn (0 recommandé).")]
    private int initialReserve = 0;

    [Header("Camera Recoil")]
    [Range(0f, 2f)] [SerializeField] private float shootShakeIntensity = 0.5f;

    // ===== Runtime (côté serveur uniquement, PAS networked) =====
    private int ammoInMag;
    private int reserveAmmo;
    private bool isReloading;
    private TickTimer reloadTimer;

    // ------------- IWeaponAmmo -------------
    public int AmmoInMag => ammoInMag;
    public int MagSize => magSize;
    public bool IsReloading => isReloading;
    public float ReloadSeconds => reloadSeconds;

    public AudioSource GetAudioSource() => audioSource;
    public AudioClip   GetShootSfx()   => shootSound;
    public AudioClip   GetReloadSfx()  => reloadSound;
    public Animator    GetAnimator()   => animator;
    public string      GetShootTrigger()=> shootTriggerParam;
    public Animator    GetMuzzleAnimator() => muzzleAnimator;
    public string      GetMuzzleTrigger()  => muzzleTriggerParam;
    public NetworkPrefabRef GetBulletPrefab() => bulletPrefab;

    // --- Références ---
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

    public void Fire(Vector2 dir) { /* FX joués via RPC par PlayerWeapon */ }

    // ---- AMMO : logique serveur uniquement ----
    public void ServerInitAmmo()
    {
        // Chargeur plein au spawn, réserve selon paramètre (par défaut 0).
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
        if (isReloading || ammoInMag <= 0) return;
        ammoInMag = Mathf.Max(ammoInMag - 1, 0);

        // Si on veut autoriser la recharge auto quand le chargeur est vide ET qu'il reste de la réserve
        if (ammoInMag == 0 && autoReloadOnEmpty && reserveAmmo > 0)
            ServerStartReload(runner);
    }

    /// <summary>
    /// Déclenche une recharge uniquement s'il y a de la réserve.
    /// </summary>
    public bool ServerStartReload(NetworkRunner runner)
    {
        if (isReloading) return false;
        if (ammoInMag >= magSize) return false;
        if (reserveAmmo <= 0) return false; // 🔑 empêche la recharge infinie sans loot

        isReloading = true;
        reloadTimer = TickTimer.CreateFromSeconds(runner, Mathf.Max(0.01f, reloadSeconds));
        return true;
    }

    public void ServerTickReload(NetworkRunner runner)
    {
        if (!isReloading) return;
        if (!reloadTimer.Expired(runner)) return;

        // Transfère de la réserve vers le chargeur
        int need   = Mathf.Max(0, magSize - ammoInMag);
        int toLoad = Mathf.Min(need, reserveAmmo);
        ammoInMag  = Mathf.Clamp(ammoInMag + toLoad, 0, magSize);
        reserveAmmo = Mathf.Clamp(reserveAmmo - toLoad, 0, maxReserve);

        isReloading = false;
    }

    // ====== API serveur pour le loot ======

    /// <summary> Définit directement les balles du chargeur (utilisé si tu veux forcer un état). </summary>
    public void ServerSetAmmo(int value)
    {
        ammoInMag = Mathf.Clamp(value, 0, magSize);
    }

    /// <summary> Ajoute des balles à la réserve (pickup). </summary>
    public void ServerAddReserve(int amount)
    {
        if (amount <= 0) return;
        reserveAmmo = Mathf.Clamp(reserveAmmo + amount, 0, maxReserve);
    }

    /// <summary> Permet de lire la réserve côté debug (non exposé par l’interface). </summary>
    public int ServerGetReserve() => reserveAmmo;

    // Utilitaires
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
        fireInterval  = Mathf.Clamp(fireInterval, 0.03f, 2f);
        magSize       = Mathf.Max(1, magSize);
        reloadSeconds = Mathf.Max(0.05f, reloadSeconds);
        maxReserve    = Mathf.Max(0, maxReserve);
        initialReserve= Mathf.Clamp(initialReserve, 0, maxReserve);
        if (bulletPrefab.Equals(default(NetworkPrefabRef)))
            Debug.LogWarning($"[Thompson] '{name}' n'a pas de bulletPrefab assigné.");
    }
#endif
}
