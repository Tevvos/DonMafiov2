using UnityEngine;
using TMPro;
using PlayFab;
using PlayFab.ClientModels;

public class PseudoChanger : MonoBehaviour
{
    [SerializeField] private TMP_InputField newPseudoInput;

    public void ChangePseudo()
    {
        string newPseudo = newPseudoInput.text.Trim();

        if (string.IsNullOrEmpty(newPseudo) || newPseudo.Length < 3)
        {
            Debug.LogWarning("⚠️ Le pseudo doit contenir au moins 3 caractères !");
            return;
        }

        var request = new UpdateUserTitleDisplayNameRequest
        {
            DisplayName = newPseudo
        };

        PlayFabClientAPI.UpdateUserTitleDisplayName(request, result =>
        {
            Debug.Log("✅ Pseudo mis à jour sur PlayFab : " + result.DisplayName);

            // Stockage local
            PlayerPrefs.SetString("Username", result.DisplayName);

            // Mise à jour de l'UI joueur
            FindObjectOfType<PlayerPreviewDisplay>()?.UpdateUsernameDisplay();

        }, error =>
        {
            Debug.LogError("❌ Erreur lors du changement de pseudo : " + error.GenerateErrorReport());
        });
    }
}
