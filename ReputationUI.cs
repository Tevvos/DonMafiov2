using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Badge d’affichage : [Icône]  "Rank  |  1 234 RP"
/// Source de vérité = PlayerRanking (owner local). Fallback = ReputationManager.
/// À déposer dans MultiplayerScene & GameScene01 (pas networké).
/// </summary>
[DisallowMultipleComponent]
public class ReputationUI : MonoBehaviour {
    [Header("Références UI")]
    [SerializeField] private Image icon;                // petite icône (optionnel)
    [SerializeField] private TextMeshProUGUI label;     // TMP affichant "Rank | RP"

    [Header("Format")]
    [SerializeField] private string format = "{0}  |  {1} RP"; // {0}=nom rang, {1}=réputation (N0)

    [Header("Icônes optionnelles par rang")]
    [SerializeField] private bool useRankIcons = false;
    [SerializeField] private Sprite[] rankIcons; // optionnel : 1 sprite par rang (index 0..)

    // --- Source préférée ---
    private PlayerRanking _localRanking;

    void OnEnable() {
        TryAutoBind();
        BindLocalRanking();
        RefreshNow();

        // s’abonner aux events (Ranking prioritaire)
        PlayerRanking.OnPointsChanged += OnPointsChanged;
        if (ReputationManager.Instance != null) {
            ReputationManager.Instance.OnReputationOrRankChanged += HandleRepManChanged;
        }
    }

    void OnDisable() {
        PlayerRanking.OnPointsChanged -= OnPointsChanged;
        if (ReputationManager.Instance != null) {
            ReputationManager.Instance.OnReputationOrRankChanged -= HandleRepManChanged;
        }
    }

    void Update() {
        // filet de sécurité: si on a perdu la ref (respawn, scène), on rebind
        if (_localRanking == null) {
            BindLocalRanking();
        }
    }

    private void TryAutoBind() {
        if (label == null) label = GetComponentInChildren<TextMeshProUGUI>(true);
        if (icon == null)  icon  = GetComponentInChildren<Image>(true);
    }

    private void BindLocalRanking() {
        _localRanking = null;
        var all = FindObjectsOfType<PlayerRanking>(true);
        foreach (var pr in all) {
            if (pr && pr.Object && pr.Object.HasInputAuthority) {
                _localRanking = pr;
                break;
            }
        }
    }

    private void OnPointsChanged(PlayerRanking pr, int newPoints, int rankIndex) {
        // On ne rafraîchit que pour le propriétaire local (affiché sur CE HUD)
        if (_localRanking != null && pr == _localRanking) {
            RefreshNow();
        }
    }

    private void HandleRepManChanged(int rep, int rankIndex) {
        // Fallback si jamais le HUD s’appuie encore sur ReputationManager
        RefreshNow();
    }

    [ContextMenu("Force Refresh")]
    public void RefreshNow() {
        if (label == null) return;

        // 1) Source de vérité : PlayerRanking du propriétaire local
        if (_localRanking != null) {
            int points = _localRanking.GetPointsValue();
            string name = _localRanking.GetCurrentRankName();
            label.text = string.Format(format, name, points.ToString("N0"));

            if (useRankIcons && icon != null && rankIcons != null && rankIcons.Length > 0) {
                int idx = Mathf.Clamp(_localRanking.GetCurrentRankIndex(), 0, rankIcons.Length - 1);
                if (rankIcons[idx] != null) icon.sprite = rankIcons[idx];
            }
            return;
        }

        // 2) Fallback : ReputationManager (si jamais le Ranking local n’est pas dispo)
        if (ReputationManager.Instance != null) {
            var rep  = ReputationManager.Instance.Reputation;
            var name = ReputationManager.Instance.GetRankName();
            label.text = string.Format(format, name, rep.ToString("N0"));

            if (useRankIcons && icon != null && rankIcons != null && rankIcons.Length > 0) {
                int idx = Mathf.Clamp(ReputationManager.Instance.RankIndex, 0, rankIcons.Length - 1);
                if (rankIcons[idx] != null) icon.sprite = rankIcons[idx];
            }
        }
    }
}
