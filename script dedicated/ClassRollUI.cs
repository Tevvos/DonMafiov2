using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClassRollUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup group;              // le CanvasGroup utilisé pour le fade
    [SerializeField] private TextMeshProUGUI titleText;      // texte de la classe
    [SerializeField] private TextMeshProUGUI subtitleText;   // optionnel
    [SerializeField] private Image classImage;               // image de la classe

    [Header("Sprites par classe")]
    [SerializeField] private Sprite mastodonteSprite;
    [SerializeField] private Sprite dogOfWarSprite;
    [SerializeField] private Sprite furtifSprite;

    [Header("Options d’affichage")]
    [SerializeField] private bool preserveAspect = true;
    [SerializeField, Min(0f)] private float fadeInTime = 0.25f;
    [SerializeField, Min(0f)] private float fadeOutTime = 0.25f;
    [SerializeField] private Vector2 imageMaxSize = new Vector2(420, 420);

    private Coroutine _fadeCo;

    // --- Helper ---
    public bool IsVisible => group && group.alpha > 0.001f;

    private void Awake()
    {
        if (group)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        if (!classImage)
            classImage = GetComponentInChildren<Image>(true);

        if (classImage)
        {
            classImage.enabled = false;
            classImage.preserveAspect = preserveAspect;
            var rt = classImage.rectTransform;
            if (rt)
            {
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, imageMaxSize.x);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, imageMaxSize.y);
            }
        }
    }

    // --- Affichage principal ---
    public void Show(PlayerClassType cls)
    {
        // Texte
        if (titleText)
            titleText.text = GetTitleFor(cls);

        if (subtitleText)
            subtitleText.text = GetSubtitleFor(cls);

        // Image
        if (classImage)
        {
            classImage.sprite = GetSpriteFor(cls);
            classImage.enabled = classImage.sprite != null;
        }

        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(FadeTo(1f, fadeInTime));
    }

    public void Hide()
    {
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(FadeTo(0f, fadeOutTime));
    }

    // --- Coupure immédiate (utilisée quand la partie démarre) ---
    public void HideImmediate()
    {
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        if (!group) return;

        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        if (classImage)
        {
            classImage.enabled = false;
            classImage.sprite = null;
        }
        if (titleText) titleText.text = string.Empty;
        if (subtitleText) subtitleText.text = string.Empty;
    }

    private System.Collections.IEnumerator FadeTo(float target, float duration)
    {
        if (!group) yield break;

        float start = group.alpha;
        float t = 0f;

        group.blocksRaycasts = false;
        group.interactable = false;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, duration > 0f ? t / duration : 1f);
            yield return null;
        }
        group.alpha = target;

        if (Mathf.Approximately(target, 0f))
        {
            if (classImage)
            {
                classImage.enabled = false;
                classImage.sprite = null;
            }
        }
    }

    // --- Helpers ---
    private string GetTitleFor(PlayerClassType cls)
    {
        switch (cls)
        {
            case PlayerClassType.Mastodonte: return "Mastodonte";
            case PlayerClassType.DogOfWar:   return "Dog of War";
            case PlayerClassType.Furtif:     return "Furtif";
            default: return "";
        }
    }

    private string GetSubtitleFor(PlayerClassType cls)
    {
        switch (cls)
        {
            case PlayerClassType.Mastodonte: return "Bouclier humain. Force brute.";
            case PlayerClassType.DogOfWar:   return "Toujours armé, jamais à court.";
            case PlayerClassType.Furtif:     return "Vitesse et discrétion.";
            default: return "";
        }
    }

    private Sprite GetSpriteFor(PlayerClassType cls)
    {
        switch (cls)
        {
            case PlayerClassType.Mastodonte: return mastodonteSprite;
            case PlayerClassType.DogOfWar:   return dogOfWarSprite;
            case PlayerClassType.Furtif:     return furtifSprite;
            default: return null;
        }
    }
}
