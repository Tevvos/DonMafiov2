using UnityEngine;
using UnityEngine.UI; // Nécessaire pour accéder à l'InputField et au Button

public class NameInputHandler : MonoBehaviour
{
    public InputField nameInputField; // Référence à l'InputField dans l'UI
    public Button submitButton; // Référence au bouton qui soumet le nom

    void Start()
    {
        // Ajouter un écouteur d'événement pour le bouton
        submitButton.onClick.AddListener(OnSubmitName);
    }

    void OnSubmitName()
    {
        // Vérifier si un nom a été entré
        if (!string.IsNullOrEmpty(nameInputField.text))
        {
            // Sauvegarder le nom dans PlayerPrefs
            PlayerPrefs.SetString("PlayerName", nameInputField.text);
            PlayerPrefs.Save(); // Sauvegarder les données dans PlayerPrefs
            Debug.Log("Nom sauvegardé : " + nameInputField.text);

            // Charger la scène du jeu après soumission du nom
            UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        }
        else
        {
            Debug.LogWarning("Le nom ne peut pas être vide !");
        }
    }
}
