using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class DamageOverlayUI : MonoBehaviour {
    public static DamageOverlayUI Instance { get; private set; }

    [Header("Refs")]
    [SerializeField] private Image overlayImage;

    [Header("Pulse")]
    [SerializeField] private float duration = 0.35f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.45f;
    [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0,0, 0.12f,1);

    private CanvasGroup _group;
    private Coroutine _co;

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (!overlayImage) Debug.LogWarning("[DamageOverlayUI] Assigne overlayImage (Image plein écran).");
        _group = gameObject.GetComponent<CanvasGroup>();
        if (!_group) _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;
    }

    public void Pulse(float strength = 1f) {
        if (!overlayImage) return;
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(CoPulse(Mathf.Clamp01(strength)));
    }

    IEnumerator CoPulse(float strength) {
        float t = 0f;
        float peakT = alphaCurve.keys[alphaCurve.length - 1].time;
        while (t < peakT) {
            t += Time.unscaledDeltaTime;
            _group.alpha = alphaCurve.Evaluate(t) * maxAlpha * strength;
            yield return null;
        }
        float back = Mathf.Max(0.01f, duration - peakT);
        float startA = _group.alpha;
        float e = 0f;
        while (e < back) {
            e += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(startA, 0f, e / back);
            yield return null;
        }
        _group.alpha = 0f;
        _co = null;
    }
}
