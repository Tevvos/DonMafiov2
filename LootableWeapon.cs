using UnityEngine;
using Fusion;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider2D))]
public class LootableWeapon : NetworkBehaviour
{
    // --- Networked state ---
    [Networked] public NetworkString<_16> NetWeaponName { get; set; }
    [Networked] public PlayerRef NetDropper { get; set; }
    [Networked] public TickTimer PickupDelay { get; set; }

    // --- Config ---
    [Header("Config")]
    [SerializeField] private string overrideWeaponName = string.Empty;
    [SerializeField] private Collider2D triggerCollider;
    [SerializeField] private float requireExitDistance = 0.6f;

    [Header("Ammo on Pickup")]
    [Tooltip("Taille du chargeur donnée au joueur quand il ramasse cette arme.")]
    [SerializeField] private int magSizeOnPickup = 8;

    [Header("Auto Despawn")]
    [Tooltip("Temps avant disparition automatique du loot (0 = jamais).")]
    [SerializeField] private float autoDespawnTime = 60f;

    // --- Visuals ---
    [Header("Visual")]
    [SerializeField] private SpriteRenderer visualRenderer;
    [SerializeField] private Vector3 visualOffset = new Vector3(0f, -0.05f, 0f);
    [SerializeField] private bool usePrefabSprite = true;
    [SerializeField] private string iconsFolder = "WeaponIcons";
    [SerializeField] private AudioClip pickupSound;
    [SerializeField] private bool useDropPop = true;
    [SerializeField] private bool showDisabledTint = true;

    // --- Runtime ---
    private bool _visualBuilt = false;
    private bool _mustExit = false;
    private bool _exitSatisfied = false;

    private static readonly Dictionary<string, Sprite> spriteCache = new();

    // === Initialization ===
    public void InitByName(string nameFromServer)
    {
        overrideWeaponName = nameFromServer;
        if (Object && Object.HasStateAuthority)
            NetWeaponName = nameFromServer;

        // 🟩 Force rebuild du visuel tout de suite
        BuildVisual();
    }

    public void InitDropper(PlayerRef dropper, float delaySeconds, NetworkRunner runner = null)
    {
        if (!Object || !Object.HasStateAuthority) return;

        NetDropper = dropper;
        var r = runner ?? Runner;
        if (r != null)
            PickupDelay = TickTimer.CreateFromSeconds(r, Mathf.Max(0.01f, delaySeconds));

        _mustExit = true;
        _exitSatisfied = false;

        // --- Offset pour placer l'arme au sol ---
        Vector3 dropPos = transform.position;
        dropPos.y -= 0.25f; // 🟩 corrige la hauteur d’apparition
        transform.position = dropPos;

        // --- Effet rebond ou éjection ---
        if (TryGetComponent(out Rigidbody2D rb))
        {
            float randomDir = Random.Range(-1f, 1f);
            rb.AddForce(new Vector2(randomDir, 1f) * 1.5f, ForceMode2D.Impulse);
            rb.AddTorque(Random.Range(-5f, 5f), ForceMode2D.Impulse);
        }

        // --- Despawn automatique ---
        if (autoDespawnTime > 0f)
            StartCoroutine(AutoDespawnCoroutine());
    }

    private IEnumerator AutoDespawnCoroutine()
    {
        yield return new WaitForSeconds(autoDespawnTime);
        DespawnSelf();
    }

    private void DespawnSelf()
    {
        if (Object != null && Object.IsValid)
            Runner.Despawn(Object);
        else if (this != null)
            Destroy(gameObject);
    }

    private string ResolveWeaponName()
    {
        var n = NetWeaponName.ToString();
        return string.IsNullOrEmpty(n) ? overrideWeaponName : n;
    }

    // === Fusion hooks ===
    public override void Spawned()
    {
        if (triggerCollider == null) triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider) triggerCollider.isTrigger = true;

        BuildVisual();

        if (useDropPop) StartCoroutine(DropPopEffect());
    }

    public override void Render()
    {
        // 🟩 Si le nom est arrivé après le spawn, on reconstruit le visuel
        if (!_visualBuilt && !string.IsNullOrEmpty(ResolveWeaponName()))
            BuildVisual();

        // Changement de couleur selon disponibilité
        if (visualRenderer && showDisabledTint)
        {
            bool canPickup = !_mustExit || (_exitSatisfied && !PickupDelay.IsRunning);
            visualRenderer.color = canPickup ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.6f);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object || !Object.HasStateAuthority || !_mustExit || _exitSatisfied || NetDropper == default)
            return;

        if (!Runner.TryGetPlayerObject(NetDropper, out var pObj) || pObj == null)
            return;

        float sqrDist = (pObj.transform.position - transform.position).sqrMagnitude;
        if (sqrDist >= requireExitDistance * requireExitDistance)
            _exitSatisfied = true;
    }

    // === Visual building ===
    private void BuildVisual()
    {
        if (visualRenderer == null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null)
            {
                var go = new GameObject("Visual");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = visualOffset;
                sr = go.AddComponent<SpriteRenderer>();
            }
            visualRenderer = sr;
        }

        // Préfère le sprite du prefab
        if (usePrefabSprite && visualRenderer.sprite != null)
        {
            visualRenderer.enabled = true;
            _visualBuilt = true;
            return;
        }

        // Sinon, tente de charger depuis Resources/WeaponIcons
        string wName = ResolveWeaponName();
        Sprite sprite = GetWeaponSprite(wName);
        if (sprite != null)
        {
            visualRenderer.sprite = sprite;
            visualRenderer.enabled = true;
        }

        _visualBuilt = true;
    }

    private Sprite GetWeaponSprite(string wName)
    {
        if (string.IsNullOrEmpty(wName) || string.IsNullOrEmpty(iconsFolder))
            return null;

        if (spriteCache.TryGetValue(wName, out var cached))
            return cached;

        Sprite sprite = Resources.Load<Sprite>(iconsFolder + "/" + wName);
        if (sprite != null)
            spriteCache[wName] = sprite;
        return sprite;
    }

    // === Pickup rules ===
    private void OnTriggerEnter2D(Collider2D other)
    {
        var pw = other.GetComponentInParent<PlayerWeapon>();
        if (pw == null || pw.Object == null) return;

        // ✅ Shared : n'importe quel client peut demander le pickup via RPC
        // Le StateAuthority valide et applique
        var playerNO = pw.GetComponentInParent<NetworkObject>();
        if (playerNO != null)
            RPC_RequestPickup(playerNO);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(NetworkObject playerNO)
    {
        if (!Object || !Object.HasStateAuthority) return;
        if (!playerNO) return;

        var pw = playerNO.GetComponent<PlayerWeapon>();
        if (pw == null) return;

        // Empêche le dropper de ramasser instantanément
        if (playerNO.InputAuthority == NetDropper)
        {
            if (!_exitSatisfied || PickupDelay.IsRunning) return;
        }

        string wName = ResolveWeaponName();
        if (string.IsNullOrEmpty(wName)) return;

        // Donne l'arme à tous
        pw.ServerEquipWeaponForAll(wName);
        pw.SetMagFromLoot(magSizeOnPickup);

        // Son de pickup
        if (pickupSound != null)
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);

        // Détruit le loot
        DespawnSelf();
    }

    // === Effets visuels ===
    private IEnumerator DropPopEffect()
    {
        if (visualRenderer == null) yield break;
        Vector3 start = transform.localScale * 0.6f;
        Vector3 end = transform.localScale;
        float t = 0f;
        while (t < 0.2f)
        {
            transform.localScale = Vector3.Lerp(start, end, t / 0.2f);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = end;
    }
}
