using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class ButtonClickWithSoundAndScale : MonoBehaviour
{
    public AudioClip clickSound; // 🎵 Son du clic
    public float delayBeforeSceneChange = 2f; // ⏳ Délai avant de changer de scène
    public string sceneToLoad = "ChooseNameScene"; // 🎯 Scène à charger
    private AudioSource audioSource;
    private Button button;
    private Vector3 originalScale;

    void Start()
    {
        button = GetComponent<Button>(); // 📌 Récupère le bouton
        audioSource = GetComponent<AudioSource>(); // 🎵 Récupère l'AudioSource
        originalScale = transform.localScale; // Sauvegarde la taille initiale

        if (button != null)
        {
            button.onClick.AddListener(OnButtonClick);
        }
        else
        {
            Debug.LogError("❌ Le bouton n'a pas été trouvé sur cet objet !");
        }
    }

    public void OnButtonClick()
    {
        StartCoroutine(HandleButtonClick()); // 🚀 Lance l'effet et le changement de scène
    }

    private IEnumerator HandleButtonClick()
    {
        // 🎵 Joue le son du clic
        if (audioSource != null && clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }

        // 🔄 Réduit la taille du bouton temporairement
        transform.localScale = originalScale * 0.9f;

        // ⏳ Attends la fin du son
        yield return new WaitForSeconds(clickSound.length);

        // 🔄 Restaure la taille du bouton après le clic
        transform.localScale = originalScale;

        // ⏳ Attends avant de changer de scène
        yield return new WaitForSeconds(delayBeforeSceneChange);

        // 🎯 Change de scène
        SceneManager.LoadScene(sceneToLoad);
    }
}
