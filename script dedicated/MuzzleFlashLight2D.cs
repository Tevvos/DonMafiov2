using UnityEngine;
using UnityEngine.Rendering.Universal;

public class MuzzleFlashLight2D : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Light2D muzzleLight;   // Assigne ton Light2D ici (souvent FirePoint/Light2D)

    [Header("Flash Settings")]
    [SerializeField] private float peakIntensity = 2.5f;
    [SerializeField] private float peakRadius = 1.6f;
    [SerializeField] private float riseTime = 0.02f;
    [SerializeField] private float fallTime = 0.06f;
    [SerializeField] private AnimationCurve fallCurve = AnimationCurve.EaseInOut(0,1,1,0);

    [Header("Optional")]
    [SerializeField] private float randomJitter = 0.15f;
    [SerializeField] private bool autoFindLightIfNull = true;
    [SerializeField] private bool previewInEditor = true; // 🌟 garde la lumière visible quand on n’est PAS en Play

    float _baseRadius = 1.0f;
    float _offIntensity = 0f;
    Coroutine _fxCo;

    void OnValidate()
    {
        // Auto-référencement en éditeur
        if (muzzleLight == null && autoFindLightIfNull)
        {
            muzzleLight = GetComponent<Light2D>();
            if (muzzleLight == null)
                muzzleLight = GetComponentInChildren<Light2D>(true);
        }
        if (muzzleLight != null)
            _baseRadius = muzzleLight.pointLightOuterRadius;
    }

    void Awake()
    {
        if (muzzleLight == null && autoFindLightIfNull)
        {
            muzzleLight = GetComponent<Light2D>();
            if (muzzleLight == null)
                muzzleLight = GetComponentInChildren<Light2D>(true);
        }

        if (muzzleLight != null)
        {
            _baseRadius = muzzleLight.pointLightOuterRadius;

            // ⚠️ On n’éteint la lumière qu’en Play. En éditeur, on laisse visible si previewInEditor = true.
            if (Application.isPlaying)
                muzzleLight.intensity = 0f;
            else if (!previewInEditor)
                muzzleLight.intensity = 0f;
        }
    }

    public void TriggerFlash()
    {
        if (muzzleLight == null) return;
        if (_fxCo != null) StopCoroutine(_fxCo);
        _fxCo = StartCoroutine(Co_Flash());
    }

    System.Collections.IEnumerator Co_Flash()
    {
        float targetIntensity = peakIntensity * (1f + Random.Range(-randomJitter, randomJitter));
        float targetRadius = peakRadius * (1f + Random.Range(-randomJitter * 0.5f, randomJitter * 0.5f));

        // Rise
        float t = 0f;
        while (t < riseTime)
        {
            t += Time.deltaTime;
            float k = riseTime > 0f ? (t / riseTime) : 1f;
            muzzleLight.intensity = Mathf.Lerp(_offIntensity, targetIntensity, k);
            muzzleLight.pointLightOuterRadius = Mathf.Lerp(_baseRadius, targetRadius, k);
            yield return null;
        }

        // Fall
        t = 0f;
        while (t < fallTime)
        {
            t += Time.deltaTime;
            float k = fallTime > 0f ? (t / fallTime) : 1f;
            float f = fallCurve.Evaluate(k);
            muzzleLight.intensity = Mathf.Lerp(targetIntensity, _offIntensity, k);
            muzzleLight.pointLightOuterRadius = Mathf.Lerp(targetRadius, _baseRadius, k * (1f - 0.4f * f));
            yield return null;
        }

        muzzleLight.intensity = 0f;
        muzzleLight.pointLightOuterRadius = _baseRadius;
        _fxCo = null;
    }

    // Option: tuning à l'équip par arme
    public void SetPerWeaponTuning(float newPeakIntensity, float newPeakRadius)
    {
        peakIntensity = newPeakIntensity;
        peakRadius = newPeakRadius;
    }

}
