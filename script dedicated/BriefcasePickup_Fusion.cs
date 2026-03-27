using Fusion;
using UnityEngine;

public class BriefcasePickup_Fusion : NetworkBehaviour
{
    [Header("Pickup")]
    [SerializeField] private float effectDuration = 30f;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 30f;

    [Header("Visual/Collider to toggle")]
    [SerializeField] private SpriteRenderer[] renderers;
    [SerializeField] private Collider2D[] colliders;

    [Header("Camera Zoom Effect (Orthographic Size)")]
    [Tooltip("Plus grand = la caméra recule. Ex: normal 5 -> effet 8")]
    [SerializeField] private float zoomOutOrthoSize = 8f;

    [Tooltip("Vitesse du zoom (lerp). Plus grand = plus rapide.")]
    [SerializeField] private float zoomLerpSpeed = 6f;

    [Header("Zoom Pulse (optional)")]
    [SerializeField] private bool enableZoomPulse = false;
    [Tooltip("Amplitude du pulse (ex 0.15 = léger).")]
    [SerializeField] private float pulseAmplitude = 0.15f;
    [Tooltip("Vitesse du pulse (ex 2 = lent, 4 = plus rapide).")]
    [SerializeField] private float pulseSpeed = 2.5f;

    [Header("Pickup Kick (optional)")]
    [SerializeField] private bool enablePickupKick = true;
    [Tooltip("Kick instantané au pickup (en ortho size). Ex: 0.35")]
    [SerializeField] private float pickupKickAmount = 0.35f;

    // Networked
    [Networked] private NetworkBool IsAvailable { get; set; } = true;
    [Networked] private TickTimer RespawnTimer { get; set; }
    [Networked] private TickTimer EffectTimer { get; set; }
    [Networked] private PlayerRef EffectOwner { get; set; }

    // Local camera override (par client)
    private Camera _localCam;
    private float _defaultOrthoSize;
    private bool _defaultCached;

    private bool _effectActiveLocal;
    private float _currentZoom;
    private float _kickVel; // velocity pour amortir le kick
    private float _pulseT;

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<SpriteRenderer>(true);

        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider2D>(true);

        foreach (var c in colliders)
            if (c) c.isTrigger = true;
    }

    public override void Spawned()
    {
        ApplyAvailability(IsAvailable);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsAvailable) return;

        var playerNetObj = other.GetComponentInParent<NetworkObject>();
        if (playerNetObj == null) return;

        var who = playerNetObj.InputAuthority;
        if (who == PlayerRef.None) return;

        RPC_RequestPickup(who);
    }

    // ================= SERVER =================

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(PlayerRef who)
    {
        if (!IsAvailable) return;

        IsAvailable = false;
        ApplyAvailability(false);

        EffectOwner = who;
        EffectTimer = TickTimer.CreateFromSeconds(Runner, effectDuration);
        RPC_ApplyEffect(who, true);

        if (respawnDelay > 0f)
            RespawnTimer = TickTimer.CreateFromSeconds(Runner, respawnDelay);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        if (EffectTimer.IsRunning && EffectTimer.Expired(Runner))
        {
            RPC_ApplyEffect(EffectOwner, false);
            EffectTimer = default;
            EffectOwner = PlayerRef.None;
        }

        if (!IsAvailable && respawnDelay > 0f && RespawnTimer.Expired(Runner))
        {
            IsAvailable = true;
            ApplyAvailability(true);
        }
    }

    // ================= CLIENT LOCAL =================

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyEffect(PlayerRef who, bool enable)
    {
        if (Runner.LocalPlayer != who) return;

        var fx = FindFirstObjectByType<FocusMalletteEffect>();
        if (fx != null)
            fx.EnableFocus(enable);

        CacheLocalCameraIfNeeded();
        if (_localCam == null || !_localCam.orthographic || !_defaultCached) return;

        // initialise l’état local
        _effectActiveLocal = enable;
        _pulseT = 0f;

        // kickoff : on prend la valeur actuelle comme base
        _currentZoom = _localCam.orthographicSize;

        // petit kick au moment du pickup (uniquement quand ça s’active)
        if (enable && enablePickupKick)
        {
            _currentZoom = Mathf.Max(0.01f, _currentZoom + pickupKickAmount);
            _localCam.orthographicSize = _currentZoom;
        }

        // si on coupe -> on laisse LateUpdate ramener smooth vers default
        // (pas de snap)
    }

    private void CacheLocalCameraIfNeeded()
    {
        if (_localCam == null)
            _localCam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        if (!_defaultCached && _localCam != null && _localCam.orthographic)
        {
            _defaultOrthoSize = _localCam.orthographicSize;
            _currentZoom = _defaultOrthoSize;
            _defaultCached = true;
        }
    }

    private void LateUpdate()
    {
        // On force le zoom chaque frame pour éviter que CameraFollowFusion l’écrase
        CacheLocalCameraIfNeeded();
        if (_localCam == null || !_localCam.orthographic || !_defaultCached) return;

        float target = _effectActiveLocal ? zoomOutOrthoSize : _defaultOrthoSize;

        // pulse léger (optionnel) quand l’effet est actif
        if (_effectActiveLocal && enableZoomPulse)
        {
            _pulseT += Time.deltaTime * pulseSpeed;
            float pulse = Mathf.Sin(_pulseT) * pulseAmplitude;
            target += pulse;
        }

        // Lerp smooth vers la cible
        // (zoomLerpSpeed = vitesse, stable même si FPS varie)
        float t = 1f - Mathf.Exp(-zoomLerpSpeed * Time.deltaTime);
        _currentZoom = Mathf.Lerp(_currentZoom, target, t);

        _localCam.orthographicSize = _currentZoom;
    }

    // ================= UTILS =================

    private void ApplyAvailability(bool available)
    {
        if (renderers != null)
            foreach (var r in renderers)
                if (r) r.enabled = available;

        if (colliders != null)
            foreach (var c in colliders)
                if (c) c.enabled = available;
    }
}
