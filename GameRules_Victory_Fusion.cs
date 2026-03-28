using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class GameRules_Victory_Fusion : NetworkBehaviour
{
    public static GameRules_Victory_Fusion Instance;

    public static string LastWinnerName { get; private set; } = "";
    public static bool   IsGameLive     { get; private set; }
    public static System.Action<bool> OnGameLiveChanged;

    [Header("Rules")]
    [SerializeField] private int   minPlayersToEnableWin = 2;
    [SerializeField] private float minRoundStartDelay    = 0f;

    [Tooltip("Si ON: seuls les joueurs vraiment 'spawn' (avatar avec PlayerHealth/Ranking) sont comptés.")]
    [SerializeField] private bool  requireSpawnedAvatar  = true;

    [Header("Gating (anti-lobby)")]
    [Tooltip("Vrai si on veut interdire toute victoire hors de la scène de jeu.")]
    [SerializeField] private bool restrictToGameplayScene = true;

    [Tooltip("Nom exact de la scène de jeu (fallback si buildIndex ne correspond pas).")]
    [SerializeField] private string gameplaySceneName = "GameScene01";

    [Tooltip("Index build de la scène de jeu (met -1 pour ignorer).")]
    [SerializeField] private int gameplaySceneBuildIndex = 2;

    [Tooltip("Exiger un feu vert explicite depuis le flow de match (ex: au spawn) via SetExternalMatchStarted(true).")]
    [SerializeField] private bool requireExternalMatchStart = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs  = true;
    [SerializeField] private bool drawDebugHUD = false;

    private readonly HashSet<PlayerRef> _contestants = new HashSet<PlayerRef>();
    private readonly HashSet<PlayerRef> _eliminated  = new HashSet<PlayerRef>();

    private bool  _roundActive = false;
    private bool  _roundFinished = false;
    private int   _playersCountAtStart = 0;
    private float _roundStartServerTime = 0f;

    // Feu vert externe (ex: MatchFlow_Fusion quand la partie démarre vraiment)
    private static bool _externalMatchStarted = false;

    bool HasServerAuthorityStrict() => Object && Object.HasStateAuthority;

    // --- API publique pour ton flow ---
    /// <summary>Appelle true quand la partie démarre (après le spawn / intro), false au retour lobby.</summary>
    public static void SetExternalMatchStarted(bool started)
    {
        _externalMatchStarted = started;
        if (Instance && Instance.verboseLogs)
            Debug.Log($"[Victory] ExternalGate = {started}");
    }

    public override void Spawned()
    {
        Instance = this;

        _contestants.Clear();
        _eliminated.Clear();
        _roundActive = false;
        _roundFinished = false;
        _playersCountAtStart = 0;
        _roundStartServerTime = 0f;

        SetGameLive(false);
        Debug.Log($"[Victory] Spawned. HasStateAuthority={(Object ? Object.HasStateAuthority : false)} | Runner.IsServer={(Runner ? Runner.IsServer : false)} | HasNO={(GetComponent<NetworkObject>()!=null)}");
        if (!Object) Debug.LogError("[Victory] Object==null -> Ce NetworkBehaviour n'est PAS spawné par Fusion (NetworkObject/SceneManager manquant).");
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasServerAuthorityStrict())
        {
            if (Runner && Runner.Tick % 60 == 0) VLog("[Victory] Ignore tick (pas StateAuthority).");
            return;
        }

        // === Verrou anti-lobby ===
        bool matchStarted = IsGameplayContextOk() && IsExternalGateOk();
        if (!matchStarted)
        {
            // Tant qu’on n’est pas en jeu, on s’assure qu’aucun round ne tourne ni ne démarre
            if (_roundActive || IsGameLive)
                Debug.LogWarning("[Victory] Contexte pas 'en jeu' -> on désactive le round et la 'live state' (anti-lobby).");

            _roundActive = false;
            _roundFinished = false;
            SetGameLive(false);
            return;
        }

        // À partir d’ici, on est bien “en jeu”.
        RebuildContestants();

        VLog($"[Victory] Eval Tick -> RoundActive={_roundActive}, Finished={_roundFinished}, Contestants={_contestants.Count}, Eliminated={_eliminated.Count}, PlayersAtStart={_playersCountAtStart}");

        if (_roundActive)
        {
            EvaluateVictory();
            return;
        }

        if (_roundFinished)
        {
            if (_contestants.Count < minPlayersToEnableWin) return;
            _roundFinished = false;
            _roundStartServerTime = 0f;
        }

        // Démarrage de manche (seulement en jeu)
        if (_contestants.Count >= minPlayersToEnableWin)
        {
            if (minRoundStartDelay <= 0f) StartRound();
            else
            {
                if (_roundStartServerTime == 0f)
                    _roundStartServerTime = Runner ? Runner.SimulationTime : 0f;
                else if ((Runner ? Runner.SimulationTime : 0f) - _roundStartServerTime >= minRoundStartDelay)
                    StartRound();
            }
        }
    }

    void StartRound()
    {
        _roundActive = true;
        _playersCountAtStart = _contestants.Count;
        SetGameLive(true);
        Debug.Log($"[Victory] Round START avec {_playersCountAtStart} contestants.");
    }

    // ---------- Players scan ----------
    void RebuildContestants()
    {
        if (!Runner) return;

        var active = new HashSet<PlayerRef>();
        foreach (var p in Runner.ActivePlayers) active.Add(p);

        // Détecte vraiment les avatars, même si PlayerObject n'est pas l'avatar
        var spawned = new HashSet<PlayerRef>();
        foreach (var p in active)
            if (IsSpawnedInGame(p)) spawned.Add(p);

        // Fallback: si requireSpawnedAvatar ET spawned==0 mais Active >= minPlayers, on bascule sur Active
        bool useActiveFallback = requireSpawnedAvatar && spawned.Count == 0 && active.Count >= minPlayersToEnableWin;
        var source = useActiveFallback ? active : (requireSpawnedAvatar ? spawned : active);

        if (useActiveFallback)
            Debug.LogWarning("[Victory] Fallback: Spawned==0 alors qu'il y a des joueurs actifs. On utilise Active pour lancer le round. (Pense à lier l'avatar avec Runner.SetPlayerObject)");

        // Nettoie / met à jour
        var toRemove = new List<PlayerRef>();
        foreach (var c in _contestants)
            if (!source.Contains(c) || _eliminated.Contains(c))
                toRemove.Add(c);
        foreach (var r in toRemove) _contestants.Remove(r);

        foreach (var p in source)
            if (!_eliminated.Contains(p)) _contestants.Add(p);

        if (verboseLogs)
            VLog($"[Victory] Active={active.Count} | Spawned={spawned.Count} | Contestants={_contestants.Count} | Eliminated={_eliminated.Count} | RequireSpawned={requireSpawnedAvatar} | FallbackUsed={useActiveFallback}");
    }

    // ✅ Version robuste : si PlayerObject n'est pas l'avatar, on scanne la scène
    bool IsSpawnedInGame(PlayerRef pref)
    {
        // 1) Chemin classique : PlayerObject qui EST l'avatar
        if (Runner && Runner.TryGetPlayerObject(pref, out var no) && no != null)
        {
            if (HasGameplay(no)) return true;
        }

        // 2) Chemin robuste : on cherche un avatar qui a l'autorité pref
        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            if (ph && ph.Object && ph.Object.InputAuthority == pref) return true;

        foreach (var pr in FindObjectsOfType<PlayerRanking>(true))
            if (pr && pr.Object && pr.Object.InputAuthority == pref) return true;

        return false;
    }

    bool HasGameplay(NetworkObject no)
    {
        if (!no) return false;
        return no.GetComponent<PlayerHealth>() ||
               no.GetComponentInChildren<PlayerHealth>(true) ||
               no.GetComponentInParent<PlayerHealth>(true) ||
               no.GetComponent<PlayerRanking>() ||
               no.GetComponentInChildren<PlayerRanking>(true) ||
               no.GetComponentInParent<PlayerRanking>(true);
    }

    // ---------- External notifications ----------
    public static void NotifySpawn(PlayerRef pref)
    {
        if (!Instance) return;
        if (!Instance.HasServerAuthorityStrict()) { Instance.VLog("[Victory] NotifySpawn ignoré (pas StateAuthority)."); return; }
        Instance.RebuildContestants();
        if (Instance._roundActive) Instance.EvaluateVictory();
    }

    public static void NotifySpawn(PlayerRanking pr)
    {
        if (!pr || !pr.Object) return;
        NotifySpawn(pr.Object.InputAuthority);
    }

    public static void NotifyFinalDeath(PlayerRef pref)
    {
        if (!Instance) return;
        if (!Instance.HasServerAuthorityStrict()) { Instance.VLog("[Victory] NotifyFinalDeath ignoré (pas StateAuthority)."); return; }
        if (pref == PlayerRef.None) return;

        Instance._eliminated.Add(pref);
        Instance.RebuildContestants();
        Debug.Log($"[Victory] FinalDeath pour {pref}. Contestants={Instance._contestants.Count}, Eliminated={Instance._eliminated.Count}");
        Instance.EvaluateVictory();
    }

    public static void NotifyQuit(PlayerRef pref)
    {
        if (!Instance) return;
        if (!Instance.HasServerAuthorityStrict()) { Instance.VLog("[Victory] NotifyQuit ignoré (pas StateAuthority)."); return; }
        if (pref == PlayerRef.None) return;

        Instance._eliminated.Add(pref);
        Instance.RebuildContestants();
        Debug.Log($"[Victory] Quit pour {pref}. Contestants={Instance._contestants.Count}, Eliminated={Instance._eliminated.Count}");
        Instance.EvaluateVictory();
    }

    // ---------- Victory eval ----------
    void EvaluateVictory()
    {
        if (!_roundActive) { VLog("[Victory] EvaluateVictory ignoré (Round non actif)."); return; }

        VLog($"[Victory] EvaluateVictory: Contestants={_contestants.Count}, PlayersAtStart={_playersCountAtStart}");

        if (_contestants.Count == 1 && _playersCountAtStart >= minPlayersToEnableWin)
        {
            foreach (var last in _contestants) { DeclareWinner(last); break; }
        }
        else if (_contestants.Count == 0 && _playersCountAtStart >= minPlayersToEnableWin)
        {
            DeclareStalemate();
        }
    }

    void DeclareWinner(PlayerRef pref)
    {
        // Sécurité anti-lobby : si le contexte n'est pas “en jeu”, on ignore TOUT
        if (!IsGameplayContextOk() || !IsExternalGateOk())
        {
            Debug.LogWarning("[Victory] Victoire ignorée (contexte non 'en jeu' / gate externe OFF).");
            _roundActive = false;
            _roundFinished = false;
            SetGameLive(false);
            ResetRoundDataOnly();
            return;
        }

        _roundActive = false;
        _roundFinished = true;
        SetGameLive(false);

        LastWinnerName = ResolveName(pref);
        Debug.Log($"[Victory] WINNER = {LastWinnerName} ({pref}). StateAuth={(Object && Object.HasStateAuthority)}");

        // Attribution points / annonce UI seulement si contexte OK
        GameSceneRankingHub.ReportVictory(pref);

        if (HasServerAuthorityStrict())
        {
            Debug.Log("[Victory] Envoi RPC_PresentVictory -> ALL");
            RPC_PresentVictory(pref);
        }
        else
        {
            Debug.LogError("[Victory] Pas StateAuthority -> RPC_PresentVictory NON envoyé.");
        }

        ResetRoundDataOnly();
    }

    void DeclareStalemate()
    {
        if (!IsGameplayContextOk() || !IsExternalGateOk())
        {
            Debug.LogWarning("[Victory] Stalemate ignoré (contexte non 'en jeu' / gate externe OFF).");
            _roundActive = false;
            _roundFinished = false;
            SetGameLive(false);
            ResetRoundDataOnly();
            return;
        }

        _roundActive = false;
        _roundFinished = true;
        SetGameLive(false);

        if (HasServerAuthorityStrict())
        {
            Debug.Log("[Victory] Stalemate -> Envoi RPC_PresentStalemate -> ALL");
            RPC_PresentStalemate();
        }
        else
        {
            Debug.LogError("[Victory] Pas StateAuthority -> RPC_PresentStalemate NON envoyé.");
        }

        ResetRoundDataOnly();
    }

    void ResetRoundDataOnly()
    {
        _contestants.Clear();
        _eliminated.Clear();
        _playersCountAtStart = 0;
        _roundStartServerTime = 0f;
    }

    // ---------- RPCs UI ----------
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PresentVictory(PlayerRef winnerRef)
    {
        Debug.Log("[Victory] RPC_PresentVictory reçu (client)");
        var wp = EnsureWinPresenter();
        if (wp != null) { Debug.Log("[Victory] WinPresenter OK -> ShowResult()"); wp.ShowResult(winnerRef, LastWinnerName); }
        else Debug.LogError("[Victory] AUCUN WinPresenter trouvé lors de RPC_PresentVictory !");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PresentStalemate()
    {
        Debug.Log("[Victory] RPC_PresentStalemate reçu (client)");
        var wp = EnsureWinPresenter();
        if (wp != null) { Debug.Log("[Victory] WinPresenter OK -> ShowStalemate()"); wp.ShowStalemate(); }
        else Debug.LogError("[Victory] AUCUN WinPresenter trouvé lors de RPC_PresentStalemate !");
    }

    WinPresenter EnsureWinPresenter()
    {
        var wp = WinPresenter.Instance ? WinPresenter.Instance : FindObjectOfType<WinPresenter>(true);
        if (wp == null) return null;

        if (!wp.gameObject.activeSelf) wp.gameObject.SetActive(true);

        var cg = typeof(WinPresenter).GetField("group", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(wp) as CanvasGroup;
        if (cg)
        {
            cg.alpha = Mathf.Max(0f, cg.alpha);
            cg.interactable = true;
            cg.blocksRaycasts = true;
            if (!cg.gameObject.activeSelf) cg.gameObject.SetActive(true);
        }
        return wp;
    }

    static void SetGameLive(bool live)
    {
        if (IsGameLive == live) return;
        IsGameLive = live;
        OnGameLiveChanged?.Invoke(IsGameLive);
    }

    string ResolveName(PlayerRef pref)
    {
        if (!Runner) return pref.ToString();
        if (Runner.TryGetPlayerObject(pref, out var no))
        {
            var pr = no.GetComponent<PlayerRanking>() ??
                     no.GetComponentInChildren<PlayerRanking>(true) ??
                     no.GetComponentInParent<PlayerRanking>(true);
            if (pr) return string.IsNullOrWhiteSpace(pr.PlayerName) ? pref.ToString() : pr.PlayerName;
        }

        foreach (var pr in FindObjectsOfType<PlayerRanking>(true))
            if (pr && pr.Object && pr.Object.InputAuthority == pref)
                return string.IsNullOrWhiteSpace(pr.PlayerName) ? pref.ToString() : pr.PlayerName;

        return pref.ToString();
    }

    void VLog(string m) { if (verboseLogs) Debug.Log(m); }

    // --- Helpers de contexte ---
    bool IsGameplayContextOk()
    {
        if (!restrictToGameplayScene) return true;

        var s = SceneManager.GetActiveScene();
        bool idxOk = (gameplaySceneBuildIndex >= 0) && (s.buildIndex == gameplaySceneBuildIndex);
        bool nameOk = !string.IsNullOrEmpty(gameplaySceneName) && s.name == gameplaySceneName;

        bool ok = idxOk || nameOk;
        if (!ok && verboseLogs)
            Debug.Log($"[Victory] Scene gate KO -> Active='{s.name}'(#{s.buildIndex}) attendu '{gameplaySceneName}'(#{gameplaySceneBuildIndex}).");
        return ok;
    }

    bool IsExternalGateOk()
    {
        if (!requireExternalMatchStart) return true;
        if (!_externalMatchStarted && verboseLogs)
            Debug.Log("[Victory] External gate KO (SetExternalMatchStarted(false)).");
        return _externalMatchStarted;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnGUI()
    {
        if (!drawDebugHUD) return;
        GUI.Box(new Rect(4, 4, 700, 140), GUIContent.none);
        GUI.Label(new Rect(10, 10, 680, 20), $"IsGameLive: {IsGameLive} | RoundActive: {_roundActive} | Finished: {_roundFinished}");
        GUI.Label(new Rect(10, 30, 680, 20), $"Contestants: {_contestants.Count} | Eliminated: {_eliminated.Count}");
        GUI.Label(new Rect(10, 50, 680, 20), $"PlayersAtStart: {_playersCountAtStart} | LastWinner: {LastWinnerName}");
        GUI.Label(new Rect(10, 70, 680, 20), $"MinToWin: {minPlayersToEnableWin} | RequireSpawnedAvatar: {requireSpawnedAvatar}");
        GUI.Label(new Rect(10, 90, 680, 20), $"SceneGate: {(restrictToGameplayScene ? "ON" : "OFF")} -> '{gameplaySceneName}' #{gameplaySceneBuildIndex}");
        GUI.Label(new Rect(10, 110, 680, 20), $"ExternalGate: {(requireExternalMatchStart ? "ON" : "OFF")} -> started={_externalMatchStarted}");
    }
#endif
}
