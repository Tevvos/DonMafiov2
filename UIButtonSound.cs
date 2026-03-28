using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class UIButtonSound : MonoBehaviour
{
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private AudioSource audioSource;

    private void Awake()
    {
        if (audioSource == null)
        {
            // Optionnel : chercher un AudioSource global dans la scène
            audioSource = FindObjectOfType<AudioSource>();
        }

        GetComponent<Button>().onClick.AddListener(PlayClickSound);
    }

    private void PlayClickSound()
    {
        if (audioSource != null && clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
        else
        {
            Debug.LogWarning("🔇 Aucun son ou AudioSource assigné pour le bouton : " + gameObject.name);
        }
    }
}
