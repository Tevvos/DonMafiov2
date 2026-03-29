using UnityEngine;
using UnityEngine.SceneManagement;

public class BackgroundMusic : MonoBehaviour
{
    private static BackgroundMusic instance;

    [SerializeField] private AudioClip[] musics;

    // FIX : utilise le build index au lieu du nom de scène en dur
    // Si tu renommes la scène de jeu, seule cette valeur est à changer
    [SerializeField, Tooltip("Build index de la scène de jeu où la musique menu doit s'arrêter.")]
    private int gameSceneBuildIndex = 2;

    private AudioSource audioSource;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.playOnAwake = false;

            PlayRandomMusic();

            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void PlayRandomMusic()
    {
        if (musics != null && musics.Length > 0)
        {
            int index = Random.Range(0, musics.Length);
            audioSource.clip = musics[index];
            audioSource.Play();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // FIX : comparaison par build index, robuste aux renommages de scène
        if (scene.buildIndex == gameSceneBuildIndex)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Destroy(gameObject);
        }
    }
}
