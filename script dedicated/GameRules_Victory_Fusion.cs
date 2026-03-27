using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MODIFIÉ pour le système multi-rooms.
/// 
/// Ce script conserve son rôle de référence statique (LastWinnerName, IsGameLive,
/// SetExternalMatchStarted) mais cède la décision de victoire à GameSession
/// quand RoomManager est présent.
/// 
/// En mode multi-rooms : GameSession.NotifyDeath() → GameSession.DeclareWinner()
/// → RoomBroadcaster.RPC_OnVictory() → WinPresenter (côté client).
/// 
/// En mode fallback mono-room (pas de RoomManager dans la scène) :
/// l'ancienne logique FixedUpdateNetwork/EvaluateVictory reste active.
/// </summary>
[DisallowMultipleComponent]
public class GameRules_Victory_Fusion : NetworkBehaviour
{
    public static GameRules_Victory_Fusion Instance;

    // Exposés en statique pour les autres systèmes (GameSession, WinPresenter…)
    public static string LastWinnerName { get; set; } = "";
    public static bool   IsGameLive     { get; private set; }
    public static System.Action<bool> OnGameLiveChanged;

    [Header("Rules")]
    [SerializeField] private int   minPlayersToEnableWin = 2;
    [SerializeField] private float minRoundStartDelay    = 0f;
    [SerializeField] private bool  requireSpawnedAvatar  = true;

    [Header("Gating (anti-lobby)")]
    [SerializeField] private bool   restrictToGameplayScene     = true;
    [SerializeField] private string gameplaySceneName           = "GameScene01";
    [SerializeField] private int    gameplaySceneBuildIndex     = 2;
    [SerializeField] private bool   requireExternalMatchStart   = true;

    [Header("Multi-rooms")]
    [Tooltip("Si RoomManager est présent dans la scène, ce script délègue la victoire à GameSession et désactive sa propre évaluation.")]
    [SerializeField] private bool deferToRoomManagerIfPresent = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs  = true;
    [SerializeField] private bool drawDebugHUD = false;

    private readonly HashSet<PlayerRef> _contestants = new();
    private readonly HashSet<PlayerRef> _eliminated  = new();

    private bool  _roundActive        = false;
    private bool  _roundFinished      = false;
    private int   _playersCountAtStart= 0;
    private float _roundStartServerTime = 0f;

    private static bool _externalMatchStarted = false;

    // ── Détection du mode multi-rooms ─────────────────────────────────────────
    private bool IsMultiRoomMode => deferToRoomManagerIfPresent && RoomManager.Instance != null;

    bool HasServerAuthorityStrict() => Object && Object.HasStateAuthority;

    // ===================== API PUBLIQUE =====================

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
        _roundActive          = false;
        _roundFinished        = false;
        _playersCountAtStart  = 0;
        _roundStartServerTime = 0f;

        SetGameLive(false);
        Debug.Log($"[Victory] Spawned. MultiRoomMode={IsMultiRoomMode} | HasStateAuthority={(Object ? Object.HasStateAuthority : false)}");
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasServerAuthorityStrict()) return;

        // ── En mode multi-rooms : on ne gère PAS la victoire ici ──────────────
        // C'est GameSession qui s'en charge. On maintient juste IsGameLive à jour.
        if (IsMultiRoomMode)
        {
            // On se contente de refléter l'état du serveur dédié
            if (Runner && Runner.Tick % 120 == 0)
                VLog("[Victory] Mode multi-rooms actif : évaluation déléguée à GameSession.");
            return;
        }

        // ── Mode fallback mono-room (pas de RoomManager) ──────────────────────
        bool matchStarted = IsGameplayContextOk() && IsExternalGateOk();
        if (!matchStarted)
        {
            if (_roundActive || IsGameLive)
                Debug.LogWarning("[Victory] Contexte pas 'en jeu' -> désactivation anti-lobby.");
            _roundActive   = false;
            _roundFinished = false;
            SetGameLive(false);
            return;
        }

        RebuildContestants();

        if (_roundActive)
        {
            EvaluateVictory();
            return;
        }

        if (_roundFinished)
        {
            if (_contestants.Count < minPlayersToEnableWin) return;
            _roundFinished        = false;
            _roundStartServerTime = 0f;
        }

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
        _roundActive         = true;
        _playersCountAtStart = _contestants.Count;
        SetGameLive(true);
        Debug.Log($"[Victory] Round START avec {_playersCountAtStart} contestants (mono-room fallback).");
    }

    // ===================== SCAN JOUEURS (MONO-ROOM) =====================

    void RebuildContestants()
    {
        if (!Runner) return;

        var active = new HashSet<PlayerRef>();
        foreach (var p in Runner.ActivePlayers) active.Add(p);

        var spawned = new HashSet<PlayerRef>();
        foreach (var p in active)
            if (IsSpawnedInGame(p)) spawned.Add(p);

        bool useActiveFallback = requireSpawnedAvatar && spawned.Count == 0 && active.Count >= minPlayersToEnableWin;
        var source = useActiveFallback ? active : (requireSpawnedAvatar ? spawned : active);

        if (useActiveFallback)
            Debug.LogWarning("[Victory] Fallback: Spawned==0 -> utilise Active.");

        var toRemove = new List<PlayerRef>();
        foreach (var c in _contestants)
            if (!source.Contains(c) || _eliminated.Contains(c)) toRemove.Add(c);
        foreach (var r in toRemove) _contestants.Remove(r);

        foreach (var p in source)
            if (!_eliminated.Contains(p)) _contestants.Add(p);

        VLog($"[Victory] Active={active.Count} | Spawned={spawned.Count} | Contestants={_contestants.Count} | Eliminated={_eliminated.Count}");
    }

    bool IsSpawnedInGame(PlayerRef pref)
    {
        if (Runner && Runner.TryGetPlayerObject(pref, out var no) && no != null)
            if (HasGameplay(no)) return true;

        foreach (var ph in FindObjectsOfType<PlayerHealth>(true))
            if (ph && ph.Object && ph.Object.InputAuthority == pref) return true;

        foreach (var pr in FindObjectsOfType<PlayerRanking>(true))
            if (pr && pr.Object && pr.Object.InputAuthority == pref) return true;

        return false;
    }

    bool HasGameplay(NetworkObject no)
    {
        if (!no) return false;
        return no.GetComponent<PlayerHealth>()
            || no.GetComponentInChildren<PlayerHealth>(true)
            || no.GetComponentInParent<PlayerHealth>(true)
            || no.GetComponent<PlayerRanking>()
            || no.GetComponentInChildren<PlayerRanking>(true)
            || no.GetComponentInParent<PlayerRanking>(true);
    }

    // ===================== NOTIFICATIONS EXTERNES =====================

    /// <summary>
    /// En mode multi-rooms : appelé par GameSession.
    /// En mode mono-room   : appelé directement par le vieux flow.
    /// </summary>
    public static void NotifySpawn(PlayerRef pref)
    {
        if (!Instance) return;
        if (!Instance.HasServerAuthorityStrict()) { Instance.VLog("[Victory] NotifySpawn ignoré (pas StateAuthority)."); return; }

        // En mode multi-rooms, GameSession gère le démarrage → on ne fait rien ici
        if (Instance.IsMultiRoomMode) return;

        Instance.RebuildContestants();
        if (Instance._roundActive) Instance.EvaluateVictory();
    }

    public static void NotifySpawn(PlayerRanking pr)
    {
        if (!pr || !pr.Object) return;
        NotifySpawn(pr.Object.InputAuthority);
    }

    /// <summary>
    /// En mode multi-rooms : appelé via RoomManager.NotifyFinalDeath() → GameSession.NotifyDeath().
    /// En mode mono-room   : appelé directement par PlayerHealth (fallback).
    /// </summary>
    public static void NotifyFinalDeath(PlayerRef pref)
    {
        if (!Instance) return;
        if (!Instance.HasServerAuthorityStrict()) { Instance.VLog("[Victory] NotifyFinalDeath ignoré (pas StateAuthority)."); return; }
        if (pref == PlayerRef.None) return;

        // En mode multi-rooms, PlayerHealth redirige vers RoomManager → GameSession.
        // Ce chemin n'est emprunté qu'en fallback mono-room.
        if (Instance.IsMultiRoomMode)
        {
            Debug.LogWarning("[Victory] NotifyFinalDeath reçu en mode multi-rooms : utilise RoomManager.NotifyFinalDeath() à la place.");
            return;
        }

        Instance._eliminated.Add(pref);
        Instance.RebuildContestants();
        Debug.Log($"[Victory] FinalDeath pour {pref}. Contestants={Instance._contestants.Count}");
        Instance.EvaluateVictory();
    }

    public static void NotifyQuit(PlayerRef pref)
    {
        if (!Instance) return;
        if (!Instance.HasServerAuthorityStrict()) { Instance.VLog("[Victory] NotifyQuit ignoré (pas StateAuthority)."); return; }
        if (pref == PlayerRef.None) return;

        if (Instance.IsMultiRoomMode)
        {
            // En multi-rooms, la déconnexion est gérée par RoomManager.RemovePlayer()
            // qui appelle GameSession.RemovePlayer() → HandleElimination() si match en cours.
            return;
        }

        Instance._eliminated.Add(pref);
        Instance.RebuildContestants();
        Debug.Log($"[Victory] Quit pour {pref}. Contestants={Instance._contestants.Count}");
        Instance.EvaluateVictory();
    }

    // ===================== VICTOIRE (MONO-ROOM FALLBACK) =====================

    void EvaluateVictory()
    {
        if (!_roundActive) { VLog("[Victory] EvaluateVictory ignoré (Round non actif)."); return; }

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
        if (!IsGameplayContextOk() || !IsExternalGateOk())
        {
            Debug.LogWarning("[Victory] Victoire ignorée (contexte KO).");
            _roundActive   = false;
            _roundFinished = false;
            SetGameLive(false);
            ResetRoundDataOnly();
            return;
        }

        _roundActive   = false;
        _roundFinished = true;
        SetGameLive(false);

        LastWinnerName = ResolveName(pref);
        Debug.Log($"[Victory] WINNER = {LastWinnerName} ({pref}).");

        GameSceneRankingHub.ReportVictory(pref);

        if (HasServerAuthorityStrict())
            RPC_PresentVictory(pref);

        ResetRoundDataOnly();
    }

    void DeclareStalemate()
    {
        if (!IsGameplayContextOk() || !IsExternalGateOk())
        {
            Debug.LogWarning("[Victory] Stalemate ignoré (contexte KO).");
            _roundActive   = false;
            _roundFinished = false;
            SetGameLive(false);
            ResetRoundDataOnly();
            return;
        }

        _roundActive   = false;
        _roundFinished = true;
        SetGameLive(false);

        if (HasServerAuthorityStrict())
            RPC_PresentStalemate();

        ResetRoundDataOnly();
    }

    void ResetRoundDataOnly()
    {
        _contestants.Clear();
        _eliminated.Clear();
        _playersCountAtStart  = 0;
        _roundStartServerTime = 0f;
    }

    // ===================== RPCs UI (MONO-ROOM FALLBACK) =====================

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PresentVictory(PlayerRef winnerRef)
    {
        Debug.Log("[Victory] RPC_PresentVictory reçu (mono-room fallback).");
        var wp = EnsureWinPresenter();
        wp?.ShowResult(winnerRef, LastWinnerName);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PresentStalemate()
    {
        Debug.Log("[Victory] RPC_PresentStalemate reçu (mono-room fallback).");
        var wp = EnsureWinPresenter();
        wp?.ShowStalemate();
    }

    WinPresenter EnsureWinPresenter()
    {
        var wp = WinPresenter.Instance ? WinPresenter.Instance : FindObjectOfType<WinPresenter>(true);
        if (wp == null) return null;
        if (!wp.gameObject.activeSelf) wp.gameObject.SetActive(true);

        var cg = typeof(WinPresenter)
            .GetField("group", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(wp) as UnityEngine.CanvasGroup;

        if (cg)
        {
            cg.alpha          = Mathf.Max(0f, cg.alpha);
            cg.interactable   = true;
            cg.blocksRaycasts = true;
            if (!cg.gameObject.activeSelf) cg.gameObject.SetActive(true);
        }
        return wp;
    }

    // ===================== HELPERS =====================

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
            var pr = no.GetComponent<PlayerRanking>()
                  ?? no.GetComponentInChildren<PlayerRanking>(true)
                  ?? no.GetComponentInParent<PlayerRanking>(true);
            if (pr) return string.IsNullOrWhiteSpace(pr.PlayerName) ? pref.ToString() : pr.PlayerName;
        }
        foreach (var pr in FindObjectsOfType<PlayerRanking>(true))
            if (pr && pr.Object && pr.Object.InputAuthority == pref)
                return string.IsNullOrWhiteSpace(pr.PlayerName) ? pref.ToString() : pr.PlayerName;
        return pref.ToString();
    }

    void VLog(string m) { if (verboseLogs) Debug.Log(m); }

    bool IsGameplayContextOk()
    {
        if (!restrictToGameplayScene) return true;
        var s    = SceneManager.GetActiveScene();
        bool idxOk  = (gameplaySceneBuildIndex >= 0) && (s.buildIndex == gameplaySceneBuildIndex);
        bool nameOk = !string.IsNullOrEmpty(gameplaySceneName) && s.name == gameplaySceneName;
        bool ok  = idxOk || nameOk;
        if (!ok && verboseLogs)
            Debug.Log($"[Victory] Scene gate KO -> '{s.name}'(#{s.buildIndex}) attendu '{gameplaySceneName}'(#{gameplaySceneBuildIndex}).");
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
        GUI.Box(new Rect(4, 4, 700, 160), GUIContent.none);
        GUI.Label(new Rect(10, 10,  680, 20), $"MultiRoomMode: {IsMultiRoomMode} | IsGameLive: {IsGameLive} | RoundActive: {_roundActive} | Finished: {_roundFinished}");
        GUI.Label(new Rect(10, 30,  680, 20), $"Contestants: {_contestants.Count} | Eliminated: {_eliminated.Count}");
        GUI.Label(new Rect(10, 50,  680, 20), $"PlayersAtStart: {_playersCountAtStart} | LastWinner: {LastWinnerName}");
        GUI.Label(new Rect(10, 70,  680, 20), $"MinToWin: {minPlayersToEnableWin} | RequireSpawnedAvatar: {requireSpawnedAvatar}");
        GUI.Label(new Rect(10, 90,  680, 20), $"SceneGate: {(restrictToGameplayScene ? "ON" : "OFF")} -> '{gameplaySceneName}' #{gameplaySceneBuildIndex}");
        GUI.Label(new Rect(10, 110, 680, 20), $"ExternalGate: {(requireExternalMatchStart ? "ON" : "OFF")} -> started={_externalMatchStarted}");
        GUI.Label(new Rect(10, 130, 680, 20), $"RoomManager: {(RoomManager.Instance != null ? "PRESENT" : "absent (mono-room fallback)")}");
    }
#endif
}
