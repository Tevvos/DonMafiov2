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
    [SerializeField] private AmmoKind ammoKind = AmmoKind.Thompson;
    [SerializeField] private int ammoAmount = 30;

    [Header("All Mode")]
    [SerializeField] private int pistolAmount   = 6;
    [SerializeField] private int thompsonAmount = 30;
    [SerializeField] private int shotgunAmount  = 2;

    [Header("Collider")]
    [SerializeField] private Collider2D triggerCollider;

    [Header("Hide / Disable")]
    [SerializeField] private SpriteRenderer[] renderersToHide;
    [SerializeField] private Collider2D[] collidersToDisable;

    [Header("Audio")]
    [SerializeField] private AudioClip pickupSound;
    [Range(0f, 1f)] [SerializeField] private float pickupVolume = 0.8f;

    [Header("UI Toast")]
    [SerializeField] private bool showToast = true;
    [SerializeField] private bool toastOnlyForEquipped = true;

    [Networked] private NetworkBool Picked { get; set; }

    private void Awake()
    {
        if (triggerCollider == null) triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null) triggerCollider.isTrigger = true;

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

        if (Runner != null && Runner.IsRunning)
        {
            var ownerNO = pw.GetComponentInParent<NetworkObject>();
            if (ownerNO != null)
                RPC_RequestPickup(ownerNO);
        }
        else
        {
            // Mode offline : applique directement
            if (mode == RefillMode.Single)
                pw.RPC_AddAmmoFromPickup((int)ammoKind, ammoAmount);
            else
            {
                if (pistolAmount   > 0) pw.RPC_AddAmmoFromPickup((int)AmmoKind.Pistol,   pistolAmount);
                if (thompsonAmount > 0) pw.RPC_AddAmmoFromPickup((int)AmmoKind.Thompson, thompsonAmount);
                if (shotgunAmount  > 0) pw.RPC_AddAmmoFromPickup((int)AmmoKind.Shotgun,  shotgunAmount);
            }
            Destroy(gameObject);
        }
    }

    // ── StateAuthority de l'AmmoPickup reçoit la demande ────────────
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(NetworkObject playerNO)
    {
        if (Picked) return;
        var pw = playerNO ? playerNO.GetComponent<PlayerWeapon>() : null;
        if (pw == null) return;

        Picked = true;
        PlayPickupSound();

        // Calcule les munitions à donner
        if (mode == RefillMode.Single)
            ApplyOneKind(pw, ammoKind, Mathf.Max(0, ammoAmount));
        else
        {
            if (pistolAmount   > 0) ApplyOneKind(pw, AmmoKind.Pistol,   pistolAmount);
            if (thompsonAmount > 0) ApplyOneKind(pw, AmmoKind.Thompson, thompsonAmount);
            if (shotgunAmount  > 0) ApplyOneKind(pw, AmmoKind.Shotgun,  shotgunAmount);
        }

        RPC_HidePickup();
        StartCoroutine(StateAuthorityDespawnNextFrame());
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All, InvokeLocal = true)]
    private void RPC_HidePickup()
    {
        if (renderersToHide != null)
            foreach (var r in renderersToHide) if (r) r.enabled = false;
        if (collidersToDisable != null)
            foreach (var c in collidersToDisable) if (c) c.enabled = false;
    }



    private IEnumerator StateAuthorityDespawnNextFrame()
    {
        yield return null;
        if (Runner != null && Runner.IsRunning && Object != null && Object.IsValid)
            Runner.Despawn(Object);
    }

    private void ApplyOneKind(PlayerWeapon pw, AmmoKind kind, int amount)
    {
        if (amount <= 0) return;
        // ✅ Shared : on passe par le RPC du PlayerWeapon qui gère l'autorité correctement
        // ServerAddReserveToBank vérifie HasStateAuthority sur le PlayerWeapon
        // En Shared le SA du PlayerWeapon = le client propriétaire
        // On doit donc envoyer le pickup via un RPC All→StateAuthority sur le PlayerWeapon
        pw.RPC_AddAmmoFromPickup((int)kind, amount);
    }

    private void PlayPickupSound()
    {
        if (pickupSound == null) return;
        var src = GetComponent<AudioSource>();
        if (src != null) src.PlayOneShot(pickupSound, pickupVolume);
        else AudioSource.PlayClipAtPoint(pickupSound, transform.position, pickupVolume);
    }
}
