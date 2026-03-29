using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// AmmoSpawner — GameMode.Shared
/// En Shared il n'y a pas de runner.IsServer global.
/// Ce NetworkBehaviour spawne les pickups uniquement sur l'objet
/// qui a la StateAuthority (= le premier client qui arrive, ou celui
/// à qui Fusion assigne l'autorité sur cet objet scène).
/// </summary>
public class AmmoSpawner : NetworkBehaviour
{
    [Header("Prefab du pickup de munitions")]
    [SerializeField] private NetworkPrefabRef ammoPickupPrefab;

    [Header("Points de spawn (assigner dans la scène)")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Options")]
    [Tooltip("Si activé, spawn automatiquement au démarrage (côté StateAuthority uniquement).")]
    [SerializeField] private bool spawnOnStart = true;

    [Tooltip("Temps avant respawn d'un pickup ramassé (secondes).")]
    [SerializeField] private float respawnDelay = 35f;

    [Tooltip("À quelle fréquence on vérifie si un point est vide (secondes).")]
    [SerializeField] private float checkInterval = 1.0f;

    // 1 slot par spawn point
    private NetworkObject[] _spawnedByIndex;
    private bool[] _respawnPending;

    // ──────────────────────────────────────────────────────────────────
    //  Fusion hooks
    // ──────────────────────────────────────────────────────────────────

    public override void Spawned()
    {
        // Seul le StateAuthority de cet objet gère le spawn des pickups
        if (!Object.HasStateAuthority) return;

        _spawnedByIndex = new NetworkObject[spawnPoints.Length];
        _respawnPending  = new bool[spawnPoints.Length];

        if (spawnOnStart)
            StartCoroutine(CoDelayedSpawnAll());

        StartCoroutine(ServerRespawnLoop());
    }

    // ──────────────────────────────────────────────────────────────────
    //  Coroutines
    // ──────────────────────────────────────────────────────────────────

    private IEnumerator CoDelayedSpawnAll()
    {
        yield return new WaitForSeconds(1f);
        RespawnAll();
    }

    private IEnumerator ServerRespawnLoop()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.05f, checkInterval));

        while (Runner != null && Runner.IsRunning && Object.HasStateAuthority)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                var sp = spawnPoints[i];
                if (!sp) continue;

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
        yield return new WaitForSeconds(Mathf.Max(0.1f, respawnDelay));

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
        // ✅ Shared : Object.HasStateAuthority au lieu de runner.IsServer
        if (!Object.HasStateAuthority) return;
        if (index < 0 || index >= spawnPoints.Length) return;

        var p = spawnPoints[index];
        if (!p) return;

        var no = Runner.Spawn(ammoPickupPrefab, p.position, Quaternion.identity, null);
        _spawnedByIndex[index] = no;

        Debug.Log($"🔹 AmmoSpawner: spawn index={index}");
    }

    // ──────────────────────────────────────────────────────────────────
    //  API publique
    // ──────────────────────────────────────────────────────────────────

    public void RespawnAll()
    {
        // ✅ Shared : Object.HasStateAuthority
        if (!Object.HasStateAuthority) return;

        ClearSpawned();

        if (_spawnedByIndex == null || _spawnedByIndex.Length != spawnPoints.Length)
            _spawnedByIndex = new NetworkObject[spawnPoints.Length];
        if (_respawnPending == null || _respawnPending.Length != spawnPoints.Length)
            _respawnPending = new bool[spawnPoints.Length];

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            _respawnPending[i] = false;
            var p = spawnPoints[i];
            if (!p) continue;
            var no = Runner.Spawn(ammoPickupPrefab, p.position, Quaternion.identity, null);
            _spawnedByIndex[i] = no;
        }

        Debug.Log($"🔸 AmmoSpawner: RespawnAll → {spawnPoints.Length} points.");
    }

    public void ClearSpawned()
    {
        // ✅ Shared : Object.HasStateAuthority
        if (!Object.HasStateAuthority) return;
        if (_spawnedByIndex == null) return;

        for (int i = 0; i < _spawnedByIndex.Length; i++)
        {
            var no = _spawnedByIndex[i];
            if (no != null)
            {
                if (no.Runner != null) Runner.Despawn(no);
                else Destroy(no.gameObject);
            }
            _spawnedByIndex[i] = null;
            if (_respawnPending != null && i < _respawnPending.Length)
                _respawnPending[i] = false;
        }
    }
}
