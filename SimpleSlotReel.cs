using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class SimpleSlotReel : MonoBehaviour {
    [Header("Visuel")]
    [Tooltip("Mets tes sprites (slices du bandeau) du HAUT vers le BAS, dans l'ordre.")]
    [SerializeField] List<Sprite> symbols = new List<Sprite>();
    [Tooltip("UI (Image) = true. SpriteRenderer (monde) = false.")]
    [SerializeField] bool useUI = true;
    [Tooltip("Viewport: si UI, laisse vide -> le composant utilisera son propre RectTransform.")]
    [SerializeField] RectTransform viewport;
    [Tooltip("Taille d’un symbole (px si UI, unités monde si SpriteRenderer).")]
    [SerializeField] Vector2 itemSize = new Vector2(128,128);
    [Tooltip("Combien d’éléments visibles dans la fenêtre.")]
    [SerializeField] int visibleCount = 3;
    [SerializeField] float spacing = 0f; // espace entre symboles

    [Header("Moteur de défilement")]
    [SerializeField] float startSpeed = 1200f;   // px/sec
    [SerializeField] float maxSpeed   = 2400f;   // px/sec
    [SerializeField] float accelTime  = 0.35f;   // s
    [SerializeField] float minSpinTime= 0.8f;    // s plein régime
    [SerializeField] float decelTime  = 0.6f;    // s de ralentissement
    [SerializeField] int extraLoops   = 2;       // boucles avant l’arrêt

    [Header("Aléatoire pondéré (optionnel)")]
    [Tooltip("Poids par symbole (même taille que symbols). Laisse vide => poids = 1.")]
    [SerializeField] List<int> weights = new List<int>();
    [SerializeField] int seed = 0; // 0 = non déterministe
    System.Random rng;

    [Header("Events")]
    public UnityEvent<int> onStopped; // renvoie l’index final (dans la liste 'symbols')

    // --- Runtime ---
    class ViewItem {
        public RectTransform rt;
        public Image img;
        public Transform tf;
        public SpriteRenderer sr;
    }
    List<ViewItem> pool = new List<ViewItem>();
    RectTransform contentUI;
    Transform contentWorld;

    float itemStep;     // distance verticale d’un "cran"
    float offset;       // décalage en px/unité
    int topSymbolIndex; // index du symbole affiché en haut du viewport
    bool isSpinning;
    float speed;

    public bool IsSpinning => isSpinning;
    int CenterRow => Mathf.Max(0, visibleCount / 2);
    int SymbolsCount => symbols != null ? symbols.Count : 0;
    int Wrap(int i){ if (SymbolsCount==0) return 0; i%=SymbolsCount; if (i<0) i+=SymbolsCount; return i; }
    public int CurrentIndex => Wrap(topSymbolIndex + CenterRow);

    void Awake(){
        if (SymbolsCount == 0){
            Debug.LogWarning($"{name}: Pas de sprites dans 'symbols'.");
            enabled = false; return;
        }
        if (weights == null || weights.Count != SymbolsCount){
            weights = new List<int>(new int[SymbolsCount]);
            for (int i=0;i<SymbolsCount;i++) weights[i] = 1;
        }
        if (useUI && viewport == null){
            viewport = GetComponent<RectTransform>();
            if (!viewport) viewport = gameObject.AddComponent<RectTransform>();
        }
        rng = (seed == 0) ? new System.Random() : new System.Random(seed);
        Build();
    }

    void OnDisable() {
        StopAllCoroutines();
        isSpinning = false;
    }

    void Build(){
        itemStep = itemSize.y + spacing;

        // Conteneur
        if (useUI){
            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(transform, false);
            contentUI = contentGO.GetComponent<RectTransform>();
            contentUI.anchorMin = new Vector2(0.5f, 1f);
            contentUI.anchorMax = new Vector2(0.5f, 1f);
            contentUI.pivot    = new Vector2(0.5f, 1f);
            contentUI.anchoredPosition = Vector2.zero;

            var rt = (RectTransform)transform;
            if (rt.sizeDelta == Vector2.zero) rt.sizeDelta = new Vector2(itemSize.x, visibleCount * itemSize.y);
        } else {
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(transform, false);
            contentWorld = contentGO.transform;
            contentWorld.localPosition = Vector3.zero;
        }

        // Pool
        int needed = Mathf.Max(1, visibleCount + 2);
        for (int i=0;i<needed;i++){
            pool.Add(CreateItem(i));
        }

        topSymbolIndex = 0;
        offset = 0f;
        Refresh();
    }

    ViewItem CreateItem(int i){
        var vi = new ViewItem();
        var go = new GameObject($"Item_{i}");

        if (useUI){
            go.transform.SetParent(contentUI, false);
            vi.rt = go.AddComponent<RectTransform>();
            vi.rt.sizeDelta = itemSize;
            vi.img = go.AddComponent<Image>();
            vi.img.preserveAspect = true;
        } else {
            go.transform.SetParent(contentWorld, false);
            vi.tf = go.transform;
            vi.sr = go.AddComponent<SpriteRenderer>();
        }
        return vi;
    }

    void SetSprite(ViewItem v, Sprite s){
        if (useUI){
            v.img.sprite = s;
        } else {
            v.sr.sprite = s;
            var size = s.bounds.size;
            var sx = (size.x == 0)?1f:itemSize.x/size.x;
            var sy = (size.y == 0)?1f:itemSize.y/size.y;
            v.tf.localScale = new Vector3(sx, sy, 1f);
        }
    }

    void SetPos(ViewItem v, float y){
        if (useUI){
            v.rt.anchoredPosition = new Vector2(0f, y);
        } else {
            v.tf.localPosition = new Vector3(0f, y, 0f);
        }
    }

    void Refresh(){
        for (int i=0;i<pool.Count;i++){
            float y = -i * itemStep + offset;
            SetPos(pool[i], y);
            int sym = Wrap(topSymbolIndex + i);
            SetSprite(pool[i], symbols[sym]);
        }
    }

    void StepRaw(float pixels){
        offset -= pixels;
        while (offset <= -itemStep){
            offset += itemStep;
            topSymbolIndex = Wrap(topSymbolIndex + 1);
        }
        Refresh();
    }

    // --- Correctif: calcul précis de la distance au prochain cran ---
    float DistanceToNextStep() {
        float stepProg = Mathf.Repeat(-offset, itemStep); 
        float toNext = itemStep - stepProg; 
        if (toNext < 0.0001f) toNext = itemStep;
        return toNext;
    }

    IEnumerator SpinRoutine(int targetIndex){
        isSpinning = true;
        float hardTimeout = Time.realtimeSinceStartup + 10f;

        // Accélération
        float t = 0f;
        speed = startSpeed;
        while (t < accelTime){
            t += Time.deltaTime;
            speed = Mathf.Lerp(startSpeed, maxSpeed, t/accelTime);
            StepRaw(speed * Time.deltaTime);
            yield return null;
        }

        // Vitesse max minimum
        float run = 0f;
        while (run < minSpinTime){
            run += Time.deltaTime;
            StepRaw(maxSpeed * Time.deltaTime);
            yield return null;
        }

        // Ralentissement corrigé
        int n = SymbolsCount;
        int goalIndex = targetIndex;
        int stepsToGoal = ((goalIndex - CurrentIndex) % n + n) % n + extraLoops * n;

        float decT = 0f;
        while (stepsToGoal > 0) {
            decT += Time.deltaTime;
            float k = Mathf.Clamp01(decT / decelTime);
            float curSpeed = Mathf.Lerp(maxSpeed, 200f, k);
            float move = curSpeed * Time.deltaTime;

            float toNextStep = DistanceToNextStep();
            if (move >= toNextStep) {
                move = toNextStep - 0.0001f;
            }

            StepRaw(move);

            if (toNextStep - move <= 0.0002f) {
                StepRaw(0.0002f);
                stepsToGoal--;
            }

            if (Time.realtimeSinceStartup > hardTimeout) {
                Debug.LogWarning("SpinRoutine timeout -> snap final");
                break;
            }

            yield return null;
        }

        SnapToIndex(goalIndex);

        isSpinning = false;
        onStopped?.Invoke(goalIndex);
    }

    void SnapToIndex(int targetIndex){
        int center = CenterRow;
        int delta = ((targetIndex - center) - topSymbolIndex);
        topSymbolIndex = Wrap(topSymbolIndex + delta);
        offset = 0f;
        Refresh();
    }

    int PickWeightedIndex(){
        int total = 0;
        for (int i=0;i<weights.Count;i++) total += Mathf.Max(1, weights[i]);
        int roll = rng.Next(1, total+1);
        int acc = 0;
        for (int i=0;i<weights.Count;i++){
            acc += Mathf.Max(1, weights[i]);
            if (roll <= acc) return i;
        }
        return 0;
    }

    public void SpinRandom(){
        if (isSpinning || SymbolsCount==0) return;
        int idx = PickWeightedIndex();
        StartCoroutine(SpinRoutine(idx));
    }

    public void SpinToIndex(int idx){
        if (isSpinning || SymbolsCount==0) return;
        idx = Mathf.Clamp(idx, 0, SymbolsCount-1);
        StartCoroutine(SpinRoutine(idx));
    }

    public void SetSymbols(List<Sprite> newSymbols, List<int> newWeights = null){
        symbols = newSymbols ?? new List<Sprite>();
        if (newWeights == null || newWeights.Count != symbols.Count){
            weights = new List<int>(new int[symbols.Count]);
            for (int i=0;i<symbols.Count;i++) weights[i] = 1;
        } else weights = newWeights;
        pool.Clear();
        foreach (Transform c in (useUI ? (Transform)contentUI : contentWorld)) Destroy(c.gameObject);
        topSymbolIndex = 0; offset = 0f;
        pool = new List<ViewItem>();
        int needed = Mathf.Max(1, visibleCount + 2);
        for (int i=0;i<needed;i++) pool.Add(CreateItem(i));
        Refresh();
    }

    // Boutons de test (protégés)
    [ContextMenu("Test/Spin Random")]
    void CM_SpinRandom(){
        if (!Application.isPlaying) { Debug.LogWarning("Play Mode requis."); return; }
        SpinRandom();
    }
    [ContextMenu("Test/Spin To 0")]
    void CM_Spin0(){
        if (!Application.isPlaying) { Debug.LogWarning("Play Mode requis."); return; }
        SpinToIndex(0);
    }
// Nombre total de symboles (pour RandomizeReelsStart)
public int GetSymbolsCount() {
    return symbols != null ? symbols.Count : 0;
}

// Force l'affichage immédiat d'un symbole au centre, sans animation
public void ForceSymbol(int index) {
    if (symbols == null || symbols.Count == 0) return;
    index = Mathf.Clamp(index, 0, symbols.Count - 1);

    // Place 'index' au centre visuel
    int center = Mathf.Max(0, visibleCount / 2);
    topSymbolIndex = Wrap(index - center);
    offset = 0f;
    Refresh();
}

}
