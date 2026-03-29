using UnityEngine;
using Fusion;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// WeaponSpawner — GameMode.Shared
/// Hérite de NetworkBehaviour pour avoir Object.HasStateAuthority.
/// En Shared il n'y a pas de runner.IsServer global :
/// l'objet scène reçoit une StateAuthority automatique.
/// </summary>
public class WeaponSpawner : NetworkBehaviour
{
    [System.Serializable]
    public class WeaponEntry
    {
        public NetworkPrefabRef prefab;
        [Range(0, 100)] public float spawnChance = 50f;
    }

    [Header("Armes avec taux de probabilité (%)")]
    [SerializeField] private List<WeaponEntry> weaponPrefabs = new();

    [Header("Points de spawn")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Options")]
    [SerializeField] private bool spawnOnStart = true;

    private readonly List<NetworkObject> _spawned = new();

    // ──────────────────────────────────────────────────────────────────
    //  Fusion hook
    // ──────────────────────────────────────────────────────────────────

    public override void Spawned()
    {
        // ✅ Shared : Object.HasStateAuthority
        if (!Object.HasStateAuthority) return;

        if (spawnOnStart)
            StartCoroutine(CoDelayedSpawnAll());
    }

    private IEnumerator CoDelayedSpawnAll()
    {
        yield return new WaitForSeconds(1f);
        RespawnAll();
    }

    // ──────────────────────────────────────────────────────────────────
    //  API publique
    // ──────────────────────────────────────────────────────────────────

    public void RespawnAll()
    {
        // ✅ Shared : Object.HasStateAuthority
        if (!Object.HasStateAuthority) return;
        ClearSpawned();
        SpawnWeapons();
    }

    public void ClearSpawned()
    {
        // ✅ Shared : Object.HasStateAuthority
        if (!Object.HasStateAuthority) return;

        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            var no = _spawned[i];
            if (no)
            {
                if (no.Runner != null) Runner.Despawn(no);
                else Destroy(no.gameObject);
            }
        }
        _spawned.Clear();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Interne
    // ──────────────────────────────────────────────────────────────────

    private void SpawnWeapons()
    {
        if (spawnPoints == null || spawnPoints.Length == 0 || weaponPrefabs.Count == 0)
        {
            Debug.LogWarning("⚠️ WeaponSpawner: aucun spawn point ou prefab assigné.");
            return;
        }

        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;

            NetworkPrefabRef selected = GetRandomWeapon();
            if (!selected.IsValid) continue;

            var no = Runner.Spawn(selected, point.position, Quaternion.identity, inputAuthority: null);
            if (no) _spawned.Add(no);

            Debug.Log($"📦 WeaponSpawner: arme spawnée à {point.name}");
        }

        Debug.Log($"✅ WeaponSpawner: {_spawned.Count} armes spawnées.");
    }

    private NetworkPrefabRef GetRandomWeapon()
    {
        float total = 0f;
        foreach (var e in weaponPrefabs) total += e.spawnChance;

        float rand = Random.Range(0f, total);
        float cumul = 0f;
        foreach (var e in weaponPrefabs)
        {
            cumul += e.spawnChance;
            if (rand <= cumul) return e.prefab;
        }
        return weaponPrefabs[0].prefab;
    }
}
