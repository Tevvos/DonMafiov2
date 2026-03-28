using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthBarUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image fillImage;
    [SerializeField] private TextMeshProUGUI valueText;

    [Header("Options")]
    [SerializeField] private Gradient colorByRatio;
    [Tooltip("Cache la barre tant qu'on est au lobby.")]
    [SerializeField] private bool hideInLobby = true;

    private CanvasGroup _cg;
    private bool _visibleInitialized = false;

    void Awake()
    {
        // CanvasGroup pour gérer proprement la visibilité UI
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();

        // Au démarrage on applique l’état voulu (lobby => caché)
        ApplyVisible(!hideInLobby);
        _visibleInitialized = true;
    }

    void OnEnable()
    {
        // Si l’objet est réactivé avant Awake (rare), on sécurise
        if (!_visibleInitialized)
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
            ApplyVisible(!hideInLobby);
            _visibleInitialized = true;
        }
    }

    /// <summary>Affiche la barre (appelé par MatchFlow quand la partie commence).</summary>
    public void Show() => ApplyVisible(true);

    /// <summary>Cache la barre (appelé par MatchFlow quand on est au lobby / retour lobby).</summary>
    public void Hide() => ApplyVisible(false);

    private void ApplyVisible(bool visible)
    {
        if (_cg == null) return;
        _cg.alpha = visible ? 1f : 0f;
        _cg.interactable = visible;
        _cg.blocksRaycasts = visible;
        // On garde l'objet actif pour permettre FindObjectOfType(includeInactive:true) côté flow.
    }

    /// <summary>Mise à jour visuelle des PV.</summary>
    public void SetHealth(float current, float max)
    {
        if (fillImage == null || max <= 0f) return;

        float ratio = Mathf.Clamp01(current / max);

        fillImage.fillAmount = ratio;

        if (colorByRatio != null)
            fillImage.color = colorByRatio.Evaluate(ratio);

        if (valueText != null)
            valueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
    }

    // Aides debug dans l’Inspector (clic droit sur le composant)
    [ContextMenu("Debug/Show")]
    private void CtxShow() => Show();

    [ContextMenu("Debug/Hide")]
    private void CtxHide() => Hide();
}
