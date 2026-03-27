using UnityEngine;

/// <summary>
/// Applique un angle Z lissé de manière indépendante au framerate.
/// Utilisé par PlayerWeapon pour orienter l'arme côté client/proxy.
/// </summary>
public class WeaponNetworkSync : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform weaponTransform;

    [Header("Tuning")]
    [SerializeField] private float extraZOffset = 0f;
    [SerializeField] private float smoothingSpeed = 16f; // 1/s
    [SerializeField] private bool useSpriteFlip = false;

    private void Awake()
    {
        if (weaponTransform == null) weaponTransform = transform;
    }

    public void SetExtraZOffset(float z) => extraZOffset = z;
    public void SetUseSpriteFlip(bool v) => useSpriteFlip = v;

    /// <param name="angleDeg">angle visé en degrés (Z)</param>
    /// <param name="smooth">true : expo smoothing; false : set direct</param>
    public void ApplyAngle(float angleDeg, bool smooth)
    {
        float targetZ = angleDeg + extraZOffset;
        if (!weaponTransform) return;

        if (smooth)
        {
            float t = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
            float z = Mathf.LerpAngle(weaponTransform.eulerAngles.z, targetZ, t);
            SetRotationZ(z);
        }
        else
        {
            SetRotationZ(targetZ);
        }

        // Sprite flip optionnel (non utilisé actuellement, mais dispo)
        if (useSpriteFlip)
        {
            var sr = weaponTransform.GetComponentInChildren<SpriteRenderer>(true);
            if (sr)
            {
                // Flip en fonction de l'angle global pour éviter la rotation à 180°
                float zNorm = Mathf.DeltaAngle(0f, weaponTransform.eulerAngles.z);
                bool flipY = (zNorm > 90f || zNorm < -90f);
                sr.flipY = flipY;
            }
        }
    }

    private void SetRotationZ(float z)
    {
        var e = weaponTransform.eulerAngles;
        e.z = z;
        weaponTransform.eulerAngles = e;
    }
}
