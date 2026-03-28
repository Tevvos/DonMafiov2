
using UnityEngine;

public class FusionRunnerManager : MonoBehaviour
{
    [SerializeField] private GameObject statusText;

    public void ShowErrorUI(string message)
    {
        // ⚠️ Exemple simple : à remplacer par ton UI réelle
        Debug.Log("⚠️ UI Message: " + message);
    }
}
