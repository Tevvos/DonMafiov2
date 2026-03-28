using UnityEngine;
using Fusion;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement_FusionPro : NetworkBehaviour
{
    // ===== Networked sync =====
    [Networked] private float       NetSpeed     { get; set; }
    [Networked] private int         NetFacingX   { get; set; }   // -1 / +1
    [Networked] private NetworkBool NetArmed     { get; set; }
    [Networked] private int         NetAimSign   { get; set; }   // -1 / +1
    [Networked] private NetworkBool NetWantsMove { get; set; }   // intention de bouger (anti-reset anim)

    [Header("Movement")]
    [SerializeField] private float moveSpeed    = 3.5f;
    [SerializeField] private float acceleration = 40f;
    [SerializeField] private float deceleration = 60f;

    // --- BONUS CLASSE : FURTIF ---
    [SerializeField] private float speedMultiplier = 1f; // 1 = vitesse normale

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string speedFloatParam = "Speed";
    [SerializeField] private string moveXFloatParam = "MoveX";
    [SerializeField] private string armedBoolParam  = "Armed";
    [SerializeField] private string armedBoolAlt    = "IsArmed";

    [Tooltip("Vitesse d'anim minimale pour 'Walk' quand on pousse un mur (évite les resets).")]
    [SerializeField] private float minWalkAnimSpeed = 0.3f;

    [Tooltip("Seuil sous lequel on considère la vitesse comme nulle (anti-bruit).")]
    [SerializeField] private float zeroSpeedEpsilon = 0.02f;

    [Tooltip("Lissage quand on bouge (évite les snaps).")]
    [SerializeField] private float animDampOnMove = 0.08f;

    [Tooltip("Lissage quand on s'arrête (0 = arrêt immédiat).")]
    [SerializeField] private float animDampOnStop = 0f;

    [Header("Refs")]
    [SerializeField] private PlayerHealth playerHealth;

    public enum FlipSource { Aim, Move }
    [Header("VISUEL UNIQUEMENT (flip du corps)")]
    [SerializeField] private Transform      visualRoot;        // ← on flippe CE nœud
    [SerializeField] private SpriteRenderer bodyRenderer;      // utilisé SEULEMENT si visualRoot est null
    [Tooltip("Choix de la source du flip: Aim = direction de visée, Move = direction de déplacement")]
    [SerializeField] private FlipSource flipBy = FlipSource.Move;
    [Tooltip("+1 si les sprites regardent à DROITE par défaut, -1 s’ils regardent à GAUCHE")]
    [SerializeField] private int spriteDefaultFacing = +1;

    private Rigidbody2D rb;
    private int  lastFacingX = 1;
    private bool _isArmed;
    private bool _wantsMoveLocal; // intention locale (input authority)

    /// Exposé pour PlayerWeapon : true si orienté droite (choix Right/Left holder)
    public bool FacingRight => lastFacingX >= 0;

    public override void Spawned()
    {
        rb ??= GetComponent<Rigidbody2D>();
        if (!playerHealth) playerHealth = GetComponent<PlayerHealth>();

        // Récupération robuste du visualRoot si non assigné manuellement
        if (!visualRoot)
        {
            var vr = transform.Find("VisualRoot");
            if (vr) visualRoot = vr;

            if (!visualRoot)
            {
                var anySR = GetComponentInChildren<SpriteRenderer>();
                if (anySR) visualRoot = anySR.transform.parent ? anySR.transform.parent : anySR.transform;
            }
        }

        if (!bodyRenderer && visualRoot)
            bodyRenderer = visualRoot.GetComponentInChildren<SpriteRenderer>();

        if (Object.HasStateAuthority)
        {
            if (NetFacingX == 0) NetFacingX = 1;
            if (NetAimSign  == 0) NetAimSign = 1;
        }
        else
        {
            lastFacingX = (NetFacingX == 0) ? 1 : NetFacingX;
            if (NetAimSign == 0) NetAimSign = 1;
            ApplyArmedToAnimator(NetArmed);
            ApplyVisualFlip(GetFlipSign());
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!rb) return;

        if (playerHealth && playerHealth.IsDead())
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }
// ✅ IMPORTANT : ne pas simuler le mouvement des PROXIES (ni input authority, ni state authority)
// Sinon on les "freine" localement à 0 → ils se figent → puis TP quand l'état réseau arrive.
bool canSimulateMovement = Object.HasInputAuthority || Object.HasStateAuthority;
if (!canSimulateMovement)
{
    // On laisse NetworkRigidbody2D / Fusion appliquer la position/vitesse reçue du réseau.
    // (On garde quand même les flags réseau pour l'anim/flip via Render())
    return;
}


        Vector2 inputDir = Vector2.zero;
        bool wantsMove = false;

        // Valeurs “safe” au cas où GetInput échoue (ex: proxy sans input)
        int aimSignFromInput = (NetAimSign == 0) ? 1 : NetAimSign;

        if (GetInput(out PlayerInputData input))
        {
            float rawA = input.AimAngle;
            bool deg   = Mathf.Abs(rawA) > Mathf.PI * 1.5f && Mathf.Abs(rawA) <= 720f;
            float aRad = deg ? rawA * Mathf.Deg2Rad : rawA;

            var raw = new Vector2(input.MoveX, input.MoveY);
            inputDir = raw.sqrMagnitude > 1f ? raw.normalized : raw;
            wantsMove = inputDir.sqrMagnitude > 0.0001f;
            _wantsMoveLocal = wantsMove;

            // signe basé sur la direction réelle (cos de l’angle en radians)
            aimSignFromInput = (Mathf.Cos(aRad) >= 0f) ? 1 : -1;

            // ✅ FIX AUTORITÉ NETAimSign :
            // - si StateAuthority (host) => écriture directe
            // - sinon (client owner) => RPC vers StateAuthority (sans écrire la Networked prop)
            if (NetAimSign != aimSignFromInput)
            {
                if (Object.HasStateAuthority)
                    NetAimSign = aimSignFromInput;
                else if (Object.HasInputAuthority)
                    RPC_UpdateAimSign(aimSignFromInput);
            }
        }
        else
        {
            // important pour l’anim locale : pas d’input => on veut pas bouger
            if (Object.HasInputAuthority) _wantsMoveLocal = false;
        }

        // --- vitesse courante (avec bonus FURTIF) ---
        float curSpeed = moveSpeed * speedMultiplier;

        Vector2 desiredVel = inputDir * curSpeed;
        float step = (wantsMove ? acceleration : deceleration) * Runner.DeltaTime;
        Vector2 newVel = Vector2.MoveTowards(rb.linearVelocity, desiredVel, step);

        // Clamp à la vitesse max courante
        if (newVel.sqrMagnitude > curSpeed * curSpeed)
            newVel = newVel.normalized * curSpeed;

        rb.linearVelocity = newVel;

        if (Object.HasStateAuthority)
        {
            NetSpeed = newVel.magnitude;

            // Mémorise la direction de DÉPLACEMENT pour le flip Move
            if      (inputDir.x > 0.01f)  lastFacingX = 1;
            else if (inputDir.x < -0.01f) lastFacingX = -1;
            NetFacingX = lastFacingX;

            if (NetAimSign == 0) NetAimSign = 1;

            // intention réseau (clé anti-reset et logique d’anim)
            NetWantsMove = wantsMove;
        }
        else
        {
            if (NetFacingX != 0) lastFacingX = NetFacingX;
        }
    }

    public override void Render()
    {
        float speedMag = rb ? rb.linearVelocity.magnitude : 0f;

        // Intention: locale si input authority, sinon réseau
        bool wantsMove = Object.HasInputAuthority ? _wantsMoveLocal : NetWantsMove;

        if (animator)
        {
            float rawSpeed = Object.HasInputAuthority ? speedMag : NetSpeed;

            // Anti-bruit: très petites vitesses sont traitées comme 0.
            if (rawSpeed < zeroSpeedEpsilon) rawSpeed = 0f;

            if (!wantsMove)
            {
                // → arrêt net: pas de damping, on claque à 0
                if (animDampOnStop <= 0f)
                    animator.SetFloat(speedFloatParam, 0f);
                else
                    animator.SetFloat(speedFloatParam, 0f, animDampOnStop, Time.deltaTime);
            }
            else
            {
                // → on veut bouger: si bloqué (rawSpeed==0), garde une petite vitesse d’anim
                float targetAnimSpeed = (rawSpeed > 0f) ? rawSpeed : minWalkAnimSpeed;

                // lissage seulement en mouvement (jamais à l’arrêt)
                animator.SetFloat(speedFloatParam, targetAnimSpeed, animDampOnMove, Time.deltaTime);
            }

            // moveX reste basé sur le dernier facing de déplacement (stable)
            int face = (NetFacingX == 0 ? lastFacingX : NetFacingX);
            animator.SetFloat(moveXFloatParam, face);

            ApplyArmedToAnimator(Object.HasInputAuthority ? _isArmed : NetArmed);
        }

        ApplyVisualFlip(GetFlipSign());
    }

    private int GetFlipSign()
    {
        if (flipBy == FlipSource.Move)
        {
            int moveSign = (NetFacingX != 0 ? NetFacingX : (lastFacingX == 0 ? 1 : lastFacingX));
            return moveSign;
        }
        int aim = (NetAimSign != 0 ? NetAimSign : 1);
        if (aim == 0) aim = 1;
        return aim;
    }

    public void SetArmed(bool armed)
    {
        _isArmed = armed;
        ApplyArmedToAnimator(armed);

        if (Object.HasStateAuthority) NetArmed = armed;
        else                          RPC_RequestSetArmed(armed);
    }

    // ====== BONUS FURTIF : API publique ======
    public void ApplySpeedMultiplier(float mult) => speedMultiplier *= Mathf.Max(0.01f, mult);
    public void ResetSpeedMultiplier()           => speedMultiplier = 1f;

    // ✅ API stable “SET” pour les classes (évite cumuls)
    public void SetSpeedMultiplier(float mult)   => speedMultiplier = Mathf.Clamp(mult, 0.05f, 3f);

    public float GetCurrentSpeed()               => moveSpeed * speedMultiplier;

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestSetArmed(bool armed) => NetArmed = armed;

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_UpdateAimSign(int aimSign)
    {
        NetAimSign = (aimSign >= 0) ? 1 : -1;
    }

    private void ApplyArmedToAnimator(bool armed)
    {
        TrySetBool(animator, armedBoolParam, armed);
        TrySetBool(animator, armedBoolAlt,   armed);
    }

    /// Flip visuel : si VisualRoot est assigné, on n’utilise PAS bodyRenderer.flipX
    private void ApplyVisualFlip(int sign)
    {
        int baseFacing = Mathf.Clamp(spriteDefaultFacing, -1, 1);
        if (baseFacing == 0) baseFacing = 1;

        int final = Mathf.Clamp(sign, -1, 1) * baseFacing;
        if (final == 0) final = 1;

        if (visualRoot)
        {
            Vector3 s = visualRoot.localScale;
            float abs = Mathf.Abs(s.x) > 0.0001f ? Mathf.Abs(s.x) : 1f;
            s.x = abs * (final > 0 ? 1f : -1f);
            visualRoot.localScale = s;

            if (bodyRenderer) bodyRenderer.flipX = false; // évite le double-flip
        }
        else if (bodyRenderer)
        {
            bodyRenderer.flipX = (final < 0);
        }
    }

    private static bool TrySetBool(Animator anim, string name, bool value)
    {
        if (!anim || string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < anim.parameterCount; i++)
        {
            var p = anim.GetParameter(i);
            if (p.name == name && p.type == AnimatorControllerParameterType.Bool)
            {
                anim.SetBool(name, value);
                return true;
            }
        }
        return false;
    }
}
