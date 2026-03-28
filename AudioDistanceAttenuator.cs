using UnityEngine;
using Fusion;

/// <summary>
/// Atténuation par distance :
/// - Vol max près, 0 loin (min→max)
/// - Option bypass pour le joueur local (sons non atténués)
/// - Mode 2D (volume manuel) ou 3D Unity (CustomRolloff)
/// - Occlusion optionnelle (2D ou 3D)
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AudioDistanceAttenuator : MonoBehaviour
{
    [Header("Distances (m)")]
    public float minDistance = 2f;   // plein volume ≤ min
    public float maxDistance = 22f;  // volume nul ≥ max

    [Header("Courbe (t: 0=min → 1=max)")]
    // 0 => 1 (près = fort) ; 1 => 0 (loin = faible)
    public AnimationCurve attenuation = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Header("Occlusion (optionnel)")]
    public bool enableOcclusion = false;
    public bool usePhysics2D = true;      // ton jeu est 2D
    public LayerMask occluders;
    [Range(0f, 1f)] public float occlusionDampen = 0.5f;

    [Header("Mode")]
    public bool auto3D = false;           // true => spatialBlend=1, rolloff natif
    public float updateHz = 30f;          // fréquence d’update en 2D

    [Header("Local Ownership")]
    public bool bypassIfLocalOwner = true; // n’atténue pas si c’est le joueur local

    private AudioSource _src;
    private float _baseVol = 1f;
    private float _next;
    private NetworkObject _ownerNo;        // pour savoir si c’est le joueur local

    void Awake()
    {
        _src = GetComponent<AudioSource>();
        _baseVol = _src.volume;

        _ownerNo = GetComponentInParent<NetworkObject>();

        if (auto3D)
        {
            _src.spatialBlend = 1f;
            _src.rolloffMode = AudioRolloffMode.Custom;
            _src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, attenuation);
        }
        else
        {
            _src.spatialBlend = 0f; // 2D : on gère le volume à la main
        }
    }

    void Update()
    {
        // 3D : Unity gère le rolloff => juste bypass local si demandé
        if (auto3D)
        {
            if (bypassIfLocalOwner && IsLocalOwner())
            {
                if (_src.volume != _baseVol) _src.volume = _baseVol;
            }
            return;
        }

        // 2D : update à fréquence limitée
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f / Mathf.Max(1f, updateHz);

        // Bypass si c’est le joueur local
        if (bypassIfLocalOwner && IsLocalOwner())
        {
            if (_src.volume != _baseVol) _src.volume = _baseVol;
            return;
        }

        Transform listener = LocalAudioListenerFusion.Listener;
        if (listener == null && Camera.main != null) listener = Camera.main.transform;
        if (listener == null) return;

        // Distance "top-down"
        Vector2 p = new Vector2(transform.position.x, transform.position.y);
        Vector2 l = new Vector2(listener.position.x, listener.position.y);
        float dist = Vector2.Distance(p, l);

        // t=0 à min (près) → 1 à max (loin)
        float t = Mathf.InverseLerp(minDistance, maxDistance, dist);
        float vol = _baseVol * Mathf.Clamp01(attenuation.Evaluate(t));

        if (enableOcclusion && vol > 0f)
        {
            if (usePhysics2D)
            {
                Vector2 dir = l - p;
                if (Physics2D.Raycast(p, dir.normalized, dir.magnitude, occluders))
                    vol *= occlusionDampen;
            }
            else
            {
                Vector3 dir = listener.position - transform.position;
                if (Physics.Raycast(transform.position, dir.normalized, dir.magnitude, occluders))
                    vol *= occlusionDampen;
            }
        }

        _src.volume = vol;
    }

    private bool IsLocalOwner()
    {
        // Si l’objet vient d’un joueur : HasInputAuthority = true côté local
        return _ownerNo != null && _ownerNo.HasInputAuthority;
    }
}
