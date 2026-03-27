using UnityEngine;

public class PlayerPreviewSpawner : MonoBehaviour
{
    [Header("👤 Skin de prévisualisation")]
    [SerializeField] private GameObject[] playerSkins; // À assigner dans l'inspector
    [SerializeField] private Transform previewSpawnPoint;

    private void Start()
    {
        int skinIndex = PlayerPrefs.GetInt("PlayerSkin", 0); // À remplacer par une donnée PlayFab plus tard
        if (skinIndex >= 0 && skinIndex < playerSkins.Length)
        {
            GameObject preview = Instantiate(playerSkins[skinIndex], previewSpawnPoint.position, Quaternion.identity);
            preview.transform.SetParent(previewSpawnPoint); // Pour qu’il reste dans la hiérarchie
        }
        else
        {
            Debug.LogWarning("⚠️ Skin index invalide pour la preview.");
        }
    }
}
