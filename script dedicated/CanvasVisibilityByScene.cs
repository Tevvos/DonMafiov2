using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CanvasGroup))]
public class CanvasVisibilityByScene : MonoBehaviour
{
    [Header("Show this Canvas only in these scenes")]
    [SerializeField] private string[] visibleInScenes = { "MultiplayerScene" };

    private CanvasGroup cg;

    private void Awake()
    {
        cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyVisibility(SceneManager.GetActiveScene().name);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyVisibility(scene.name);
    }

    private void ApplyVisibility(string sceneName)
    {
        bool shouldShow = IsSceneWhitelisted(sceneName);

        // Surtout ne pas SetActive sur ce GameObject : on garder le script vivant.
        cg.alpha = shouldShow ? 1f : 0f;
        cg.interactable = shouldShow;
        cg.blocksRaycasts = shouldShow;
    }

    private bool IsSceneWhitelisted(string sceneName)
    {
        for (int i = 0; i < visibleInScenes.Length; i++)
        {
            if (visibleInScenes[i] == sceneName) return true;
        }
        return false;
    }
}
