using UnityEngine;
using System.Collections.Generic;
using System;
using Fusion;
using Fusion.Sockets;

public class RunnerCallbacks : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef playerPrefab; // ✅ CORRECTION : NetworkPrefabRef au lieu de GameObject

    private NetworkRunner runner;
    private PlayerRef localPlayerRef;

    private void Awake()
    {
        Debug.Log("📌 RunnerCallbacks Awake. DontDestroyOnLoad appliqué.");
        DontDestroyOnLoad(gameObject);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"🎯 OnPlayerJoined() appelé pour : {player}");

        if (player == runner.LocalPlayer)
        {
            Debug.Log("🧠 C'est le joueur local. Enregistrement temporaire du runner et playerRef.");
            this.runner = runner;
            this.localPlayerRef = player;
        }
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("✅ OnSceneLoadDone() → la scène est chargée.");

        if (!runner.IsServer)
        {
            Debug.Log("👥 Ce client n'est pas le serveur, pas de spawn à faire.");
            return;
        }

        if (GameManager_Fusion.Instance == null)
        {
            Debug.LogError("❌ GameManager_Fusion.Instance est null dans OnSceneLoadDone !");
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("❌ PlayerPrefab (NetworkPrefabRef) non assigné dans RunnerCallbacks !");
            return;
        }

        Vector3 spawnPos = GameManager_Fusion.Instance.GetRandomSpawnPosition();
        Debug.Log($"📍 Position de spawn obtenue : {spawnPos}");

        var playerObj = runner.Spawn(playerPrefab, spawnPos, Quaternion.identity, localPlayerRef);

        if (playerObj == null)
        {
            Debug.LogError("❌ Le runner.Spawn a échoué !");
            return;
        }

        Debug.Log("✅ runner.Spawn() a fonctionné !");
        GameManager_Fusion.Instance.RegisterPlayer(localPlayerRef, playerObj);
        Debug.Log("✅ Joueur local enregistré dans GameManager !");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"⛔ OnPlayerLeft() → {player}");

        if (GameManager_Fusion.Instance != null)
        {
            var obj = GameManager_Fusion.Instance.GetPlayerObject(player);
            if (obj != null)
            {
                runner.Despawn(obj);
                GameManager_Fusion.Instance.UnregisterPlayer(player);
                Debug.Log($"🧹 {player} despawné et supprimé du GameManager.");
            }
        }
    }

    public void OnConnectedToServer(NetworkRunner runner) => Debug.Log("🔌 Connecté au serveur.");
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) => Debug.LogWarning($"🚪 Déconnecté : {reason}");
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) => Debug.LogError($"❌ Connexion échouée : {reason}");
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) => Debug.Log($"🛑 Shutdown : {shutdownReason}");
    public void OnConnectedToHost(NetworkRunner runner) { }
    public void OnDisconnectedFromHost(NetworkRunner runner) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadStart(NetworkRunner runner) => Debug.Log("🕐 Début du chargement de scène...");
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) => Debug.Log($"📃 Liste des sessions mise à jour ({sessionList.Count})");
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
}
