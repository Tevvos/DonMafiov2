using System;
using System.Collections.Generic;
using UnityEngine;
using PlayFab;
using PlayFab.ClientModels;

/// <summary>
/// Source de vérité du compte pour la Réputation & le Rang (persistance PlayFab + backup PlayerPrefs).
/// - DontDestroyOnLoad
/// - Fournit événements pour l’UI
/// - Recalcule le rang à partir de la réputation (seuils configurables)
/// </summary>
[DisallowMultipleComponent]
public class ReputationManager : MonoBehaviour {
    public static ReputationManager Instance { get; private set; }

    // Clés PlayFab
    private const string KEY_REPUTATION = "Reputation";
    private const string KEY_RANK_INDEX = "RankIndex";

    [Header("Runtime (lecture seule)")]
    [SerializeField] private int reputation;
    [SerializeField] private int rankIndex;

    public int Reputation => reputation;
    public int RankIndex => rankIndex;

    [Header("Config rangs (0..11)")]
    [Tooltip("Doit correspondre à ton set : 0 Novizio, 1 Recruit, ... 10 Don, 11 King Skull")]
    [SerializeField] private string[] rankNames = new string[] {
        "Novizio","Recruit","Bodyguard","Soldier","Veteran","Lieutenant",
        "Capo","District Chef","Black Hand","Underboss","Don","King Skull"
    };

    [Tooltip("Seuils (RP) pour débloquer chaque rang (mêmes index que rankNames). Don < King Skull=4000.")]
    [SerializeField] private int[] rankThresholds = { 0, 50, 150, 300, 500, 800, 1200, 1700, 2300, 3000, 3400, 4000 };

    // Debounce sauvegarde PlayFab
    private bool savePending = false;
    private float saveDebounce = 0f;
    private const float SAVE_DEBOUNCE_SECONDS = 2f;

    public event Action<int,int> OnReputationOrRankChanged; // (rep, rankIndex)

    void Awake() {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start() {
        // Si déjà connecté à PlayFab quand ce manager apparaît, on peut charger direct
        if (PlayFabClientAPI.IsClientLoggedIn()) {
            InitializeFromPlayFab();
        } else {
            // Fallback local en attendant le login
            LoadFromPlayerPrefs();
            RecomputeRank();
            NotifyChanged();
        }
    }

    void Update() {
        if (savePending) {
            saveDebounce -= Time.unscaledDeltaTime;
            if (saveDebounce <= 0f) {
                savePending = false;
                SaveToPlayFab();
            }
        }
    }

    // === API ===

    /// <summary>À appeler juste après un login PlayFab réussi.</summary>
    public void InitializeFromPlayFab() {
        if (!PlayFabClientAPI.IsClientLoggedIn()) {
            // si on n'est pas login, garde local
            LoadFromPlayerPrefs();
            RecomputeRank();
            NotifyChanged();
            return;
        }

        var req = new GetUserDataRequest {
            Keys = new List<string> { KEY_REPUTATION, KEY_RANK_INDEX }
        };

        PlayFabClientAPI.GetUserData(req, res => {
            int rep = 0;
            int rIdx = 0;

            if (res.Data != null) {
                if (res.Data.TryGetValue(KEY_REPUTATION, out var repItem)) {
                    int.TryParse(repItem.Value, out rep);
                }
                if (res.Data.TryGetValue(KEY_RANK_INDEX, out var rItem)) {
                    int.TryParse(rItem.Value, out rIdx);
                }
            }

            reputation = Mathf.Max(0, rep);
            // Le rang est recalculé depuis la réputation (source de vérité)
            RecomputeRank();

            // Si l’index serveur diffère du calcul actuel, on sauvegardera proprement
            if (rIdx != rankIndex) MarkDirtyForSave();

            SaveToPlayerPrefs();
            NotifyChanged();
        },
        err => {
            Debug.LogWarning("[Reputation] GetUserData failed: " + err.GenerateErrorReport());
            LoadFromPlayerPrefs();
            RecomputeRank();
            NotifyChanged();
        });
    }

    /// <summary>Ajoute de la réputation (valeur clampée à 0 mini) + save PlayFab (debounce).</summary>
    public void AddReputation(int amount) {
        if (amount == 0) return;
        reputation = Mathf.Max(0, reputation + amount);
        RecomputeRank();
        SaveToPlayerPrefs();
        MarkDirtyForSave();
        NotifyChanged();
    }

    /// <summary>Fixe la réputation (valeur clampée) + save PlayFab (debounce).</summary>
    public void SetReputation(int value) {
        reputation = Mathf.Max(0, value);
        RecomputeRank();
        SaveToPlayerPrefs();
        MarkDirtyForSave();
        NotifyChanged();
    }

    /// <summary>Nom de rang (par défaut le rang courant).</summary>
    public string GetRankName(int idx = -1) {
        int i = (idx < 0) ? rankIndex : idx;
        if (i < 0 || i >= rankNames.Length) return "Unknown";
        return rankNames[i];
    }

    /// <summary>Forcer le rang courant à partir d’un index (utilisé si tu veux imposer la vue UI).</summary>
    public void ForceRankFromThresholds(int index) {
        rankIndex = Mathf.Clamp(index, 0, rankNames.Length - 1);
        SaveToPlayerPrefs();
        MarkDirtyForSave();
        NotifyChanged();
    }

    /// <summary>Recalcule l’index de rang courant depuis la réputation et les seuils.</summary>
    public void RecomputeRank() {
        int newIndex = 0;
        for (int i = 0; i < rankThresholds.Length; i++) {
            if (reputation >= rankThresholds[i]) newIndex = i;
            else break;
        }
        rankIndex = Mathf.Clamp(newIndex, 0, rankNames.Length - 1);
    }

    // === Persistance locale ===
    private void SaveToPlayerPrefs() {
        PlayerPrefs.SetInt(KEY_REPUTATION, reputation);
        PlayerPrefs.SetInt(KEY_RANK_INDEX, rankIndex);
        PlayerPrefs.Save();
    }

    private void LoadFromPlayerPrefs() {
        reputation = PlayerPrefs.GetInt(KEY_REPUTATION, 0);
        rankIndex  = Mathf.Clamp(PlayerPrefs.GetInt(KEY_RANK_INDEX, 0), 0, rankNames.Length - 1);
    }

    // === Persistance PlayFab ===
    private void MarkDirtyForSave() {
        if (!PlayFabClientAPI.IsClientLoggedIn()) return;
        savePending = true;
        saveDebounce = SAVE_DEBOUNCE_SECONDS;
    }

    private void SaveToPlayFab() {
        if (!PlayFabClientAPI.IsClientLoggedIn()) return;

        var data = new Dictionary<string, string> {
            { KEY_REPUTATION, reputation.ToString() },
            { KEY_RANK_INDEX, rankIndex.ToString() }
        };

        var req = new UpdateUserDataRequest {
            Data = data,
            Permission = UserDataPermission.Private
        };

        PlayFabClientAPI.UpdateUserData(req,
            _ => { /* OK */ },
            err => Debug.LogWarning("[Reputation] UpdateUserData failed: " + err.GenerateErrorReport())
        );
    }

    private void NotifyChanged() {
        OnReputationOrRankChanged?.Invoke(reputation, rankIndex);
    }
}
