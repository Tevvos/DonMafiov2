using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class AmmoSpawner : MonoBehaviour
{
    [Header("Prefab du pickup de munitions")]
    [SerializeField] private NetworkPrefabRef ammoPickupPrefab;

    [Header("Points de spawn (assigner dans la scène)")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Options")]
    [Tooltip("Si activé, spawn automatiquement au démarrage (côté serveur uniquement).")]
    [SerializeField] private bool spawnOnStart = true;

    [Tooltip("Temps avant respawn d'un pickup ramassé (secondes).")]
    [SerializeField] private float respawnDelay = 35f;

    [Tooltip("À quelle fréquence on vérifie si un point est vide (secondes).")]
    [SerializeField] private float checkInterval = 1.0f;

    private NetworkRunner runner;

    // 1 slot par spawn point (même index que spawnPoints)
    private NetworkObject[] _spawnedByIndex;

    // Pour éviter de lancer 50 coroutines pour le même point
    private bool[] _respawnPending;

    private IEnumerator Start()
    {
        // Attend que le NetworkRunner soit prêt
        runner = FindObjectOfType<NetworkRunner>();
        while (runner == null || !runner.IsRunning || !runner.IsServer)
            yield return null;

        _spawnedByIndex = new NetworkObject[spawnPoints.Length];
        _respawnPending = new bool[spawnPoints.Length];

        if (spawnOnStart)
        {
            yield return new WaitForSeconds(1f);
            RespawnAll();
        }

        // Boucle serveur: si un pickup a disparu -> respawn après délai
        StartCoroutine(ServerRespawnLoop());
    }

    private IEnumerator ServerRespawnLoop()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.05f, checkInterval));

        while (runner != null && runner.IsRunning && runner.IsServer)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                var sp = spawnPoints[i];
                if (!sp) continue;

                // Si le pickup de cet index a été despawn/détruit, Unity le considère "null"
                if (_spawnedByIndex[i] == null && !_respawnPending[i])
                {
                    _respawnPending[i] = true;
                    StartCoroutine(RespawnIndexAfterDelay(i));
                }
            }

            yield return wait;
        }
    }

    private IEnumerator RespawnIndexAfterDelay(int index)
    {
        float t = Mathf.Max(0.1f, respawnDelay);
        yield return new WaitForSeconds(t);

        // Si entre temps un RespawnAll() a déjà respawn ce point, on sort
        if (_spawnedByIndex == null || index < 0 || index >= _spawnedByIndex.Length)
            yield break;

        if (_spawnedByIndex[index] != null)
        {
            _respawnPending[index] = false;
            yield break;
        }

        SpawnAtIndex(index);
        _respawnPending[index] = false;
    }

    private void SpawnAtIndex(int index)
    {
        if (runner == null || !runner.IsServer) return;
        if (index < 0 || index >= spawnPoints.Length) return;

        var p = spawnPoints[index];
        if (!p) return;

        var no = runner.Spawn(ammoPickupPrefab, p.position, Quaternion.identity, null);
        _spawnedByIndex[index] = no;

        Debug.Log($"🔹 AmmoSpawner: respawn index={index}");
    }

    /// <summary>Supprime tous les pickups existants et respawn à neuf.</summary>
    public void RespawnAll()
    {
        if (runner == null || !runner.IsServer) return;

        ClearSpawned();

        // Recrée les tableaux si la taille a changé dans l’inspector
        if (_spawnedByIndex == null || _spawnedByIndex.Length != spawnPoints.Length)
            _spawnedByIndex = new NetworkObject[spawnPoints.Length];

        if (_respawnPending == null || _respawnPending.Length != spawnPoints.Length)
            _respawnPending = new bool[spawnPoints.Length];

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            _respawnPending[i] = false;

            var p = spawnPoints[i];
            if (!p) continue;

            var no = runner.Spawn(ammoPickupPrefab, p.position, Quaternion.identity, null);
            _spawnedByIndex[i] = no;
        }

        Debug.Log($"🔸 AmmoSpawner: RespawnAll -> {spawnPoints.Length} points.");
    }

    /// <summary>Supprime proprement tous les objets spawnés.</summary>
    public void ClearSpawned()
    {
        if (runner == null || !runner.IsServer) return;

        if (_spawnedByIndex == null)
            return;

        for (int i = 0; i < _spawnedByIndex.Length; i++)
        {
            var no = _spawnedByIndex[i];
            if (no != null)
            {
                if (no.Runner != null) runner.Despawn(no);
                else Destroy(no.gameObject);
            }
            _spawnedByIndex[i] = null;
            if (_respawnPending != null && i < _respawnPending.Length) _respawnPending[i] = false;
        }
    }
}
