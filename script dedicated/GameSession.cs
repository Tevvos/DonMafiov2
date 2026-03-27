using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Représente une partie isolée (une "room").
/// Gère les joueurs, le cycle de match, et la détection de victoire.
/// N'est PAS un MonoBehaviour : instancié et géré par RoomManager côté serveur.
/// </summary>
public class GameSession
{
    public string RoomId      { get; }
    public int    PlayerCount => _players.Count;
    public bool   MatchStarted  { get; private set; }
    public bool   IsFinished    { get; private set; }

    private readonly HashSet<PlayerRef> _players    = new();
    private readonly HashSet<PlayerRef> _eliminated = new();
    private readonly Dictionary<PlayerRef, NetworkObject> _avatars = new();

    private readonly NetworkRunner _runner;
    private readonly int _minPlayersToStart;
    private readonly int _maxPlayers;

    private bool _roundFinished;
    private bool _victoryDeclared;

    public GameSession(string roomId, NetworkRunner runner, int minPlayersToStart = 2, int maxPlayers = 8)
    {
        RoomId                  = roomId;
        _runner                 = runner;
        this._minPlayersToStart = minPlayersToStart;
        this._maxPlayers        = maxPlayers;
    }

    // ===================== JOUEURS =====================

    public bool HasRoom(PlayerRef player) => _players.Contains(player);

    public void AddPlayer(PlayerRef player)
    {
        _players.Add(player);
        Debug.Log($"[GameSession:{RoomId}] +{player} | total={_players.Count}");
        TryStartMatchIfReady();
    }

    /// <summary>Retire le joueur et le traite comme éliminé si match en cours.</summary>
    public void RemovePlayer(PlayerRef player)
    {
        bool wasInGame = _players.Contains(player);
        _players.Remove(player);
        _avatars.Remove(player);
        Debug.Log($"[GameSession:{RoomId}] -{player} | total={_players.Count}");

        if (wasInGame && MatchStarted && !_roundFinished)
            HandleElimination(player);
    }

    /// <summary>
    /// Retire le joueur du registre SANS l'éliminer.
    /// Utilisé quand on déplace un joueur vers une autre room avant le début du match.
    /// </summary>
    public void RemovePlayerSoft(PlayerRef player)
    {
        _players.Remove(player);
        _avatars.Remove(player);
        Debug.Log($"[GameSession:{RoomId}] soft-remove {player} | total={_players.Count}");
    }

    public void RegisterAvatar(PlayerRef player, NetworkObject avatar)
    {
        if (avatar) _avatars[player] = avatar;
    }

    public NetworkObject GetAvatar(PlayerRef player)
    {
        _avatars.TryGetValue(player, out var no);
        return no;
    }

    // ===================== CYCLE DE MATCH =====================

    private void TryStartMatchIfReady()
    {
        if (MatchStarted || IsFinished) return;
        if (_players.Count < _minPlayersToStart) return;

        MatchStarted     = true;
        _roundFinished   = false;
        _victoryDeclared = false;
        _eliminated.Clear();

        Debug.Log($"[GameSession:{RoomId}] Match démarré avec {_players.Count} joueurs !");

        GameRules_Victory_Fusion.SetExternalMatchStarted(true);

        var bc = RoomBroadcaster.GetForRoom(RoomId);
        bc?.RPC_OnMatchStart(RoomId);
    }

    // ===================== MORT / VICTOIRE =====================

    public void NotifyDeath(PlayerRef dead)
    {
        if (!MatchStarted || _roundFinished) return;
        HandleElimination(dead);
    }

    private void HandleElimination(PlayerRef player)
    {
        if (_victoryDeclared) return;
        _eliminated.Add(player);

        var alive = new HashSet<PlayerRef>(_players);
        alive.ExceptWith(_eliminated);

        Debug.Log($"[GameSession:{RoomId}] Éliminé: {player} | Vivants: {alive.Count}");

        if (alive.Count == 1)
            foreach (var w in alive) { DeclareWinner(w); break; }
        else if (alive.Count == 0)
            DeclareStalemate();
    }

    private void DeclareWinner(PlayerRef winner)
    {
        if (_victoryDeclared) return;
        _victoryDeclared = true;
        _roundFinished   = true;
        MatchStarted     = false;

        string name = ResolvePlayerName(winner);
        GameRules_Victory_Fusion.LastWinnerName = name;
        Debug.Log($"[GameSession:{RoomId}] WINNER = {name} ({winner})");

        GameSceneRankingHub.ReportVictory(winner);

        var bc = RoomBroadcaster.GetForRoom(RoomId);
        if (bc != null) bc.RPC_OnVictory(winner, name);
        else Debug.LogWarning($"[GameSession:{RoomId}] RoomBroadcaster introuvable.");

        GameRules_Victory_Fusion.SetExternalMatchStarted(false);
    }

    private void DeclareStalemate()
    {
        if (_victoryDeclared) return;
        _victoryDeclared = true;
        _roundFinished   = true;
        MatchStarted     = false;

        Debug.Log($"[GameSession:{RoomId}] STALEMATE.");

        var bc = RoomBroadcaster.GetForRoom(RoomId);
        if (bc != null) bc.RPC_OnStalemate();
        else Debug.LogWarning($"[GameSession:{RoomId}] RoomBroadcaster introuvable.");

        GameRules_Victory_Fusion.SetExternalMatchStarted(false);
    }

    // ===================== RESET =====================

    public void ResetRound()
    {
        MatchStarted     = false;
        IsFinished       = false;
        _roundFinished   = false;
        _victoryDeclared = false;
        _eliminated.Clear();
        Debug.Log($"[GameSession:{RoomId}] Round réinitialisé. Joueurs: {_players.Count}");
        TryStartMatchIfReady();
    }

    // ===================== DISPOSE =====================

    public void Dispose()
    {
        GameRules_Victory_Fusion.SetExternalMatchStarted(false);
        foreach (var kv in _avatars)
        {
            if (kv.Value && kv.Value.Runner != null)
                try { _runner.Despawn(kv.Value); } catch { }
        }
        _players.Clear();
        _avatars.Clear();
        _eliminated.Clear();
        IsFinished = true;
        Debug.Log($"[GameSession:{RoomId}] Session libérée.");
    }

    // ===================== UTILS =====================

    private string ResolvePlayerName(PlayerRef pref)
    {
        if (_runner && _runner.TryGetPlayerObject(pref, out var no) && no)
        {
            var pr = no.GetComponent<PlayerRanking>()
                  ?? no.GetComponentInChildren<PlayerRanking>(true)
                  ?? no.GetComponentInParent<PlayerRanking>(true);
            if (pr && !string.IsNullOrWhiteSpace(pr.PlayerName)) return pr.PlayerName;
        }
        foreach (var pr in UnityEngine.Object.FindObjectsOfType<PlayerRanking>(true))
            if (pr && pr.Object && pr.Object.InputAuthority == pref)
                return string.IsNullOrWhiteSpace(pr.PlayerName) ? pref.ToString() : pr.PlayerName;
        return pref.ToString();
    }

    public IReadOnlyCollection<PlayerRef> Players    => _players;
    public IReadOnlyCollection<PlayerRef> Eliminated => _eliminated;
}
