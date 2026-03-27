using UnityEngine;
using TMPro;
using PlayFab;
using PlayFab.ClientModels;

public class UsernameChangerUI : MonoBehaviour
{
    [Header("🖊️ Champs UI")]
    [SerializeField] private TMP_InputField newUsernameInput;
    [SerializeField] private TextMeshProUGUI feedbackText;

    private const int minLength = 3;

    public void OnChangeUsernameClick()
    {
        string newName = newUsernameInput.text.Trim();

        if (newName.Length < minLength)
        {
            feedbackText.text = $" Nom trop court (min {minLength} caractères)";
            return;
        }

        // Tu peux ajouter un check de caractères interdits ici si besoin

        // Enregistre localement
        PlayerPrefs.SetString("Username", newName);

        // Mets à jour sur PlayFab
        var request = new UpdateUserTitleDisplayNameRequest
        {
            DisplayName = newName
        };

        PlayFabClientAPI.UpdateUserTitleDisplayName(request, result =>
        {
            feedbackText.text = $" Pseudo mis à jour : {newName}";

            // Mise à jour de l'affichage (si player preview actif)
            if (FindObjectOfType<PlayerPreviewDisplay>() is PlayerPreviewDisplay preview)
            {
                preview.UpdateUsernameDisplay();
            }

        }, error =>
        {
            feedbackText.text = $" Erreur PlayFab : {error.ErrorMessage}";
        });
    }
}
