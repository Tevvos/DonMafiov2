using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;

[DefaultExecutionOrder(-1000)]
public class Bootloader : MonoBehaviour
{
    // Dans le nouveau modèle serveur dédié, le Bootloader ne crée plus
    // de FusionRunnerManager persistant. Le runner est géré directement
    // par FusionMultiplayerManager dans MultiplayerScene.
    // Ce script est conservé pour compatibilité mais désactivé côté runner.

    [Header("Legacy (désactivé en mode serveur dédié)")]
    [SerializeField] private GameObject fusionManagerPrefab;

    [Header("Mode serveur dédié")]
    [Tooltip("Si activé, le Bootloader ne crée PAS de runner persistant.\nLe runner est géré par FusionMultiplayerManager.")]
    [SerializeField] private bool dedicatedServerMode = true;

    private static Bootloader _singleton;
    private static bool _quitting;

    private void Awake()
    {
        if (_singleton != null && _singleton != this) { Destroy(gameObject); return; }
        _singleton = this;
        DontDestroyOnLoad(gameObject);

        // En mode serveur dédié : on ne crée plus de runner persistant ici.
        // FusionMultiplayerManager crée son propre runner quand nécessaire.
        if (dedicatedServerMode)
        {
            Debug.Log("[Bootloader] Mode serveur dédié : pas de FusionRunnerManager persistant.");
            return;
        }

        // Legacy : ancien mode host (conservé si tu veux revenir en arrière)
        EnsureFusionManager();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_singleton == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnApplicationQuit() => _quitting = true;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }

    private void EnsureFusionManager()
    {
        if (fusionManagerPrefab == null)
        {
            Debug.LogWarning("[Bootloader] fusionManagerPrefab non assigné (mode legacy).");
            return;
        }

        var existing = FindObjectOfType<NetworkRunner>();
        if (existing != null) return;

        var go = Instantiate(fusionManagerPrefab);
        go.name = "FusionRunnerManager";
        DontDestroyOnLoad(go);
        Debug.Log("[Bootloader] FusionRunnerManager créé (mode legacy).");
    }
}
