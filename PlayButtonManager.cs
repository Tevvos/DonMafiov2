using UnityEngine;
using UnityEngine.SceneManagement;
using PlayFab;

public class PlayButtonManager : MonoBehaviour
{
    [Header("🎮 Paramètres")]
    [SerializeField] private string multiplayerSceneName = "MultiplayerScene";

    /// <summary>
    /// Appelé quand on clique sur le bouton "Play".
    /// </summary>
    public void OnPlayClicked()
    {
        if (PlayFabClientAPI.IsClientLoggedIn())
        {
            Debug.Log("✅ Connexion confirmée, chargement de la scène multijoueur...");
            SceneManager.LoadScene(multiplayerSceneName);
        }
        else
        {
            Debug.LogWarning("❌ Joueur non connecté, accès refusé !");
        }
    }
}
