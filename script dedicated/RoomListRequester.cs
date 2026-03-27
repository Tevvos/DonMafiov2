using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// À placer sur le prefab FusionRunnerManager (celui qui a le NetworkRunner).
/// C'est lui qui envoie les RPCs vers le serveur pour demander la liste des rooms.
/// RoomListUI (MonoBehaviour simple dans la scène) appelle AskRoomList().
/// </summary>
public class RoomListRequester : NetworkBehaviour
{
    // Callback à appeler quand la liste arrive
    private Action<List<RoomInfo>> _pendingCallback;

    /// <summary>
    /// Demande la liste des rooms au serveur.
    /// onReceived est appelé sur le client quand la réponse arrive.
    /// </summary>
    public void AskRoomList(Action<List<RoomInfo>> onReceived)
    {
        if (Runner == null || !Runner.IsRunning)
        {
            Debug.LogWarning("[RoomListRequester] Runner non prêt.");
            onReceived?.Invoke(new List<RoomInfo>());
            return;
        }

        _pendingCallback = onReceived;
        RPC_RequestRoomList();
    }

    // ===================== RPCs =====================

    /// <summary>Client → Serveur : demande la liste.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRoomList()
    {
        if (RoomManager.Instance == null)
        {
            RPC_SendRoomList(string.Empty);
            return;
        }

        var rooms = RoomManager.Instance.GetRoomList();
        string csv = Serialize(rooms);
        RPC_SendRoomList(csv);
    }

    /// <summary>Serveur → Tous les clients : envoie la liste sérialisée.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SendRoomList(string csv)
    {
        // Seul le client qui a le callback en attente traite la réponse
        if (_pendingCallback == null) return;

        var rooms = Deserialize(csv);
        _pendingCallback.Invoke(rooms);
        _pendingCallback = null;
    }

    // ===================== SÉRIALISATION =====================
    // Format CSV : roomId|playerCount|maxPlayers|isStarted|isFinished;...

    private static string Serialize(List<RoomInfo> rooms)
    {
        if (rooms == null || rooms.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var r in rooms)
        {
            sb.Append(r.RoomId).Append('|')
              .Append(r.PlayerCount).Append('|')
              .Append(r.MaxPlayers).Append('|')
              .Append(r.IsStarted  ? '1' : '0').Append('|')
              .Append(r.IsFinished ? '1' : '0')
              .Append(';');
        }
        return sb.ToString();
    }

    private static List<RoomInfo> Deserialize(string csv)
    {
        var list = new List<RoomInfo>();
        if (string.IsNullOrEmpty(csv)) return list;

        var entries = csv.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var p = entry.Split('|');
            if (p.Length < 5) continue;
            list.Add(new RoomInfo
            {
                RoomId      = p[0],
                PlayerCount = int.TryParse(p[1], out var pc) ? pc : 0,
                MaxPlayers  = int.TryParse(p[2], out var mp) ? mp : 8,
                IsStarted   = p[3] == "1",
                IsFinished  = p[4] == "1"
            });
        }
        return list;
    }
}
