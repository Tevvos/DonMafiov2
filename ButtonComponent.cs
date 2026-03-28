using UnityEngine;
using UnityEngine.UI;

public class ButtonClickEffect : MonoBehaviour
{
    private Button button;

    void Start()
    {
        // Récupère le composant Button attaché à l'objet
        button = GetComponent<Button>();

        // Vérifie si le bouton existe et ajoute l'écouteur de clic
        if (button != null)
        {
            button.onClick.AddListener(OnButtonClick);
        }
        else
        {
            Debug.LogError("Le composant Button n'a pas été trouvé !");
        }
    }

    void OnButtonClick()
    {
        // Action à réaliser lors du clic (par exemple, un changement de couleur ou un message)
        Debug.Log("Le bouton a été cliqué !");
    }
}
