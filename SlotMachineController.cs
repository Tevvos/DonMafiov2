using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections;

public class SlotMachineController : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private SimpleSlotReel[] reels;   // Assigne Reel_01, Reel_02, Reel_03
    [SerializeField] private Button           spinButton;

    [Header("Sons")]
    [SerializeField] private AudioSource sfxStart;
    [SerializeField] private AudioSource sfxEnd;
    [SerializeField] private AudioSource sfxJackpot;

    [Header("Paramètres")]
    [SerializeField] private float delayBetweenReels = 0.3f;
    [SerializeField] private bool  jackpotOnAllSame  = true;
    [SerializeField] private bool  randomizeOnStart  = true;

    // État
    private bool  isSpinning;
    private int   reelsStopped;
    private int[] lastResults;

    // Handlers mémorisés pour RemoveListener propre
    private UnityAction<int>[] onStoppedHandlers;

    void Awake()
    {
        if (reels == null) reels = new SimpleSlotReel[0];
        EnsureBuffers(reels.Length);
    }

    void OnEnable()
    {
        // (ré)assure la bonne taille des buffers (au cas où l’inspector a changé)
        EnsureBuffers(reels.Length);

        // S’abonner un frame plus tard => évite “Collection was modified” si des events tournent
        StartCoroutine(SubscribeNextFrame());
    }

    void OnDisable()
    {
        UnsubscribeAll();
    }

    void Start()
    {
        if (spinButton != null)
            spinButton.onClick.AddListener(OnSpinPressed);

        if (randomizeOnStart)
            RandomizeReelsStart();

        if (reels == null || reels.Length == 0)
            Debug.LogWarning($"{name}: Aucun reel assigné.");
    }

    // ---------- Helpers de souscription ----------
    private IEnumerator SubscribeNextFrame()
    {
        yield return null; // attend 1 frame
        UnsubscribeAll();  // évite doubles abonnements si OnEnable a été appelé plusieurs fois

        for (int i = 0; i < reels.Length; i++)
        {
            var r = reels[i];
            if (r == null) continue;

            // Crée l’UnityEvent s’il est null
            if (r.onStopped == null)
                r.onStopped = new UnityEvent<int>();

            // Handler capturant l’index
            int k = i;
            onStoppedHandlers[k] = (finalIdx) => OnReelStopped(k, finalIdx);
            r.onStopped.AddListener(onStoppedHandlers[k]);
        }
    }

    private void UnsubscribeAll()
    {
        if (reels == null || onStoppedHandlers == null) return;

        for (int i = 0; i < reels.Length; i++)
        {
            if (reels[i] == null) continue;
            if (onStoppedHandlers[i] != null && reels[i].onStopped != null)
            {
                reels[i].onStopped.RemoveListener(onStoppedHandlers[i]);
            }
            onStoppedHandlers[i] = null;
        }
    }

    private void EnsureBuffers(int count)
    {
        if (lastResults == null || lastResults.Length != count)
            lastResults = new int[count];

        if (onStoppedHandlers == null || onStoppedHandlers.Length != count)
            onStoppedHandlers = new UnityAction<int>[count];
    }

    // ---------- Démarrage visuel aléatoire ----------
    private void RandomizeReelsStart()
    {
        if (reels == null) return;
        foreach (var reel in reels)
        {
            if (reel == null) continue;
            int count = reel.GetSymbolsCount(); // doit exister dans SimpleSlotReel
            if (count <= 0) continue;
            reel.ForceSymbol(Random.Range(0, count)); // doit exister dans SimpleSlotReel
        }
    }

    // ---------- Input ----------
    private void OnSpinPressed()
    {
        if (isSpinning) return;
        if (reels == null || reels.Length == 0)
        {
            Debug.LogWarning("Pas de reels assignés.");
            return;
        }

        isSpinning   = true;
        reelsStopped = 0;

        if (sfxStart) sfxStart.Play();
        StartCoroutine(SpinSequence());
    }

    private IEnumerator SpinSequence()
    {
        for (int i = 0; i < reels.Length; i++)
        {
            if (reels[i] != null)
                reels[i].SpinRandom();

            if (i < reels.Length - 1)
                yield return new WaitForSeconds(delayBetweenReels);
        }
    }

    // Reçu via UnityEvent<int> onStopped de chaque reel
    private void OnReelStopped(int reelIndex, int finalIndex)
    {
        if (reelIndex < 0 || reelIndex >= lastResults.Length) return;
        lastResults[reelIndex] = finalIndex;
        reelsStopped++;

        if (reelsStopped >= reels.Length)
            EndSpin();
    }

    private void EndSpin()
    {
        isSpinning = false;

        bool hasJackpot = false;

        if (jackpotOnAllSame && reels.Length >= 3)
        {
            int refIdx = lastResults[0];
            bool allSame = true;
            for (int i = 1; i < reels.Length; i++)
                if (lastResults[i] != refIdx) { allSame = false; break; }

            if (allSame)
            {
                hasJackpot = true;
                if (sfxJackpot) sfxJackpot.Play();
                Debug.Log($"🎉 JACKPOT ! Index = {refIdx}");
            }
        }

        if (!hasJackpot && sfxEnd)
            sfxEnd.Play();

        Debug.Log("🎰 Tous les rouleaux sont arrêtés.");
    }

#if UNITY_EDITOR
    [ContextMenu("Test/Spin")]
    private void CM_Spin() => OnSpinPressed();
#endif
}
