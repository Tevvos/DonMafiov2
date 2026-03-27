using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;

[DefaultExecutionOrder(-1000)]
public class Bootloader : MonoBehaviour
{
    [Header("Assign the prefab here (root must have NetworkRunner)")]
    [SerializeField] private GameObject fusionManagerPrefab;

    private static Bootloader _singleton;
    private static GameObject _fusionManager;
    private static bool _quitting; // ne pas respawn quand on quitte l’app

    private void Awake()
    {
        // Singleton Bootloader
        if (_singleton != null && _singleton != this) { Destroy(gameObject); return; }
        _singleton = this;
        DontDestroyOnLoad(gameObject);

        EnsureFusionManager(); // crée si manquant

        // Optionnel: tu peux écouter les scènes, mais on NE TOUCHE PAS au manager ici.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_singleton == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnApplicationQuit()
    {
        _quitting = true;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Rien à faire côté manager (il est persistant).
        // Si tu veux remettre l’UI menu visible, fais-le dans un autre script via CanvasGroup.
    }

    // ---------- Cœur : s’assure qu’une instance unique existe ----------
    private static void EnsureFusionManager()
    {
        if (_fusionManager != null) return;

        var boot = _singleton;
        if (boot == null)
        {
            Debug.LogError("❌ Bootloader singleton introuvable.");
            return;
        }
        if (boot.fusionManagerPrefab == null)
        {
            Debug.LogError("❌ Bootloader: 'fusionManagerPrefab' non assigné.");
            return;
        }

        _fusionManager = Instantiate(boot.fusionManagerPrefab);
        _fusionManager.name = "FusionRunnerManager";
        DontDestroyOnLoad(_fusionManager);

        // Santé : il DOIT avoir un NetworkRunner à la racine
        var runner = _fusionManager.GetComponent<NetworkRunner>();
        if (runner == null)
            Debug.LogError("🟥 FusionRunnerManager doit avoir un NetworkRunner sur l'objet racine.");

        // Sentinel anti-destruction : respawn auto si quelqu’un fait Destroy()
        var sentinel = _fusionManager.GetComponent<FusionManagerSentinel>();
        if (sentinel == null) sentinel = _fusionManager.AddComponent<FusionManagerSentinel>();
        sentinel.onDestroyed = OnManagerDestroyed;

        Debug.Log("✅ Bootloader: FusionRunnerManager créé (persistant).");
    }

    private static void OnManagerDestroyed()
    {
        // Appelé par le sentinel quand l’objet est détruit.
        if (_quitting) return; // on ne respawn pas quand on ferme l’app
        _fusionManager = null;
        Debug.LogWarning("⚠️ FusionRunnerManager détruit → respawn automatique.");
        // respawn à la frame suivante (évite conflit avec la destruction en cours)
        _singleton.StartCoroutine(_singleton.RespawnNextFrame());
    }

    private System.Collections.IEnumerator RespawnNextFrame()
    {
        yield return null;
        EnsureFusionManager();
    }

    // -------- Sentinel interne : détecte disable/destroy --------
    private class FusionManagerSentinel : MonoBehaviour
    {
        public System.Action onDestroyed;

        private void OnDisable()
        {
            // Empêche qu’un script “cache” le manager
            if (!_quitting)
            {
                Debug.LogWarning("⚠️ FusionRunnerManager DISABLE → re-enable immédiat.");
                gameObject.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            onDestroyed?.Invoke();
        }
    }
}
