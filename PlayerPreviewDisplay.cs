using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

#if PLAYFAB_PRESENT
using PlayFab;
using PlayFab.ClientModels;
#endif

/// <summary>
/// Affiche le nom + rang du joueur dans le menu preview (non networké).
/// - Si un PlayerRanking est présent : on s'y branche.
/// - Sinon : on lit PlayFab UserData ("RankPoints" / "Rank" / "Reputation") ou PlayerPrefs,
///           puis on calcule l'icône via les seuils locaux.
/// </summary>
[AddComponentMenu("DonMafio/UI/Player Preview Display")]
public class PlayerPreviewDisplay : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image rankIconImage;

    [Header("Source (optionnel)")]
    [Tooltip("Si vide, sera recherché sur ce GameObject ou ses parents.")]
    [SerializeField] private PlayerRanking previewRanking;

    [Header("Seuils de rang (local, pour le preview SANS réseau)")]
    [SerializeField] private int[] rankPointThresholds = { 0, 50, 150, 300, 500, 800, 1200, 1700, 2300, 3000, 3400, 4000 };
    [SerializeField] private string[] rankNames = {
        "Novizio","Recruit","Bodyguard","Soldier","Veteran","Lieutenant",
        "Capo","District Chef","Black Hand","Underboss","Don","King Skull"
    };
    [SerializeField] private Sprite[] rankIcons;

    [Header("Fallback / Options")]
    [SerializeField] private string playerPrefsKey = "Nickname";
    [SerializeField] private string playerPrefsRankKey = "RankPoints_Local";
    [SerializeField] private float playfabWaitTimeout = 5f;   // secondes
    [SerializeField] private bool logDebug = false;

    private void Awake()
    {
        if (!previewRanking)
            previewRanking = GetComponent<PlayerRanking>() ?? GetComponentInParent<PlayerRanking>();
    }

    private void OnEnable()
    {
        PlayerRanking.OnAnyPlayerNameUpdated += RefreshFromRanking;

        // Si un bridge côté PlayFab expose le DisplayName, écoute-le
        if (PlayFabProfileBridge.Instance != null)
            PlayFabProfileBridge.OnDisplayNameChanged += OnBridgeName;

        RefreshFromRanking();
    }

    private void OnDisable()
    {
        PlayerRanking.OnAnyPlayerNameUpdated -= RefreshFromRanking;
        if (PlayFabProfileBridge.Instance != null)
            PlayFabProfileBridge.OnDisplayNameChanged -= OnBridgeName;
    }

    private void Start()
    {
        // Nom
        if (PlayFabProfileBridge.Instance != null)
        {
            var dn = PlayFabProfileBridge.Instance.DisplayName;
            if (!string.IsNullOrWhiteSpace(dn)) OnBridgeName(dn);
            else PlayFabProfileBridge.Instance.FetchDisplayNameAfterLogin();
        }
        else
        {
            string local = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(local)) OnBridgeName(local);
        }

        // Rang/icone
        if (!previewRanking) // seulement si pas de PlayerRanking dispo
            StartCoroutine(CoFetchRankPointsAndApplyIcon());
    }

    /// <summary>Méthode d’UI externe (sans arg).</summary>
    public void UpdateUsernameDisplay()
    {
        RefreshFromRanking();

        if (nameText && (string.IsNullOrWhiteSpace(nameText.text) || nameText.text == "Player"))
        {
            if (PlayFabProfileBridge.Instance != null)
            {
                var dn = PlayFabProfileBridge.Instance.DisplayName;
                if (!string.IsNullOrWhiteSpace(dn)) OnBridgeName(dn);
            }
            else
            {
                string local = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(local)) OnBridgeName(local);
            }
        }

        if (!previewRanking) // si preview pur
            StartCoroutine(CoFetchRankPointsAndApplyIcon());
    }

    /// <summary>Méthode d’UI externe (avec arg) — met à jour nom + pousse dans Ranking si présent.</summary>
    public void UpdateUsernameDisplay(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
        {
            PlayerPrefs.SetString(playerPrefsKey, newName);
            if (previewRanking) previewRanking.SetPlayerName(newName);
            if (nameText) nameText.text = newName;
            if (logDebug) Debug.Log($"[Preview] UpdateUsernameDisplay -> '{newName}'");
        }
    }

    public void SetRanking(PlayerRanking ranking)
    {
        previewRanking = ranking;
        RefreshFromRanking(); // bascule en mode “via ranking”
    }

    private void RefreshFromRanking()
    {
        if (!isActiveAndEnabled) return;

        if (previewRanking)
        {
            string n = string.IsNullOrWhiteSpace(previewRanking.PlayerName) ? "Player" : previewRanking.PlayerName;
            if (nameText) nameText.text = n;

            if (rankIconImage)
            {
                var icon = previewRanking.GetRankIcon();
                rankIconImage.sprite = icon;
                rankIconImage.enabled = icon != null;
            }

            if (logDebug) Debug.Log($"[Preview] via PlayerRanking -> '{n}', icon={(rankIconImage?rankIconImage.sprite:null)}");
        }
        // sinon : le Start() / CoFetchRankPointsAndApplyIcon() s’occupe du rank/icone
    }

    private void OnBridgeName(string newName)
    {
        if (nameText) nameText.text = string.IsNullOrWhiteSpace(newName) ? "Player" : newName;
        if (logDebug) Debug.Log($"[Preview] Bridge name -> '{newName}'");
    }

    private IEnumerator CoFetchRankPointsAndApplyIcon()
    {
        // Si déjà une icône/nom valide, on ne refait pas
        if (rankIconImage && rankIconImage.sprite != null) yield break;

        // 1) essai PlayFab (si présent)
#if PLAYFAB_PRESENT
        float t = 0f;
        while (!PlayFabClientAPI.IsClientLoggedIn() && t < playfabWaitTimeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (PlayFabClientAPI.IsClientLoggedIn())
        {
            bool done = false;
            int best = -1;

            PlayFabClientAPI.GetUserData(new GetUserDataRequest(), res =>
            {
                if (res.Data != null)
                {
                    TryParseKey(res.Data, "RankPoints", ref best);
                    TryParseKey(res.Data, "Rank", ref best);
                    TryParseKey(res.Data, "Reputation", ref best);
                }
                done = true;
            },
            err => { done = true; });

            while (!done) yield return null;

            if (best >= 0)
            {
                ApplyRankIconFromPoints(best);
                if (logDebug) Debug.Log($"[Preview] PlayFab RankPoints -> {best}");
                yield break;
            }
        }
#endif
        // 2) fallback PlayerPrefs local
        int localPts = PlayerPrefs.GetInt(playerPrefsRankKey, 0);
        ApplyRankIconFromPoints(localPts);
        if (logDebug) Debug.Log($"[Preview] Local RankPoints -> {localPts}");
    }

#if PLAYFAB_PRESENT
    private void TryParseKey(System.Collections.Generic.Dictionary<string, UserDataRecord> data, string key, ref int best)
    {
        if (data.TryGetValue(key, out var rec))
        {
            if (int.TryParse(rec.Value, out int val))
                if (val > best) best = val;
        }
    }
#endif

    private void ApplyRankIconFromPoints(int pts)
    {
        if (!rankIconImage) return;
        int idx = GetRankIndexFor(pts);
        if (rankIcons != null && rankIcons.Length > 0)
        {
            idx = Mathf.Clamp(idx, 0, rankIcons.Length - 1);
            rankIconImage.sprite = rankIcons[idx];
            rankIconImage.enabled = rankIconImage.sprite != null;
        }
    }

    private int GetRankIndexFor(int pts)
    {
        if (rankPointThresholds == null || rankPointThresholds.Length == 0)
            return 0;

        int idx = 0;
        for (int i = 0; i < rankPointThresholds.Length; i++)
        {
            if (pts >= rankPointThresholds[i]) idx = i;
            else break;
        }
        return idx;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // sécurise la longueur des arrays pour l’inspecteur
        if (rankNames != null && rankPointThresholds != null && rankPointThresholds.Length != rankNames.Length)
        {
            // on ne redimensionne pas automatiquement ici pour éviter de casser tes icônes;
            // assure-toi que rankIcons a le même compte que rankNames/thresholds.
        }
    }
#endif
}
