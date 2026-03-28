using UnityEngine;

public class QuitGameButton : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip clickSound;

    private bool hasClicked = false;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        if (clickSound == null)
        {
            clickSound = Resources.Load<AudioClip>("Sounds/UIClick");
        }
    }

    public void QuitGame()
    {
        if (hasClicked) return;
        hasClicked = true;

        if (clickSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(clickSound);
            Invoke(nameof(QuitApplication), clickSound.length);
        }
        else
        {
            Debug.LogWarning("⚠️ Aucun son de clic assigné !");
            QuitApplication();
        }
    }

    private void QuitApplication()
    {
        Debug.Log("🚪 Quitter le jeu...");
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // Pour quitter dans l’éditeur
#endif
    }
}
