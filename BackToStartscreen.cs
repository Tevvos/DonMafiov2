using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class BackToStartscreen : MonoBehaviour
{
    [Header("Options")]
    [Tooltip("Appuyer sur Échap retourne au menu avec arrêt propre du runner.")]
    [SerializeField] private bool enableEscapeKey = true;

    [Tooltip("Index de la scène Startscreen (fallback si le manager est introuvable).")]
    [SerializeField] private int startscreenBuildIndex = 0;

    [Tooltip("Désactiver l'interaction pendant la transition pour éviter le double-clic.")]
    [SerializeField] private bool disableCurrentSelectableOnClick = true;

    /// <summary>
    /// A assigner sur ton bouton 'Retour Menu'.
    /// </summary>
    public void OnClickBackToMenu()
    {
        // (Optionnel) Empêche le double-clic pendant la transition
        if (disableCurrentSelectableOnClick && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            var selectable = EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.Selectable>();
            if (selectable != null) selectable.interactable = false;
        }

        SafeReturnToMenu();
    }

    private void Update()
    {
        if (!enableEscapeKey) return;

        // Retour menu sur Échap
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SafeReturnToMenu();
        }
    }

    /// <summary>
    /// Toujours privilégier cet appel au lieu d'un SceneManager.LoadScene direct.
    /// Il déclenche un shutdown propre du NetworkRunner via FusionMultiplayerManager.
    /// </summary>
    private void SafeReturnToMenu()
    {
        var mgr = FusionMultiplayerManager.Instance;

        if (mgr != null)
        {
            // Shutdown propre puis load Startscreen depuis le manager
            mgr.SafeReturnToMenu();
        }
        else
        {
            // Fallback si le manager est introuvable (ex. en test isolé)
            Debug.LogWarning("BackToStartscreen: FusionMultiplayerManager introuvable, fallback vers LoadScene direct.");
            try
            {
                SceneManager.LoadScene(startscreenBuildIndex);
            }
            catch
            {
                Debug.LogError("BackToStartscreen: Échec LoadScene fallback. Vérifie ton Build Settings.");
            }
        }
    }

    /// <summary>
    /// (Optionnel) A assigner sur un bouton 'Quitter' si tu veux quitter l'appli.
    /// </summary>
    public void OnClickQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
