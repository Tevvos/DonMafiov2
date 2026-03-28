using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class SimpleFader : MonoBehaviour
{
    public static SimpleFader Instance { get; private set; }

    [SerializeField] float defaultDuration = 0.35f;
    [SerializeField] AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    CanvasGroup cg;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildOverlayIfNeeded();
        SetAlpha(0f); // transparent au démarrage
    }

    void BuildOverlayIfNeeded()
    {
        var canvasGO = new GameObject("FaderCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50000;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        cg = canvasGO.GetComponent<CanvasGroup>();

        var imgGO = new GameObject("Black", typeof(RectTransform), typeof(Image));
        imgGO.transform.SetParent(canvasGO.transform, false);
        var rt = imgGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        var img = imgGO.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = true; // bloque les clics quand noir
    }

    void SetAlpha(float a)
    {
        a = Mathf.Clamp01(a);
        cg.alpha = a;
        bool block = a > 0.001f;
        cg.blocksRaycasts = block;
        cg.interactable = block;
    }

    IEnumerator FadeTo(float target, float duration)
    {
        float start = cg.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            float eased = Mathf.Lerp(start, target, curve.Evaluate(k));
            SetAlpha(eased);
            yield return null;
        }
        SetAlpha(target);
    }

    public Coroutine FadeToBlack(float duration = -1f)
    {
        if (duration < 0f) duration = defaultDuration;
        return StartCoroutine(FadeTo(1f, duration));
    }

    public Coroutine FadeFromBlack(float duration = -1f)
    {
        if (duration < 0f) duration = defaultDuration;
        return StartCoroutine(FadeTo(0f, duration));
    }

    public static void Ensure()
    {
        if (Instance != null) return;
        var go = new GameObject("SimpleFader");
        go.AddComponent<SimpleFader>();
    }

    // ---------------------
    // 👇 Fonctions visibles dans Button.onClick
    // ---------------------

    public void OnClickFadeToBlack()
    {
        Ensure();
        Instance.FadeToBlack();
    }

    public void OnClickFadeFromBlack()
    {
        Ensure();
        Instance.FadeFromBlack();
    }
}
