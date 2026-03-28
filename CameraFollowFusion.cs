using UnityEngine;
using Fusion;
using System.Collections;

/// <summary>
/// CameraFollowFusion
/// - Suit automatiquement le joueur local (HasInputAuthority).
/// - Lissage time-constant + look-ahead par vitesse.
/// - Zoom API (normal/death).
/// - Mini "choc" caméra (kick) avec retour automatique et fluide à l'état neutre.
/// - Reset propre du kick au respawn/changement de cible.
/// </summary>
[DefaultExecutionOrder(10000)]
public class CameraFollowFusion : MonoBehaviour
{
    [Header("Lissage (constante de temps en secondes)")]
    [Tooltip("Plus petit = plus nerveux. Ex: 0.05 à 0.10")]
    public float smoothTime = 0.06f;

    [Header("Offsets")]
    public Vector2 offset = Vector2.zero;
    public float zOffset = -10f;

    [Header("Cible (VisualRoot recommandé)")]
    public bool useVisualRoot = true;
    public string visualRootName = "VisualRoot";
    public bool autoReacquire = true;
    public float reacquireEvery = 0.25f;

    [Header("Look-ahead (par vitesse de la cible)")]
    public Vector2 leadByVelocity = new Vector2(1.0f, 0.5f);
    public float maxLeadDistance = 1.5f;

    [Header("Zoom (orthographic)")]
    public float normalZoom = 5f;
    public float deathZoom = 8f;
    public float zoomSpeed = 3f;

    [Header("Camera Kick (impact)")]
    [Tooltip("Amplitude du déplacement")]
    public float kickAmplitude = 0.15f;
    [Tooltip("Amplitude de rotation (degrés)")]
    public float kickRotation = 2f;
    [Tooltip("Vitesse d’amortissement du kick")]
    public float kickDecay = 12f;
    [Tooltip("Délai avant retour automatique à la position neutre")]
    public float kickReturnDelay = 1.5f;
    [Tooltip("Durée du retour fluide à la position neutre")]
    public float kickReturnDuration = 0.35f;

    // Runtime
    private Transform playerRoot;
    private Transform target;
    private Vector3 lastTargetPos;
    private bool hasLastPos = false;
    private Camera cam;
    private float targetZoom;

    // Lissage de base (séparé du kick)
    private Vector3 _smoothedPos;
    private bool _hasSmoothed = false;

    // État du kick
    private Vector2 _kickVel;      // vitesse écran (x,y)
    private float   _rotVel;       // vitesse rotation Z (°/s)
    private Vector2 _kickOffset;   // offset accumulé
    private float   _kickRotZ;     // rotation accumulée
    private float   _kickResetTimer; // compte à rebours avant retour auto
    private bool    _returning;      // en phase de retour fluide
    private float   _returnT;        // temps écoulé du retour
    private Quaternion _baseWorldRot;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (!cam) cam = Camera.main;

        targetZoom = normalZoom;
        if (cam) cam.orthographicSize = normalZoom;

        _baseWorldRot = transform.rotation;
        _smoothedPos = transform.position;
        _hasSmoothed = false;
        ClearKick(true);
    }

    private void Start()
    {
        TryFindLocalPlayer(true);
        if (autoReacquire) StartCoroutine(ReacquireLoop());
    }

    private IEnumerator ReacquireLoop()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.05f, reacquireEvery));
        while (true)
        {
            if (!target) TryFindLocalPlayer(false);
            yield return wait;
        }
    }

    private void LateUpdate()
    {
        if (!target) return;

        float dt = Mathf.Max(0.0001f, Time.deltaTime);

        // Look-ahead via vitesse
        Vector3 vel = Vector3.zero;
        if (hasLastPos) vel = (target.position - lastTargetPos) / dt;
        lastTargetPos = target.position;
        hasLastPos = true;

        Vector2 lead = new Vector2(vel.x * leadByVelocity.x, vel.y * leadByVelocity.y);
        if (lead.sqrMagnitude > maxLeadDistance * maxLeadDistance)
            lead = lead.normalized * maxLeadDistance;

        // Position désirée (sans kick)
        Vector3 desired = new Vector3(
            target.position.x + offset.x + lead.x,
            target.position.y + offset.y + lead.y,
            zOffset
        );

        // Lissage time-constant indépendant FPS sur une copie séparée
        float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0002f, smoothTime));
        if (!_hasSmoothed)
        {
            _smoothedPos = desired;
            _hasSmoothed = true;
        }
        else
        {
            _smoothedPos = Vector3.Lerp(_smoothedPos, desired, alpha);
        }

        // Mise à jour du “kick”
        UpdateKick(dt);

        // Position finale = suivi lissé + kick (pas de feedback loop)
        transform.position = _smoothedPos + new Vector3(_kickOffset.x, _kickOffset.y, 0f);

        // Zoom lissé
        if (cam)
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, dt * Mathf.Max(0.01f, zoomSpeed));

        // Rotation due au kick
        transform.rotation = _baseWorldRot * Quaternion.Euler(0f, 0f, _kickRotZ);

        // Retour automatique après délai
        if (_kickResetTimer > 0f)
        {
            _kickResetTimer -= dt;
            if (_kickResetTimer <= 0f && !_returning && (_kickOffset.sqrMagnitude > 0f || Mathf.Abs(_kickRotZ) > 0f))
            {
                // Lance le retour fluide
                _returning = true;
                _returnT = 0f;
            }
        }
    }

    private void UpdateKick(float dt)
    {
        // Pendant la phase active (avant retour), le kick est amorti mais peut garder un offset résiduel
        if (!_returning)
        {
            _kickVel = Vector2.Lerp(_kickVel, Vector2.zero, dt * kickDecay);
            _rotVel  = Mathf.Lerp(_rotVel, 0f, dt * kickDecay);

            _kickOffset += _kickVel * dt;
            _kickRotZ   += _rotVel * dt;

            // Snaps anti-dérive minimes
            if (_kickVel.sqrMagnitude < 1e-6f) _kickVel = Vector2.zero;
            if (Mathf.Abs(_rotVel) < 1e-6f)     _rotVel  = 0f;
        }
        else
        {
            // Retour fluide vers zéro (offset et rotation)
            _returnT += dt;
            float t = Mathf.Clamp01(_returnT / Mathf.Max(0.01f, kickReturnDuration));
            // Smoothstep (0->1)
            t = t * t * (3f - 2f * t);

            _kickOffset = Vector2.Lerp(_kickOffset, Vector2.zero, t);
            _kickRotZ   = Mathf.Lerp(_kickRotZ, 0f, t);

            if (t >= 0.999f)
            {
                // Fin du retour : on snap proprement
                _kickOffset = Vector2.zero;
                _kickRotZ   = 0f;
                _returning  = false;
            }
        }
    }

    /// <summary>
    /// Déclenche un mini “choc caméra” (uniquement pour le joueur local qui subit).
    /// </summary>
    public void KickCamera(float intensity = 1f)
    {
        intensity = Mathf.Clamp(intensity, 0f, 2f);

        // Impulsion initiale
        _kickVel = Random.insideUnitCircle * (kickAmplitude * intensity * 30f);
        _rotVel  = Random.Range(-1f, 1f) * (kickRotation  * intensity * 40f);

        // Redémarre le délai de retour et annule un éventuel retour en cours
        _kickResetTimer = Mathf.Max(0f, kickReturnDelay);
        _returning = false;
        _returnT = 0f;
    }

    /// <summary>Réinitialise tout le kick (appelé au respawn/set target).</summary>
    public void ClearKick(bool hard)
    {
        _kickVel    = Vector2.zero;
        _rotVel     = 0f;
        _kickOffset = Vector2.zero;
        _kickRotZ   = 0f;
        _kickResetTimer = 0f;
        _returning = false;
        _returnT = 0f;
        if (hard) transform.rotation = _baseWorldRot;
    }

    private void TryFindLocalPlayer(bool log)
    {
        // 1) Via NetworkRunner
        NetworkRunner runner = FindObjectOfType<NetworkRunner>();
        if (runner && runner.IsRunning)
        {
            var po = runner.GetPlayerObject(runner.LocalPlayer);
            if (po)
            {
                if (po.TryGetComponent(out PlayerMovement_FusionPro pm))
                {
                    SetTarget(pm.transform, log);
                    return;
                }
                SetTarget(po.transform, log);
                return;
            }
        }

        // 2) Via NetworkObject HasInputAuthority
        foreach (var no in FindObjectsOfType<NetworkObject>())
        {
            if (no && no.HasInputAuthority)
            {
                SetTarget(no.transform, log);
                return;
            }
        }

        // 3) Fallback: PlayerMovement_FusionPro dans la scène
        foreach (var pm in FindObjectsOfType<PlayerMovement_FusionPro>(true))
        {
            if (pm && pm.Object && pm.Object.HasInputAuthority)
            {
                SetTarget(pm.transform, log);
                return;
            }
        }

        if (log) Debug.LogWarning("[CameraFollowFusion] Aucun joueur local trouvé (encore).");
    }

    public void SetTarget(Transform newPlayerRoot) => SetTarget(newPlayerRoot, true);

    private void SetTarget(Transform newPlayerRoot, bool log)
    {
        playerRoot = newPlayerRoot;
        target = playerRoot;

        if (useVisualRoot && playerRoot != null)
        {
            var vr = playerRoot.Find(visualRootName);
            if (vr) target = vr;
        }

        hasLastPos = false;
        _hasSmoothed = false;      // réarme le lissage pour éviter un snap
        ClearKick(true);           // reset du kick au respawn/changement de cible

        if (log && target != null) Debug.Log("[CameraFollowFusion] Cible: " + target.name);
    }

    // API de zoom
    public void SetZoom(bool isDead)   => targetZoom = isDead ? deathZoom : normalZoom;
    public void SetZoom(float newZoom) => targetZoom = Mathf.Max(0.01f, newZoom);
/// <summary>
/// Secousse légère de caméra lors d’un tir (recoil court et rapide).
/// </summary>
public void ShootShake(float intensity = 0.4f)
{
    intensity = Mathf.Clamp(intensity, 0f, 1f);

    // Impulsion très brève, sans rotation, pour un effet de recul léger
    Vector2 dir = Random.insideUnitCircle * 0.3f; // secousse latérale subtile
    _kickVel += dir * (kickAmplitude * intensity * 25f);

    // Petit tremblement vertical en rotation
    _rotVel += Random.Range(-1f, 1f) * (kickRotation * intensity * 20f);

    // On redémarre un timer de retour plus rapide
    _kickResetTimer = 0.25f; // courte durée avant retour
    _returning = false;
    _returnT = 0f;
}

}
