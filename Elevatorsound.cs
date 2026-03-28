using UnityEngine;

public class ElevatorSound : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip enterSound;
    public AudioClip exitSound;

    private void Start()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) // Vérifie que c'est bien un joueur
        {
            Debug.Log("🎵 Son de l'ascenseur (entrée)");
            if (audioSource != null && enterSound != null)
            {
                audioSource.PlayOneShot(enterSound);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player")) // Vérifie que c'est bien un joueur
        {
            Debug.Log("🎵 Son de l'ascenseur (sortie)");
            if (audioSource != null && exitSound != null)
            {
                audioSource.PlayOneShot(exitSound);
            }
        }
    }
}
