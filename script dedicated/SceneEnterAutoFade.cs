using UnityEngine;
using System.Collections;

public class SceneEnterAutoFade : MonoBehaviour
{
    public enum Mode { FadeFromBlack, FadeToBlack }

    [SerializeField] Mode onStart = Mode.FadeFromBlack;
    [SerializeField] float duration = 0.35f;
    [SerializeField] float delay = 0.25f;    // laisse un court temps aux spawns/initialisations
    [SerializeField] bool forceFaderOnTop = true;

    IEnumerator Start()
    {
        SimpleFader.Ensure();

        if (forceFaderOnTop) BringFaderToFront();

        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        if (onStart == Mode.FadeFromBlack)
            yield return SimpleFader.Instance.FadeFromBlack(duration);
        else
            yield return SimpleFader.Instance.FadeToBlack(duration);
    }

    // S'assure que le fader est au-dessus de TOUT (utile si un Canvas UI a un sortingOrder énorme)
    void BringFaderToFront()
    {
        if (SimpleFader.Instance == null) return;
        var faderCanvas = SimpleFader.Instance.GetComponentInChildren<UnityEngine.Canvas>(true);
        if (faderCanvas == null) return;

        int maxOrder = faderCanvas.sortingOrder;
        var canvases = FindObjectsOfType<UnityEngine.Canvas>();
        foreach (var c in canvases)
        {
            if (c == null || c == faderCanvas) continue;
            if (c.sortingOrder > maxOrder) maxOrder = c.sortingOrder;
        }

        faderCanvas.overrideSorting = true;
        faderCanvas.sortingOrder = maxOrder + 1;
    }
}
