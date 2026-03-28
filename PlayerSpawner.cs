
using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();

    public Vector3 GetSpawnPoint()
    {
        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning("Aucun point de spawn défini.");
            return Vector3.zero;
        }

        int index = Random.Range(0, spawnPoints.Count);
        return spawnPoints[index].position;
    }

    public List<Transform> GetAllSpawnPoints() => spawnPoints;
}
