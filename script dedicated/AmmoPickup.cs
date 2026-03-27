using System.Collections;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider2D))]
public class AmmoPickup : NetworkBehaviour
{
    public enum RefillMode { Single, All }

    [Header("Mode")]
    [SerializeField] private RefillMode mode = RefillMode.Single;

    [Header("Single Mode")]
    [Tooltip("Type d'ammo quand le mode est Single.")]
    [SerializeField] private AmmoKind ammoKind = AmmoKind.Thompson;
    [Tooltip("Quantité ajoutée (réserve) quand le mode est Single.")]
    [SerializeField] private int ammoAmount = 30;

    [Header("All Mode (quantités par type)")]
    [SerializeField] private int pistolAmount = 6;
    [SerializeField] private int thompsonAmount = 30;
    [SerializeField] private int shotgunAmount = 2;

    [Header("Collider Assigné (comme LootableWeapon)")]
    [SerializeField] private Collider2D triggerCollider;

    [Header("Hide / Disable (auto si vide)")]
    [SerializeField] private SpriteRenderer[] renderersToHide;
    [SerializeField] private Collider2D[] collidersToDisable;

    [Header("Audio")]
    [SerializeField] private AudioClip pickupSound;
    [Range(0f, 1f)] [SerializeField] private float pickupVolume = 0.8f;

    [Header("UI Toast")]
    [SerializeField] private bool showToast = true;
    [SerializeField, Tooltip("Si activé, affiche un toast uniquement si le pickup a été appliqué à l'arme équipée (aucun toast si les munitions partent en banque).")]
    private bool toastOnlyForEquipped = true;

    [Networked] private NetworkBool Picked { get; set; }

    private void Awake()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider2D>();

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        // Auto-setup si pas assigné
        if (renderersToHide == null || renderersToHide.Length == 0)
            renderersToHide = GetComponentsInChildren<SpriteRenderer>(true);

        if (collidersToDisable == null || collidersToDisable.Length == 0)
            collidersToDisable = GetComponentsInChildren<Collider2D>(true);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Picked) return;

        var pw = other.GetComponentInParent<PlayerWeapon>();
        if (pw == null) return;

        // En réseau : on demande au StateAuthority de valider/appliquer le pickup
        if (Runner != null && Runner.IsRunning)
        {
            var ownerNO = pw.GetComponentInParent<NetworkObject>();
            if (ownerNO != null)
                RPC_RequestPickup(ownerNO);
        }
        else
        {
            // Mode offline
            LocalPickup(pw, true);
        }
    }

    // ================= SERVER =================
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(NetworkObject playerNO)
    {
        if (Picked) return;

        var pw = playerNO ? playerNO.GetComponent<PlayerWeapon>() : null;
        if (pw == null) return;

        LocalPickup(pw, true);
    }

    // Cache instantanément pour tous (évite l’objet “toujours visible”)
    [Rpc(RpcSources.StateAuthority, RpcTargets.All, InvokeLocal = true)]
    private void RPC_HidePickup()
    {
        if (renderersToHide != null)
        {
            foreach (var r in renderersToHide)
                if (r) r.enabled = false;
        }

        if (collidersToDisable != null)
        {
            foreach (var c in collidersToDisable)
                if (c) c.enabled = false;
        }
    }

    // ================= LOGIQUE COMMUNE =================
    private void LocalPickup(PlayerWeapon pw, bool playSound)
    {
        if (Picked) return;

        if (mode == RefillMode.Single)
        {
            int amt = Mathf.Max(0, ammoAmount);
            ApplyOneKind(pw, ammoKind, amt);
        }
        else // RefillMode.All
        {
            if (pistolAmount > 0) ApplyOneKind(pw, AmmoKind.Pistol, pistolAmount);
            if (thompsonAmount > 0) ApplyOneKind(pw, AmmoKind.Thompson, thompsonAmount);
            if (shotgunAmount > 0) ApplyOneKind(pw, AmmoKind.Shotgun, shotgunAmount);
        }

        if (playSound) PlayPickupSound();

        Picked = true;

        // IMPORTANT : on cache tout de suite (pour tous) côté serveur
        if (Runner != null && Runner.IsRunning && Runner.IsServer)
        {
            RPC_HidePickup();

            // despawn 1 frame après (tick-safe)
            StartCoroutine(ServerDespawnNextFrame());
        }
        else
        {
            // Offline fallback : cache + destroy direct
            RPC_HidePickup();
            Destroy(gameObject);
        }
    }

    private IEnumerator ServerDespawnNextFrame()
    {
        yield return null;

        if (Runner != null && Runner.IsRunning && Runner.IsServer && Object != null && Object.IsValid)
            Runner.Despawn(Object);
    }

    private void ApplyOneKind(PlayerWeapon pw, AmmoKind kind, int amount)
    {
        if (amount <= 0) return;

        var wpnGO = pw.GetCurrentWeapon();
        bool appliedToWeapon = false;

        if (wpnGO != null)
        {
            switch (kind)
            {
                case AmmoKind.Pistol:
                {
                    var pistol = wpnGO.GetComponentInChildren<Pistol>(true);
                    if (pistol != null)
                    {
                        pistol.ServerAddReserve(amount);
                        appliedToWeapon = true;

                        if (pistol.AmmoInMag < pistol.MagSize && pistol.ServerGetReserve() > 0)
                            pistol.ServerStartReload(Runner);
                    }
                    break;
                }
                case AmmoKind.Thompson:
                {
                    var th = wpnGO.GetComponentInChildren<Thompson>(true);
                    if (th != null)
                    {
                        th.ServerAddReserve(amount);
                        appliedToWeapon = true;

                        if (th.AmmoInMag < th.MagSize && th.ServerGetReserve() > 0)
                            th.ServerStartReload(Runner);
                    }
                    break;
                }
                case AmmoKind.Shotgun:
                {
                    var sg = wpnGO.GetComponentInChildren<Shotgun>(true);
                    if (sg != null)
                    {
                        sg.ServerAddReserve(amount);
                        appliedToWeapon = true;

                        if (sg.AmmoInMag < sg.MagSize && sg.ServerGetReserve() > 0)
                            sg.ServerStartReload(Runner);
                    }
                    break;
                }
            }
        }

        if (!appliedToWeapon)
        {
            // Pas l’arme correspondante équipée → crédite la banque du joueur
            pw.ServerAddReserveToBank(kind, amount);
        }

        // Toast visuel :
        if (showToast && (!toastOnlyForEquipped || appliedToWeapon))
        {
            pw.RPC_ShowAmmoToast((int)kind, amount);
        }
    }

    // ======= AUDIO =======
    private void PlayPickupSound()
    {
        if (pickupSound == null) return;

        var src = GetComponent<AudioSource>();
        if (src != null) src.PlayOneShot(pickupSound, pickupVolume);
        else AudioSource.PlayClipAtPoint(pickupSound, transform.position, pickupVolume);
    }
}
