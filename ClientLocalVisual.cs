using UnityEngine;
using Fusion;

/// <summary>
/// ClientLocalVisual — smoothing ONLY (ne touche pas au gameplay)
/// </summary>
public class ClientLocalVisual : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform visualRoot;

    [Header("Smoothing")]
    [SerializeField] private float ownerLerpSpeed = 15f;    // 1/s
    [SerializeField] private float proxySmoothTime = 0.08f; // s

    private void Awake()
    {
        if (!visualRoot) visualRoot = transform.Find("VisualRoot");
        if (!visualRoot) visualRoot = transform;
    }

    public override void Render()
    {
        if (!visualRoot) return;

        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        bool isOwner = Object != null && Object.HasInputAuthority;

        if (isOwner)
        {
            float t = 1f - Mathf.Exp(-ownerLerpSpeed * dt);
            visualRoot.position = Vector3.Lerp(visualRoot.position, transform.position, t);
        }
        else
        {
            float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0002f, proxySmoothTime));
            visualRoot.position = Vector3.Lerp(visualRoot.position, transform.position, alpha);
        }
    }

    /// <summary>Appelé au respawn pour supprimer tout décalage visuel latent.</summary>
    public void SnapToTransform()
    {
        if (!visualRoot) return;
        visualRoot.position = transform.position;
    }
}
