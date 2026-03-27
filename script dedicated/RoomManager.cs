using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class RoomManager : NetworkBehaviour
{
    public static RoomManager Instance { get; private set; }

    [Header("Limites")]
    [SerializeField] private int maxRoomsPerServer = 10;
    [SerializeField] private int maxPlayersPerRoom = 8;
    [SerializeField] private int minPlayersToStart = 2;

    [Header("Prefabs")]
    [SerializeField] private NetworkPrefabRef broadcasterPrefab;
    [SerializeField] private NetworkPrefabRef playerPrefab;
    public NetworkPrefabRef PlayerPrefabRef => playerPrefab;

    private readonly Dictionary<string, GameSession> _sessions   = new();
    private readonly Dictionary<PlayerRef, string>   _playerRoom = new();

    public override void Spawned()
    {
        if (!Runner.IsServer) return;
        Instance = this;
        Debug.Log("[RoomManager] Serveur prêt.");
    }

    // ===================== JOIN / CREATE =====================

    public string JoinOrCreateRoom(PlayerRef player, string requestedRoomId = null)
    {
        if (!Runner.IsServer) return null;

        if (_playerRoom.TryGetValue(player, out var existing))
        {
            Debug.Log($"[RoomManager] {player} déjà dans '{existing}'.");
            return existing;
        }

        // Room spécifique demandée
        if (!string.IsNullOrEmpty(requestedRoomId))
        {
            if (_sessions.TryGetValue(requestedRoomId, out var req))
            {
                if (req.PlayerCount < maxPlayersPerRoom && !req.IsFinished)
                {
                    AssignPlayerToSession(player, req);
                    return requestedRoomId;
                }
                Debug.LogWarning($"[RoomManager] Room '{requestedRoomId}' pleine ou terminée.");
            }
            return CreateNewRoom(player, requestedRoomId);
        }

        // Quick-join
        foreach (var kv in _sessions)
        {
            if (kv.Value.PlayerCount < maxPlayersPerRoom && !kv.Value.IsFinished)
            {
                AssignPlayerToSession(player, kv.Value);
                return kv.Key;
            }
        }

        return CreateNewRoom(player, null);
    }

    private string CreateNewRoom(PlayerRef player, string roomId)
    {
        if (_sessions.Count >= maxRoomsPerServer)
        {
            Debug.LogWarning("[RoomManager] Limite de rooms atteinte.");
            return null;
        }

        if (string.IsNullOrEmpty(roomId))
            roomId = $"room_{System.Guid.NewGuid().ToString("N")[..8]}";

        if (!broadcasterPrefab.Equals(default(NetworkPrefabRef)))
            RoomBroadcaster.SpawnForRoom(Runner, broadcasterPrefab, roomId);
        else
            Debug.LogWarning("[RoomManager] broadcasterPrefab non assigné !");

        var session = new GameSession(roomId, Runner, minPlayersToStart, maxPlayersPerRoom);
        _sessions[roomId] = session;
        AssignPlayerToSession(player, session);

        Debug.Log($"[RoomManager] Nouvelle room '{roomId}' créée.");
        return roomId;
    }

    // ===================== REMOVE =====================

    /// <summary>Retire le joueur ET notifie GameSession (départ définitif).</summary>
    public void RemovePlayer(PlayerRef player)
    {
        if (!Runner.IsServer) return;
        if (!_playerRoom.TryGetValue(player, out var roomId)) return;

        _playerRoom.Remove(player);
        if (!_sessions.TryGetValue(roomId, out var session)) return;

        session.RemovePlayer(player);
        Debug.Log($"[RoomManager] {player} retiré de '{roomId}'. Restants: {session.PlayerCount}");

        if (session.PlayerCount == 0)
        {
            session.Dispose();
            _sessions.Remove(roomId);
            Debug.Log($"[RoomManager] Room '{roomId}' détruite.");
        }
    }

    /// <summary>
    /// Retire le joueur du registre SANS notifier GameSession.
    /// Utilisé par PlayerRoomRequest pour déplacer un joueur entre rooms.
    /// </summary>
    public void RemovePlayerSoft(PlayerRef player)
    {
        if (!Runner.IsServer) return;
        if (!_playerRoom.TryGetValue(player, out var roomId)) return;

        _playerRoom.Remove(player);
        if (!_sessions.TryGetValue(roomId, out var session)) return;

        session.RemovePlayerSoft(player);
        Debug.Log($"[RoomManager] {player} retiré (soft) de '{roomId}'.");

        if (session.PlayerCount == 0)
        {
            session.Dispose();
            _sessions.Remove(roomId);
            Debug.Log($"[RoomManager] Room '{roomId}' détruite (vide).");
        }
    }

    // ===================== AVATAR / DEATH =====================

    public void RegisterAvatar(PlayerRef player, NetworkObject avatar)
    {
        if (!_playerRoom.TryGetValue(player, out var roomId)) return;
        if (_sessions.TryGetValue(roomId, out var session))
            session.RegisterAvatar(player, avatar);
    }

    public void NotifyFinalDeath(PlayerRef player)
    {
        var session = GetSessionForPlayer(player);
        if (session == null) { Debug.LogWarning($"[RoomManager] NotifyFinalDeath: pas de session pour {player}."); return; }
        session.NotifyDeath(player);
    }

    // ===================== LISTE ROOMS (UI) =====================

    /// <summary>Retourne la liste des rooms pour l'affichage UI client.</summary>
    public List<RoomInfo> GetRoomList()
    {
        var list = new List<RoomInfo>();
        foreach (var kv in _sessions)
        {
            list.Add(new RoomInfo
            {
                RoomId      = kv.Key,
                PlayerCount = kv.Value.PlayerCount,
                MaxPlayers  = maxPlayersPerRoom,
                IsStarted   = kv.Value.MatchStarted,
                IsFinished  = kv.Value.IsFinished
            });
        }
        return list;
    }

    // ===================== QUERIES =====================

    public GameSession GetSessionForPlayer(PlayerRef player)
    {
        if (_playerRoom.TryGetValue(player, out var roomId) &&
            _sessions.TryGetValue(roomId, out var s)) return s;
        return null;
    }

    public string GetRoomIdForPlayer(PlayerRef player)
    {
        _playerRoom.TryGetValue(player, out var id);
        return id;
    }

    public IReadOnlyDictionary<string, GameSession> Sessions => _sessions;

    private void AssignPlayerToSession(PlayerRef player, GameSession session)
    {
        session.AddPlayer(player);
        _playerRoom[player] = session.RoomId;
        Debug.Log($"[RoomManager] {player} → '{session.RoomId}' ({session.PlayerCount}/{maxPlayersPerRoom}).");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (!Runner || !Runner.IsServer) return;
        int h = 28 + _sessions.Count * 22;
        GUI.Box(new Rect(4, 4, 380, h), GUIContent.none);
        GUI.Label(new Rect(8, 8, 370, 18), $"[RoomManager] {_sessions.Count}/{maxRoomsPerServer} rooms | {_playerRoom.Count} joueurs");
        int y = 28;
        foreach (var kv in _sessions)
        {
            GUI.Label(new Rect(8, y, 370, 18), $"  '{kv.Key}' : {kv.Value.PlayerCount} joueurs | started={kv.Value.MatchStarted}");
            y += 22;
        }
    }
#endif
}

[System.Serializable]
public class RoomInfo
{
    public string RoomId;
    public int    PlayerCount;
    public int    MaxPlayers;
    public bool   IsStarted;
    public bool   IsFinished;
}
