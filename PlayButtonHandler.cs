using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PlayButtonHandler : MonoBehaviour
{
    [Header("🎬 Animation & Audio")]
    [SerializeField] private Animator gunAnimator;
    [SerializeField] private string reloadTrigger = "ReloadTri";
    [SerializeField] private AudioSource reloadAudio;

    [Header("🕓 Délai avant le changement de scène")]
    [SerializeField] private float delayBeforeSceneLoad = 2.5f;

    [Header("🎮 Bouton & Scène")]
    [SerializeField] private Button playButton;
    [SerializeField] private string sceneToLoad = "MultiplayerScene";

    private bool animationStarted = false;

    private void Start()
    {
        if (playButton != null)
            playButton.interactable = false; // 🔒 bouton désactivé au départ
    }

    private void Update()
    {
        if (gunAnimator != null && !animationStarted)
        {
            AnimatorStateInfo stateInfo = gunAnimator.GetCurrentAnimatorStateInfo(0);

            // Dès qu'une animation a commencé à jouer
            if (stateInfo.normalizedTime < 1f && stateInfo.length > 0f)
            {
                animationStarted = true;

                if (playButton != null)
                {
                    playButton.interactable = true;
                    Debug.Log("✅ Animation détectée → bouton Play activé !");
                }
            }
        }
    }

    public void OnPlayButtonClicked()
    {
        if (playButton != null)
            playButton.interactable = false;

        if (gunAnimator != null)
        {
            gunAnimator.SetTrigger(reloadTrigger);
            Debug.Log("🔫 Animation 'ReloadTri' lancée !");
        }

        if (reloadAudio != null)
            reloadAudio.Play();

        Invoke(nameof(LoadSceneAfterDelay), delayBeforeSceneLoad);
    }

    private void LoadSceneAfterDelay()
    {
        Debug.Log("🚪 Chargement de la scène : " + sceneToLoad);
        SceneManager.LoadScene(sceneToLoad);
    }
}
