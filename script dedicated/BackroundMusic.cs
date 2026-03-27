using UnityEngine;
using UnityEngine.SceneManagement;

public class BackgroundMusic : MonoBehaviour
{
    private static BackgroundMusic instance;

    [SerializeField] private AudioClip[] musics; // 🎵 Liste des musiques assignées dans l’Inspector
    private AudioSource audioSource;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true; // boucle en continu
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
        if (scene.name == "GameScene01") 
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; 
            Destroy(gameObject); 
        }
    }
}
