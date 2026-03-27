using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[System.Serializable] public class StringEvent : UnityEvent<string> {}

public class ServerEntryJoinWithFade : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button joinButton;
    [SerializeField] private Text   titleText;

    [Header("Fade")]
    [SerializeField] private float fadeDuration = 0.35f;

    [Header("Join")]
    // Branche ta méthode Join(string roomName) ici (UIFusionLauncher.JoinByName, etc.)
    public StringEvent OnJoinRequested;

    [Header("Auto-retour si rien ne se passe")]
    [SerializeField] private float fallbackSeconds = 3f; // <- 3 secondes

    private bool busy;
    private bool completed;             // succès/échec OU changement de scène
    private string sessionName;
    private Coroutine watchdogCo;

    void Reset()
    {
        joinButton = GetComponentInChildren<Button>();
        titleText  = GetComponentInChildren<Text>();
    }

    void Awake()
    {
        if (joinButton != null)
            joinButton.onClick.AddListener(OnClickJoin);

        // Si une scène change, on considère que tout est en cours → le fade-in sera fait par la nouvelle scène
        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
    }

    public void Setup(string session)
    {
        sessionName = session;
        if (titleText) titleText.text = session;
    }

    void EnsureSessionName()
    {
        if (string.IsNullOrEmpty(sessionName) && titleText)
            sessionName = titleText.text;
    }

    public void OnClickJoin()
    {
        if (busy) return;
        StartCoroutine(JoinFlow());
    }

    IEnumerator JoinFlow()
    {
        busy = true;
        completed = false;

        EnsureSessionName();

        SimpleFader.Ensure();
        BringFaderToFront();

        // Lance un watchdog qui remontera l'écran tout seul au bout de fallbackSeconds si rien ne se passe
        if (watchdogCo != null) { StopCoroutine(watchdogCo); watchdogCo = null; }
        watchdogCo = StartCoroutine(FallbackRecoverAfterRealtime(fallbackSeconds));

        // Passe au noir puis déclenche ta jointure
        yield return SimpleFader.Instance.FadeToBlack(fadeDuration);
        OnJoinRequested?.Invoke(sessionName);

        // On laisse le watchdog décider de remonter si aucun callback/scene change
        busy = false;
    }

    IEnumerator FallbackRecoverAfterRealtime(float seconds)
    {
        float remaining = seconds;
        while (remaining > 0f && !completed)
        {
            yield return new WaitForSecondsRealtime(0.1f);
            remaining -= 0.1f;
        }

        if (!completed)
        {
            // Rien ne s’est passé → on remonte pour revoir la scène
            SimpleFader.Ensure();
            SimpleFader.Instance.FadeFromBlack(fadeDuration);
            completed = true;
        }

        watchdogCo = null;
    }

    void OnSceneChanged(Scene prev, Scene next)
    {
        // Un changement de scène annule le fallback (la nouvelle scène fera son FadeFromBlack)
        completed = true;
        if (watchdogCo != null) { StopCoroutine(watchdogCo); watchdogCo = null; }
    }

    void BringFaderToFront()
    {
        if (SimpleFader.Instance == null) return;
        var faderCanvas = SimpleFader.Instance.GetComponentInChildren<Canvas>(true);
        if (faderCanvas == null) return;

        int maxOrder = faderCanvas.sortingOrder;
        var all = FindObjectsOfType<Canvas>();
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null || c == faderCanvas) continue;
            if (c.sortingOrder > maxOrder) maxOrder = c.sortingOrder;
        }
        faderCanvas.overrideSorting = true;
        faderCanvas.sortingOrder = maxOrder + 1;
    }

    // Optionnel : si ton manager Fusion veut forcer le retour sans changer de scène
    public void NotifyJoinFailed()
    {
        if (completed) return;
        completed = true;
        if (watchdogCo != null) { StopCoroutine(watchdogCo); watchdogCo = null; }
        SimpleFader.Ensure();
        SimpleFader.Instance.FadeFromBlack(fadeDuration);
    }

    // Optionnel : si tu restes dans la même scène mais que la jointure a finalement réussi
    public void NotifyJoinSucceeded()
    {
        if (completed) return;
        completed = true;
        if (watchdogCo != null) { StopCoroutine(watchdogCo); watchdogCo = null; }
        SimpleFader.Ensure();
        SimpleFader.Instance.FadeFromBlack(fadeDuration);
    }
}
