using UnityEngine;
using Fusion;
using System.Collections.Generic;

public class GameManager_Fusion : MonoBehaviour
{
    public static GameManager_Fusion Instance;

    [SerializeField] private PlayerSpawner spawner;
    private readonly Dictionary<PlayerRef, NetworkObject> players = new();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ----------------- Spawn helpers -----------------

    /// <summary>
    /// Fait le spawn ici (si possible), enregistre le joueur et notifie GameRules.
    /// </summary>
    public NetworkObject SpawnAndRegister(NetworkRunner runner, PlayerRef playerRef, NetworkObject playerPrefab)
    {
        if (runner == null || playerPrefab == null || playerRef == PlayerRef.None)
        {
            Debug.LogWarning("[GM] SpawnAndRegister: paramètres invalides.");
            return null;
        }

        Vector3 pos = GetSpawnPosition();
        Quaternion rot = Quaternion.identity;

        var no = runner.Spawn(playerPrefab, pos, rot, playerRef);
        RegisterPlayer(playerRef, no);

        // 🔔 Très important : notifier les règles que ce joueur est "en lice"
        GameRules_Victory_Fusion.NotifySpawn(playerRef);
        Debug.Log($"[GM] SpawnAndRegister -> {playerRef} at {pos}");

        return no;
        // NOTE: Si tu as une logique custom d’équipement/visuals, fais-la après l'appel.
    }

    /// <summary>
    /// Si tu spawnes le joueur ailleurs (PlayerSpawner / callbacks Runner),
    /// appelle cette méthode juste après le spawn pour enregistrer + notifier.
    /// </summary>
    public void AfterExternalSpawn(PlayerRef playerRef, NetworkObject spawnedObject)
    {
        if (playerRef == PlayerRef.None || !spawnedObject)
        {
            Debug.LogWarning("[GM] AfterExternalSpawn: arguments invalides.");
            return;
        }

        RegisterPlayer(playerRef, spawnedObject);
        GameRules_Victory_Fusion.NotifySpawn(playerRef);
        Debug.Log($"[GM] AfterExternalSpawn -> {playerRef} ({spawnedObject.name})");
    }

    /// <summary>
    /// À appeler quand un joueur quitte / est despawn : unregister + notifier GameRules.
    /// </summary>
    public void HandleDespawn(PlayerRef playerRef)
    {
        if (playerRef == PlayerRef.None) return;

        UnregisterPlayer(playerRef);
        GameRules_Victory_Fusion.NotifyQuit(playerRef);
        Debug.Log($"[GM] HandleDespawn -> {playerRef}");
    }

    // ----------------- Positions -----------------

    public Vector3 GetSpawnPosition()
    {
        return spawner != null ? spawner.GetSpawnPoint() : Vector3.zero;
    }

    // Compat héritée
    public Vector3 GetRandomSpawnPosition() => GetSpawnPosition();

    // ----------------- Registry -----------------

    public void RegisterPlayer(PlayerRef player, NetworkObject obj)
    {
        if (player == PlayerRef.None || !obj) return;

        if (!players.ContainsKey(player))
        {
            players.Add(player, obj);
        }
        else
        {
            players[player] = obj; // refresh
        }
    }

    public void UnregisterPlayer(PlayerRef player)
    {
        if (players.ContainsKey(player))
            players.Remove(player);
    }

    public NetworkObject GetPlayerObject(PlayerRef player)
    {
        players.TryGetValue(player, out var obj);
        return obj;
    }

    public bool HasValidSpawn(PlayerRef player) => players.ContainsKey(player);

    // Nouveaux helpers
    public IReadOnlyDictionary<PlayerRef, NetworkObject> Players => players;

    public void SetPlayerTransform(PlayerRef player, Vector3 pos, Quaternion rot)
    {
        if (players.TryGetValue(player, out var no) && no)
        {
            no.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
