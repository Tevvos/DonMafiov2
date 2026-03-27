using Fusion;
using UnityEngine;

/// <summary>
/// Placé sur le PlayerPrefab.
/// Juste après le spawn, le client envoie au serveur le nom de room qu'il veut rejoindre.
/// Le serveur réassigne le joueur à la bonne room si nécessaire.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerRoomRequest : NetworkBehaviour
{
    private const string PREF_REQUESTED_ROOM = "DM_RequestedRoom";

    public override void Spawned()
    {
        // Seulement le joueur local envoie sa demande de room
        if (!Object.HasInputAuthority) return;

        string requestedRoom = PlayerPrefs.GetString(PREF_REQUESTED_ROOM, string.Empty);

        if (!string.IsNullOrEmpty(requestedRoom))
        {
            Debug.Log($"[PlayerRoomRequest] Demande room '{requestedRoom}' au serveur.");
            RPC_RequestRoom(requestedRoom);
        }
        else
        {
            Debug.Log("[PlayerRoomRequest] Aucune room demandée, quick-join.");
            RPC_RequestRoom(string.Empty);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestRoom(string roomName)
    {
        if (RoomManager.Instance == null)
        {
            Debug.LogWarning("[PlayerRoomRequest] RoomManager introuvable sur le serveur.");
            return;
        }

        PlayerRef player = Object.InputAuthority;

        // Le joueur est déjà dans une room (assigné par défaut dans SpawnPlayerInRoom) ?
        string currentRoom = RoomManager.Instance.GetRoomIdForPlayer(player);

        // Si la room demandée est différente de celle où il a été placé par défaut,
        // on le déplace dans la bonne room.
        if (!string.IsNullOrEmpty(roomName) && currentRoom != roomName)
        {
            Debug.Log($"[PlayerRoomRequest] Serveur: déplace {player} de '{currentRoom}' vers '{roomName}'.");

            // Retire de la room actuelle (sans despawn)
            RoomManager.Instance.RemovePlayerSoft(player);

            // Place dans la room demandée (crée si inexistante)
            string assignedRoom = RoomManager.Instance.JoinOrCreateRoom(player, roomName);
            Debug.Log($"[PlayerRoomRequest] {player} assigné à '{assignedRoom}'.");

            // Ré-enregistre l'avatar dans la nouvelle session
            var avatar = Object.GetComponent<NetworkObject>();
            if (avatar != null)
                RoomManager.Instance.RegisterAvatar(player, avatar);
        }
        else
        {
            Debug.Log($"[PlayerRoomRequest] {player} reste dans '{currentRoom}' (room correcte ou quick-join).");
        }
    }
}
