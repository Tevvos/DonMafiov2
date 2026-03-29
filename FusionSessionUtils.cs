using Fusion;
using Fusion.Sockets;
using Fusion.Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class FusionSessionUtils
{
    public static async Task<List<SessionInfo>> GetSessionList(this NetworkRunner runner)
    {
        var taskCompletion = new TaskCompletionSource<List<SessionInfo>>();

        var callback = new TempSessionListCallback(sessions =>
        {
            taskCompletion.TrySetResult(sessions);
        });

        runner.AddCallbacks(callback);

        // 🔁 Rejoint le lobby pour récupérer la liste
        runner.JoinSessionLobby(SessionLobby.ClientServer);

        // 🕒 Timeout de sécurité (3 secondes max d'attente)
        var timeoutTask = Task.Delay(3000);
        var completedTask = await Task.WhenAny(taskCompletion.Task, timeoutTask);

        runner.RemoveCallbacks(callback);

        if (completedTask == timeoutTask)
        {
            UnityEngine.Debug.LogWarning("⏱️ Timeout lors de la récupération des serveurs actifs.");
            return new List<SessionInfo>();
        }

        return await taskCompletion.Task;
    }

    private class TempSessionListCallback : INetworkRunnerCallbacks
    {
        private readonly Action<List<SessionInfo>> onSessionList;

        public TempSessionListCallback(Action<List<SessionInfo>> onSessionList)
        {
            this.onSessionList = onSessionList;
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            onSessionList?.Invoke(sessionList);
        }

        // 🧱 Implémentation minimale obligatoire
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectSpawned(NetworkRunner runner, NetworkObject obj) { }
        public void OnObjectDespawned(NetworkRunner runner, NetworkObject obj) { }
        public void OnDisconnectedFromPhotonServer(NetworkRunner runner) { }
        public void OnConnectToPhotonFailed(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnCustomAuthenticationFailed(NetworkRunner runner, string error) { }
    }
}
