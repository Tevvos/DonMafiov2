using UnityEngine;
using Fusion;
using System.Collections;
using System.Collections.Generic;

public class WeaponSpawner : MonoBehaviour
{
    [System.Serializable]
    public class WeaponEntry
    {
        public NetworkPrefabRef prefab;
        [Range(0, 100)] public float spawnChance = 50f;
    }

    [Header("📦 Armes avec taux de probabilité (%)")]
    [SerializeField] private List<WeaponEntry> weaponPrefabs = new();

    [Header("📍 Points de spawn (assignés dans la scène)")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("⚙️ Options")]
    [SerializeField] private bool spawnOnStart = true;

    private NetworkRunner runner;
    private readonly List<NetworkObject> _spawned = new();

    private IEnumerator Start()
    {
        runner = FindObjectOfType<NetworkRunner>();
        while (runner == null || !runner.IsRunning || !runner.IsServer)
            yield return null;

        if (spawnOnStart)
        {
            yield return new WaitForSeconds(1f);
            RespawnAll();
        }
    }

    // ------ API publique ------
    public void RespawnAll()
    {
        if (runner == null || !runner.IsServer) return;
        ClearSpawned();
        SpawnWeapons();
    }

    public void ClearSpawned()
{
    if (runner == null || !runner.IsServer) return;

    for (int i = _spawned.Count - 1; i >= 0; i--)
    {
        var no = _spawned[i];
        if (no)
        {
            // Fusion 2 : plus de IsSpawned → on teste si l'objet est attaché à un Runner
            if (no.Runner != null) runner.Despawn(no);
            else Destroy(no.gameObject);
        }
    }
    _spawned.Clear();
}


    // ------ interne ------
    private void SpawnWeapons()
    {
        if (spawnPoints == null || spawnPoints.Length == 0 || weaponPrefabs.Count == 0)
        {
            Debug.LogWarning("⚠️ Aucun spawn point ou prefab assigné.");
            return;
        }

        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;

            NetworkPrefabRef selected = GetRandomWeapon();
            if (!selected.IsValid) continue;

            var no = runner.Spawn(
                selected,
                point.position,
                Quaternion.identity,
                inputAuthority: null
            );

            if (no) _spawned.Add(no);
            Debug.Log($"📦 Arme instanciée à {point.name}");
        }

        Debug.Log($"✅ {_spawned.Count} armes spawnées.");
    }

    private NetworkPrefabRef GetRandomWeapon()
    {
        float totalWeight = 0f;
        foreach (var entry in weaponPrefabs) totalWeight += entry.spawnChance;

        float rand = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        foreach (var entry in weaponPrefabs)
        {
            cumulative += entry.spawnChance;
            if (rand <= cumulative)
                return entry.prefab;
        }

        return weaponPrefabs[0].prefab;
    }
}
