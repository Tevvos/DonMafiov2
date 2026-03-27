using UnityEngine;

public class ElevatorTrigger : MonoBehaviour
{
    public Animator elevatorAnimator;   
    public AudioSource audioSource;  // Source audio unique
    public AudioClip openSound;      // Son pour l'ouverture
    public AudioClip closeSound;     // Son pour la fermeture
    public string playerTag = "Player";  

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(playerTag)) 
        {
            elevatorAnimator.ResetTrigger("CloseElevator"); 
            elevatorAnimator.SetTrigger("OpenElevator");

            // Joue le son d'ouverture si disponible
            if (audioSource != null && openSound != null)
            {
                audioSource.PlayOneShot(openSound);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(playerTag)) 
        {
            elevatorAnimator.ResetTrigger("OpenElevator"); 
            elevatorAnimator.SetTrigger("CloseElevator");

            // Joue le son de fermeture si disponible
            if (audioSource != null && closeSound != null)
            {
                audioSource.PlayOneShot(closeSound);
            }
        }
    }
}
