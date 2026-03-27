
using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

   public Vector3 GetSpawnPoint()
{
    if (spawnPoints == null || spawnPoints.Count == 0)
    {
        Debug.LogError("❌ Aucun spawn point défini !");
        return new Vector3(0, 2, 0); // fallback safe
    }

    int index = Random.Range(0, spawnPoints.Count);
    return spawnPoints[index].position;
}

    public List<Transform> GetAllSpawnPoints() => spawnPoints;
}
