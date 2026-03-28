using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerPreviewMover : MonoBehaviour
{
    [Header("Mouvement")]
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float pauseDuration = 1.5f;
    [SerializeField] private float movementRange = 2f;

    [Header("Direction / Flip")]
    [Tooltip("Transform qui contient tous les sprites (ex: visualRoot).")]
    [SerializeField] private Transform visualRoot;
    [Tooltip("Flip par scale.x (recommandé). Sinon flipX sur chaque SpriteRenderer.")]
    [SerializeField] private bool flipWithScale = true;
    [Tooltip("À l'arrêt, se tourner vers la prochaine cible.")]
    [SerializeField] private bool faceTowardTargetWhileIdle = true;

    [Header("💨 Fumée (Animator optionnel)")]
    [SerializeField, Range(0f,1f)] private float smokeChance = 0.4f;
    [SerializeField] private float smokeLoopMinDuration = 4f;
    [SerializeField] private float smokeLoopMaxDuration = 6f;
    [SerializeField] private float postSmokePause = 0.4f;

    [Header("Noms d'états Animator (doivent correspondre)")]
    [SerializeField] private string smokeEnterState = "Smoke_Enter";
    [SerializeField] private string smokeLoopState  = "Smoke_Loop";
    [SerializeField] private string smokeOutState   = "Smoke_Out";
    [SerializeField, Tooltip("Durée du CrossFade entre états (s)")]
    private float crossFadeTime = 0.06f;

    // --- internals
    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer[] srs;

    private Vector2 startPosition;
    private Vector2 targetPosition;
    private float waitTimer;
    private bool isPaused;
    private bool isSmoking;

    private float lastMoveX = 1f;               // -1 gauche, +1 droite
    private float baseScaleX = 1f;
    private bool loggedMissingAnimator;

    // seuils
    private const float arriveThreshold = 0.05f;
    private const float dirEpsilon = 0.01f;     // évite flip sur micro-variations

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (visualRoot == null) visualRoot = transform;
        baseScaleX = Mathf.Abs(visualRoot.localScale.x) < 0.0001f ? 1f : Mathf.Abs(visualRoot.localScale.x);

        // Auto-find Animator : sur moi, visualRoot, puis enfants
        animator = GetComponent<Animator>();
        if (animator == null && visualRoot != null) animator = visualRoot.GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        srs = GetComponentsInChildren<SpriteRenderer>(true);
    }

    private void Start()
    {
        startPosition = transform.position;
        PickNewTarget();               // oriente aussi vers la cible
        DarkenSprite();

        if (animator == null && !loggedMissingAnimator)
        {
            Debug.LogWarning("[PlayerPreviewMover] Aucun Animator trouvé (fumée ignorée).", this);
            loggedMissingAnimator = true;
        }
    }

    private void Update()
    {
        if (isSmoking)
        {
            rb.linearVelocity = Vector2.zero;
            SetAnim(Vector2.zero);
            return;
        }

        if (isPaused)
        {
            if (faceTowardTargetWhileIdle)
            {
                float desiredX = Mathf.Sign(targetPosition.x - transform.position.x);
                if (Mathf.Abs(desiredX) >= 1f)
                {
                    lastMoveX = desiredX;
                    ApplyFlip(lastMoveX);
                    if (animator) animator.SetFloat("MoveX", lastMoveX);
                }
            }

            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                if (animator != null && Random.value < smokeChance)
                    StartCoroutine(PlaySmokeRoutine());
                else
                {
                    isPaused = false;
                    PickNewTarget();
                }
            }

            rb.linearVelocity = Vector2.zero;
            SetAnim(Vector2.zero);
            return;
        }

        // --- Mouvement actif ---
        Vector2 currentPos = transform.position;
        Vector2 toTarget = targetPosition - currentPos;
        Vector2 dir = toTarget.normalized;

        rb.linearVelocity = dir * moveSpeed;

        // Détermination robuste du côté
        if (Mathf.Abs(toTarget.x) > dirEpsilon)
        {
            float desired = Mathf.Sign(toTarget.x); // -1 ou +1
            if (!Mathf.Approximately(desired, lastMoveX))
            {
                lastMoveX = desired;
                ApplyFlip(lastMoveX);
            }
        }

        SetAnim(dir);

        if (toTarget.sqrMagnitude < arriveThreshold * arriveThreshold)
        {
            isPaused = true;
            waitTimer = Random.Range(1f, pauseDuration + 1f);
            rb.linearVelocity = Vector2.zero;
            SetAnim(Vector2.zero);
        }
    }

    private void PickNewTarget()
    {
        float offset = Random.Range(-movementRange, movementRange);
        targetPosition = new Vector2(startPosition.x + offset, startPosition.y);

        // Oriente immédiatement vers la future direction
        float desiredX = Mathf.Sign(targetPosition.x - transform.position.x);
        if (Mathf.Abs(desiredX) >= 1f)
        {
            lastMoveX = desiredX;
            ApplyFlip(lastMoveX);
            if (animator) animator.SetFloat("MoveX", lastMoveX);
        }
    }

    private void SetAnim(Vector2 dir)
    {
        if (animator != null)
        {
            animator.SetFloat("Speed", dir.magnitude);
            animator.SetFloat("MoveX", lastMoveX);
        }
    }

    private void ApplyFlip(float moveX)
    {
        if (Mathf.Approximately(moveX, 0f)) return;

        bool faceLeft = moveX < 0f;

        if (flipWithScale)
        {
            Vector3 s = visualRoot.localScale;
            s.x = baseScaleX * (faceLeft ? -1f : 1f);
            visualRoot.localScale = s;
        }
        else
        {
            if (srs == null || srs.Length == 0)
                srs = GetComponentsInChildren<SpriteRenderer>(true);

            for (int i = 0; i < srs.Length; i++)
                if (srs[i]) srs[i].flipX = faceLeft;
        }
    }

    private void DarkenSprite()
    {
        if (srs == null || srs.Length == 0)
            srs = GetComponentsInChildren<SpriteRenderer>(true);

        foreach (var sr in srs)
            if (sr) sr.color = new Color(0.6f, 0.6f, 0.6f, 1f);
    }

    // --- Helper : attendre d'être effectivement dans un état Animator
    private IEnumerator WaitUntilInState(int stateHash, float maxWait = 1f)
    {
        float t = 0f;
        while (t < maxWait)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.fullPathHash == stateHash || st.shortNameHash == stateHash) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator PlaySmokeRoutine()
    {
        if (animator == null)
        {
            // Pas d'Animator → simple pause “fumée”
            isSmoking = true;
            yield return new WaitForSeconds(Random.Range(smokeLoopMinDuration, smokeLoopMaxDuration) + postSmokePause);
            isSmoking = false;
            isPaused = false;
            PickNewTarget();
            yield break;
        }

        isSmoking = true;

        // Forcer les états (plus de triggers ratés)
        animator.ResetTrigger("Smoke");
        animator.ResetTrigger("SmokeOut");

        int hEnter = Animator.StringToHash(smokeEnterState);
        int hLoop  = Animator.StringToHash(smokeLoopState);
        int hOut   = Animator.StringToHash(smokeOutState);

        // 1) Entrée
        animator.CrossFadeInFixedTime(smokeEnterState, crossFadeTime, 0);
        yield return WaitUntilInState(hEnter, 0.5f);
        yield return new WaitForSeconds(0.5f); // petite entrée

        // 2) Boucle
        animator.CrossFadeInFixedTime(smokeLoopState, crossFadeTime, 0);
        yield return WaitUntilInState(hLoop, 0.5f);

        float loop = Random.Range(smokeLoopMinDuration, smokeLoopMaxDuration);
        yield return new WaitForSeconds(loop);

        // 3) Sortie
        animator.CrossFadeInFixedTime(smokeOutState, crossFadeTime, 0);
        yield return WaitUntilInState(hOut, 0.5f);

        yield return new WaitForSeconds(0.6f);     // temps de sortie estimé
        yield return new WaitForSeconds(postSmokePause);

        isSmoking = false;
        isPaused = false;
        PickNewTarget();
    }
}
