using UnityEngine;
using System.Collections;

public class ScreenFader : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private CanvasGroup canvasGroup; // pour le fade noir
    [SerializeField] private CanvasGroup overlayCanvasGroup; // overlay lobby

    [Header("Durées")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.8f;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.5f;
    [SerializeField, Min(0f)] private float overlayFadeOutDuration = 1f;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        gameObject.SetActive(false);
        canvasGroup.alpha = 0f;
    }

    // ============================
    // Fondu Noir (avant le match)
    // ============================

    public IEnumerator FadeOut()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        float t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeOutDuration);
            canvasGroup.alpha = a;
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }

    public IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / fadeInDuration);
            canvasGroup.alpha = a;
            yield return null;
        }
        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    // ================================
    // Overlay du Lobby (disparition)
    // ================================

    /// <summary>
    /// Fait disparaître l’overlay du lobby avec un fondu et un délai optionnel.
    /// </summary>
    public void FadeOutOverlay(float delay = 0f)
    {
        if (overlayCanvasGroup == null) return;
        StopAllCoroutines();
        StartCoroutine(FadeOutOverlayCoroutine(delay));
    }

    private IEnumerator FadeOutOverlayCoroutine(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        float startAlpha = overlayCanvasGroup.alpha;
        float t = 0f;

        while (t < overlayFadeOutDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(startAlpha, 0f, t / overlayFadeOutDuration);
            overlayCanvasGroup.alpha = a;
            yield return null;
        }

        overlayCanvasGroup.alpha = 0f;
        overlayCanvasGroup.gameObject.SetActive(false);
    }
}
