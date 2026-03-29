using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class WantedDisplay : MonoBehaviour
{
    [Header("🗂️ UI Elements")]
    [SerializeField] private RectTransform panelTransform;
    [SerializeField] private Image dossierImage;
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private TMP_Text playerRankText;
    [SerializeField] private TMP_Text bountyText;
    [SerializeField] private Image characterIcon;

    [Header("📍 Positions d'animation")]
    [SerializeField] private Vector2 offScreenPosition = new Vector2(300f, 40f);
    [SerializeField] private Vector2 onScreenPosition = new Vector2(-40f, 40f);

    // FIX : les logs de debug heartbeat sont désactivables depuis l'Inspector
    // Désactive en production pour ne pas polluer la console
    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private PlayerRanking _lastLeader;
    private int _lastLeaderKills = -1;
    private bool _isShown;

    private void OnEnable()
    {
        if (panelTransform == null)
            Debug.LogWarning("[WantedDisplay] ⚠ panelTransform non assigné dans l'Inspector.");

        if (panelTransform != null)
            panelTransform.anchoredPosition = offScreenPosition;
        _isShown = false;
    }

    private void Start()
    {
        if (panelTransform != null)
            panelTransform.anchoredPosition = offScreenPosition;
    }

    private void Update()
    {
        var leader = LeaderTracker.CurrentLeader;

        if (leader == null)
        {
            if (_isShown)
            {
                if (verboseLogs) Debug.Log("[WantedDisplay] 🔕 Aucun leader → cacher panneau");
                Show(false);
            }
            _lastLeader = null;
            _lastLeaderKills = -1;
            return;
        }

        bool changed = leader != _lastLeader || leader.killCount != _lastLeaderKills;
        if (!changed) return;

        _lastLeader = leader;
        _lastLeaderKills = leader.killCount;

        string name = leader.PlayerName;
        string rank = leader.GetCurrentRankName();
        int bounty = leader.killCount * 1000;
        Sprite rankIcon = leader.GetRankIcon();

        if (verboseLogs)
            Debug.Log($"[WantedDisplay] 🎯 UpdateDisplay pour {name}, kills={leader.killCount}, prime={bounty}$");

        UpdateDisplay(name, rank, bounty, rankIcon);
    }

    public void UpdateDisplay(string playerName, string playerRank, int bountyAmount, Sprite icon = null)
    {
        if (playerNameText != null) playerNameText.text = playerName;
        if (playerRankText != null) playerRankText.text = $"Rank: {playerRank}";
        if (bountyText != null)     bountyText.text     = $"{bountyAmount} $";

        if (characterIcon != null && icon != null)
            characterIcon.sprite = icon;

        Show(true);
    }

    public void Show(bool show)
    {
        if (panelTransform == null)
        {
            Debug.LogWarning("[WantedDisplay] ⚠ panelTransform null → impossible d'animer.");
            return;
        }

        if (_isShown == show) return;
        _isShown = show;

        float from = show ? offScreenPosition.x : onScreenPosition.x;
        float to   = show ? onScreenPosition.x  : offScreenPosition.x;
        float duration = show ? 0.8f : 0.5f;
        var ease = show ? LeanTweenType.easeOutExpo : LeanTweenType.easeInBack;

#if LEANTWEEN_OMIT
        panelTransform.anchoredPosition = new Vector2(to, onScreenPosition.y);
#else
        try
        {
            LeanTween.value(panelTransform.gameObject, from, to, duration)
                .setEase(ease)
                .setOnUpdate((float val) =>
                {
                    if (panelTransform != null)
                        panelTransform.anchoredPosition = new Vector2(val, onScreenPosition.y);
                });
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[WantedDisplay] LeanTween indisponible → position directe. " + e.Message);
            panelTransform.anchoredPosition = new Vector2(to, onScreenPosition.y);
        }
#endif
    }
}
