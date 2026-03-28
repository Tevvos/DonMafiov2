using UnityEngine;
using UnityEngine.UI;
using PlayFab;
using System.Collections;

public class PlayButtonVisibility : MonoBehaviour
{
    [Header("🎮 Bouton Play")]
    [SerializeField] private GameObject playButton;

    [Header("⏳ Délai avant affichage")]
    [SerializeField] private float delayBeforeShow = 1f;

    private void Start()
    {
        if (playButton == null)
        {
            Debug.LogWarning("⚠️ Bouton Play non assigné !");
            return;
        }

        // 🔒 Cache immédiatement
        playButton.SetActive(false);

        // ✅ Démarre vérification + délai
        StartCoroutine(CheckConnectionAndShow());
    }

    private IEnumerator CheckConnectionAndShow()
    {
        yield return new WaitForSeconds(delayBeforeShow);

        if (PlayFabClientAPI.IsClientLoggedIn())
        {
            Debug.Log("✅ Joueur connecté, bouton Play affiché après délai.");
            playButton.SetActive(true);
        }
        else
        {
            Debug.Log("🔒 Joueur non connecté, bouton Play reste masqué.");
        }
    }
}
