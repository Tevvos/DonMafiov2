using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Fusion;

public class WinPresenter : MonoBehaviour
{
    public static WinPresenter Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private TextMeshProUGUI winnerNameText;
    [SerializeField] private TextMeshProUGUI punchlineText;
    [SerializeField] private Image backdropDim;

    [Header("Auto Return Countdown")]
    [SerializeField] private TextMeshProUGUI autoReturnText;
    [SerializeField] private float delayBeforeCounter = 5f;
    [SerializeField] private float counterDuration = 10f;
    private Coroutine autoReturnCo;

    [Header("Timings")]
    [SerializeField, Min(0f)] private float fadeIn = 0.25f;
    [SerializeField, Min(0f)] private float fadeOut = 0.0f;

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip sfxVictory;
    [SerializeField] private AudioClip sfxLose;
    [SerializeField] private AudioClip sfxStalemate;

    [Header("Texts")]
    [TextArea(2, 4)] [SerializeField] private string[] victoryLines =
    {
        "BULLETS DON’T LIE.",
        "ONE DON LEFT STANDING.",
        "THE HOUSE ALWAYS WINS."
    };

    [TextArea(2, 4)] [SerializeField] private string[] loseLines =
    {
        "YOU’RE OUT. BETTER LUCK.",
        "SLEPT WITH THE FISHES.",
        "NEXT ROUND, KID."
    };

    [TextArea(2, 4)] [SerializeField] private string[] stalemateLines =
    {
        "EVERYONE FOLDED.",
        "NO KING TONIGHT.",
        "NOBODY MADE IT OUT."
    };

    Coroutine fadeCo;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (group)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.gameObject.SetActive(false);
        }

        if (backdropDim) backdropDim.enabled = false;
        if (autoReturnText) autoReturnText.gameObject.SetActive(false);
    }

    // --- NEW: garde-fou état de jeu ---
    bool IsInGame()
    {
        return MatchFlow_Fusion.HasInstance && MatchFlow_Fusion.Instance.MatchStarted;
    }

    public void ShowResult(PlayerRef winnerRef, string winnerName)
    {
        // Bloque toute victoire si on n’est pas en game
        if (!IsInGame())
        {
            Debug.Log("[WinPresenter] Ignored ShowResult: not in-game (lobby or round over).");
            return;
        }

        var runner = FindObjectOfType<NetworkRunner>();
        var local = runner ? runner.LocalPlayer : PlayerRef.None;
        bool iAmWinner = (local != PlayerRef.None && local == winnerRef);

        if (string.IsNullOrWhiteSpace(winnerName)) winnerName = "UNKNOWN";

        if (iAmWinner)
        {
            PreparePanel("VICTORY",
                         winnerName.ToUpperInvariant(),
                         Pick(victoryLines, "ONE DON LEFT STANDING."),
                         sfxVictory);
        }
        else
        {
            PreparePanel("YOU LOSE",
                         winnerName.ToUpperInvariant(),
                         Pick(loseLines, "NEXT ROUND, KID."),
                         sfxLose ? sfxLose : sfxStalemate);
        }
    }

    public void ShowStalemate()
    {
        // Bloque le stalemate si on n’est pas en game
        if (!IsInGame())
        {
            Debug.Log("[WinPresenter] Ignored ShowStalemate: not in-game (lobby or round over).");
            return;
        }
        PreparePanel("STALEMATE", "", Pick(stalemateLines, "NO WINNER."), sfxStalemate);
    }

    public void HideImmediate()
    {
        if (fadeCo != null) StopCoroutine(fadeCo);
        if (autoReturnCo != null) StopCoroutine(autoReturnCo);

        if (group)
        {
            group.alpha = 0f;
            group.gameObject.SetActive(false);
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        if (backdropDim) backdropDim.enabled = false;
        if (autoReturnText) autoReturnText.gameObject.SetActive(false);
    }

    void PreparePanel(string titleText, string winnerText, string line, AudioClip sfx)
    {
        if (fadeCo != null) StopCoroutine(fadeCo);

        if (title) title.text = titleText;
        if (winnerNameText) winnerNameText.text = winnerText;
        if (punchlineText) punchlineText.text = line;

        if (group && !group.gameObject.activeSelf) group.gameObject.SetActive(true);
        if (backdropDim) backdropDim.enabled = true;
        if (group)
        {
            group.alpha = 0f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }

        Play(sfx);
        fadeCo = StartCoroutine(Fade(0f, 1f, Mathf.Max(0.01f, fadeIn)));

        // démarre la coroutine du retour auto
        if (autoReturnCo != null) StopCoroutine(autoReturnCo);
        autoReturnCo = StartCoroutine(AutoReturnSequence());
    }

    IEnumerator AutoReturnSequence()
    {
        if (autoReturnText) autoReturnText.gameObject.SetActive(false);
        yield return new WaitForSecondsRealtime(delayBeforeCounter);

        float timer = counterDuration;
        if (autoReturnText) autoReturnText.gameObject.SetActive(true);

        while (timer > 0f)
        {
            if (autoReturnText)
                autoReturnText.text = $"Retour au lobby dans {Mathf.CeilToInt(timer)}...";
            timer -= Time.unscaledDeltaTime;
            yield return null;
        }

        ReturnToLobby();
    }

    void ReturnToLobby()
    {
        if (autoReturnCo != null) StopCoroutine(autoReturnCo);
        if (autoReturnText) autoReturnText.gameObject.SetActive(false);

        var t = System.Type.GetType("MatchFlow_Fusion");
        if (t != null)
        {
            var instProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var inst = instProp?.GetValue(null, null);
            var m = t.GetMethod("RequestReturnToLobby", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (inst != null && m != null) m.Invoke(inst, null);
        }
        HideImmediate();
    }

    IEnumerator Fade(float from, float to, float time)
    {
        if (!group || time <= 0f)
        {
            if (group) group.alpha = to;
            yield break;
        }

        float t = 0f; group.alpha = from;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / time));
            yield return null;
        }
        group.alpha = to;
    }

    string Pick(string[] arr, string fallback)
    {
        if (arr == null || arr.Length == 0) return fallback;
        int i = Random.Range(0, arr.Length);
        return string.IsNullOrWhiteSpace(arr[i]) ? fallback : arr[i];
    }

    void Play(AudioClip clip)
    {
        if (!audioSource || !clip) return;
        audioSource.PlayOneShot(clip);
    }
}
