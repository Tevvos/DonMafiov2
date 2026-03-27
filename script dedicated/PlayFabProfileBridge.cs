using UnityEngine;
using System;
#if PLAYFAB_PRESENT
using PlayFab;
using PlayFab.ClientModels;
#endif

/// Bridge global entre PlayFab et l'UI / gameplay.
/// Place-le dans ta scène de boot (ou crée-le via un Bootstrapper) et coche DontDestroyOnLoad.
public class PlayFabProfileBridge : MonoBehaviour
{
    public static PlayFabProfileBridge Instance { get; private set; }

    public static event Action<string> OnDisplayNameChanged; // broadcast pseudo

    public string DisplayName { get; private set; } = string.Empty;
    [SerializeField] private string playerPrefsKey = "Nickname";
    [SerializeField] private string playFabTitleId = "1DA3D3"; // <- ton TitleID

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
#if PLAYFAB_PRESENT
        if (string.IsNullOrWhiteSpace(PlayFabSettings.staticSettings.TitleId))
            PlayFabSettings.staticSettings.TitleId = playFabTitleId;
#endif
    }

    /// Appelle ceci **juste après** un login PlayFab réussi (ou depuis ta Startscreen quand tu sais que tu es loggé).
    public void FetchDisplayNameAfterLogin()
    {
#if PLAYFAB_PRESENT
#if !PLAYFAB_DISABLE_ISCLIENTLOGGEDIN_CHECK
        if (!PlayFabClientAPI.IsClientLoggedIn())
        {
            Debug.LogWarning("[Bridge] PlayFab non connecté, impossible de fetch le DisplayName.");
            // On tente quand même un fallback local
            UseLocalOrFallback();
            return;
        }
#endif
        PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest(), res =>
        {
            string dn = res?.AccountInfo?.TitleInfo?.DisplayName;
            if (string.IsNullOrWhiteSpace(dn))
            {
                Debug.LogWarning("[Bridge] DisplayName vide côté PlayFab. On garde local/fallback.");
                UseLocalOrFallback();
                return;
            }
            ApplyDisplayName(dn);
        },
        err =>
        {
            Debug.LogWarning($"[Bridge] GetAccountInfo fail: {err.ErrorMessage} -> fallback local");
            UseLocalOrFallback();
        });
#else
        Debug.Log("[Bridge] PLAYFAB_PRESENT non défini -> fallback local uniquement.");
        UseLocalOrFallback();
#endif
    }

    private void UseLocalOrFallback()
    {
        string local = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(local))
            local = "Player";
        ApplyDisplayName(local);
    }

    private void ApplyDisplayName(string name)
    {
        DisplayName = name.Trim();
        PlayerPrefs.SetString(playerPrefsKey, DisplayName);
        OnDisplayNameChanged?.Invoke(DisplayName);
        Debug.Log($"[Bridge] DisplayName = '{DisplayName}' (propagé + sauvegardé)");
        // Optionnel: pousse aux PlayerRanking déjà présents (si on est en jeu)
        PushToExistingRankings(DisplayName);
    }

    private void PushToExistingRankings(string name)
    {
        var ranks = FindObjectsOfType<PlayerRanking>(true);
        foreach (var r in ranks)
        {
            r.SetPlayerName(name); // s’occupe lui-même de SA/RPC
        }
    }
}
