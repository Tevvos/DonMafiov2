using UnityEngine;
using Fusion;
using UnityEngine.Rendering.PostProcessing;
using TMPro;

public class PlayerHealth : NetworkBehaviour
{
    [Header("💖 Santé")]
    [SerializeField] private float maxHealth = 100f;
    [Networked] private float CurrentHealth { get; set; }
    [Networked] private NetworkBool NetIsDead { get; set; }
    [Networked] private int NetRemainingLives { get; set; }
    [Networked] private NetworkBool NetFinalDeathNotified { get; set; }

    [Header("📍 Références (auto)")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerWeapon playerWeapon;
    [SerializeField] private PlayerMovement_FusionPro playerMovement;
    [SerializeField] private PostProcessVolume postProcessVolume;
    [SerializeField] private TextMeshProUGUI deathMessageText;

    [Header("🧠 UI Santé (Screen)")]
    [SerializeField] private HealthBarUI screenHealthBar;

    [Header("🔊 Sons")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip deathSound;

    // --- FIX SÉCURITÉ : damage max par coup (anti-triche) ---
    private const float MAX_DAMAGE_PER_HIT = 200f;

    private Rigidbody2D[] allRB2D;
    private Collider2D[] allCols;

    // FIX : flag pour éviter de re-planifier un respawn si déjà en cours
    private bool _respawnScheduled = false;

    private static readonly int HASH_IsDead = Animator.StringToHash("IsDead");
    private static readonly int HASH_DeadTr = Animator.StringToHash("Dead");
    private static readonly int HASH_Speed  = Animator.StringToHash("Speed");
    private static readonly int HASH_MoveX  = Animator.StringToHash("MoveX");

    public override void Spawned()
    {
        if (!animator)       animator        = GetComponentInChildren<Animator>(true);
        if (!playerWeapon)   playerWeapon    = GetComponent<PlayerWeapon>();
        if (!playerMovement) playerMovement  = GetComponent<PlayerMovement_FusionPro>();
        if (!audioSource)    audioSource     = GetComponent<AudioSource>();

        allRB2D = GetComponentsInChildren<Rigidbody2D>(true);
        allCols = GetComponentsInChildren<Collider2D>(true);

        if (deathMessageText) deathMessageText.enabled = false;

        if (Object.HasStateAuthority)
        {
            CurrentHealth         = maxHealth;
            NetRemainingLives     = 1;
            NetIsDead             = false;
            NetFinalDeathNotified = false;
        }

        _respawnScheduled = false;

        SafeSetBool(HASH_IsDead, NetIsDead);

        if (Object.HasInputAuthority)
        {
            if (screenHealthBar == null)
                screenHealthBar = FindObjectOfType<HealthBarUI>(true);
            UpdateLocalHealthUI(CurrentHealth, maxHealth);
        }

        Debug.Log($"[HP] Spawned {Object.InputAuthority} | StateAuth={Object.HasStateAuthority} | Health={CurrentHealth}/{maxHealth} | Lives={NetRemainingLives}");
    }

    // ======== DÉGÂTS (serveur) ========
    public void ServerApplyDamage(float amount, PlayerRef killerRef = default, string reason = "ServerApplyDamage")
    {
        if (!Object.HasStateAuthority || amount <= 0f || NetIsDead) return;

        // FIX SÉCURITÉ : clamp le damage pour éviter les exploits réseau
        amount = Mathf.Clamp(amount, 0f, MAX_DAMAGE_PER_HIT);

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        Debug.Log($"[HP] Damage {amount} by={killerRef} -> {CurrentHealth}/{maxHealth} (reason={reason})");

        RPC_UpdateHealthUI(CurrentHealth, maxHealth);
        RPC_OnHitFeedback();
        RPC_SpawnBloodFxForAll(-transform.right);

        if (CurrentHealth > 0f) return;

        NetIsDead = true;

        if (killerRef != default && !killerRef.IsNone && (Object == null || killerRef != Object.InputAuthority))
        {
            GameSceneRankingHub.ReportKill(killerRef, Object ? Object.InputAuthority : PlayerRef.None);
        }

        RPC_PlayDeathAnim();
        ServerLockAndHandleRespawnOrFinal();
        RPC_ClientOnDeath();
        RPC_RespawnVisualsAll(false);
    }

    // FIX SÉCURITÉ : clamp le damage dans le RPC pour bloquer les valeurs malveillantes
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ApplyDamage(float amount, PlayerRef killerRef)
    {
        // Validation supplémentaire côté StateAuthority
        if (amount <= 0f || amount > MAX_DAMAGE_PER_HIT) return;
        ServerApplyDamage(amount, killerRef, "RPC_ApplyDamage");
    }

    public void TakeDamage(float amount, NetworkObject attackerNO)
    {
        PlayerRef killerRef = attackerNO ? attackerNO.InputAuthority : default;
        RPC_ApplyDamage(amount, killerRef);
    }

    public void TakeDamage(float amount, GameObject attacker)
    {
        PlayerRef killerRef = default;
        if (attacker)
        {
            NetworkObject no = attacker.GetComponent<NetworkObject>() ?? attacker.GetComponentInParent<NetworkObject>();
            if (no) killerRef = no.InputAuthority;
        }
        RPC_ApplyDamage(amount, killerRef);
    }

    public void TakeDamage(float amount) => RPC_ApplyDamage(amount, default);

    // ======== FEEDBACK / UI ========
    [Rpc(RpcSources.StateAuthority | RpcSources.InputAuthority, RpcTargets.InputAuthority)]
    private void RPC_OnHitFeedback()
    {
        if (audioSource && hitSound) audioSource.PlayOneShot(hitSound);

        playerWeapon?.SpawnBloodEffect(-transform.right);

        if (Object != null && Object.HasInputAuthority)
        {
            var camFollow = FindObjectOfType<CameraFollowFusion>();
            if (camFollow != null) camFollow.KickCamera(0.9f);

            if (DamageOverlayUI.Instance != null)
                DamageOverlayUI.Instance.Pulse(1f);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_UpdateHealthUI(float current, float max) => UpdateLocalHealthUI(current, max);

    private void UpdateLocalHealthUI(float current, float max)
    {
        if (screenHealthBar == null) return;
        screenHealthBar.SetHealth(current, max);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_ClientOnDeath()
    {
        if (deathMessageText) { deathMessageText.enabled = true; deathMessageText.text = "Died"; }
        if (audioSource && deathSound) audioSource.PlayOneShot(deathSound);

        SafeSetBool(HASH_IsDead, true);
        SafeSetTrigger(HASH_DeadTr);
        if (playerMovement) playerMovement.enabled = false;

        foreach (var rb in allRB2D) if (rb) { rb.linearVelocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Static; }
        foreach (var c in allCols)  if (c)  c.enabled = false;

        if (postProcessVolume) postProcessVolume.enabled = true;
        var cam = FindObjectOfType<CameraFollowFusion>(); if (cam) cam.SetZoom(true);

        Debug.Log($"[HP] ClientOnDeath for {Object.InputAuthority}");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_ClientOnRespawn()
    {
        foreach (var c in allCols)  if (c)  c.enabled = true;
        foreach (var rb in allRB2D) if (rb) { rb.bodyType = RigidbodyType2D.Dynamic; rb.linearVelocity = Vector2.zero; }

        if (playerMovement) playerMovement.enabled = true;

        SafeSetBool(HASH_IsDead, false);
        if (animator)
        {
            animator.ResetTrigger(HASH_DeadTr);
            animator.SetFloat(HASH_Speed, 0f);
            animator.SetFloat(HASH_MoveX, 1f);
        }

        if (deathMessageText) deathMessageText.enabled = false;
        if (postProcessVolume) postProcessVolume.enabled = false;

        var cam = FindObjectOfType<CameraFollowFusion>();
        if (cam) { cam.SetZoom(false); cam.ClearKick(true); }

        var vis = GetComponent<ClientLocalVisual>();
        if (vis) vis.SnapToTransform();

        UpdateLocalHealthUI(CurrentHealth, maxHealth);
        Debug.Log($"[HP] ClientOnRespawn for {Object.InputAuthority} -> {CurrentHealth}/{maxHealth} (Lives={NetRemainingLives})");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_RespawnVisualsAll(bool alive)
    {
        SafeSetBool(HASH_IsDead, !alive);
        if (alive && animator) animator.ResetTrigger(HASH_DeadTr);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_PlayDeathAnim() { SafeSetTrigger(HASH_DeadTr); }

    private void ServerLockAndHandleRespawnOrFinal()
    {
        foreach (var rb in allRB2D) if (rb) { rb.linearVelocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Static; }
        foreach (var c in allCols)  if (c)  c.enabled = false;

        if (playerMovement) playerMovement.enabled = false;
        playerWeapon?.UnequipWeapon();
        SafeSetBool(HASH_IsDead, true);

        bool hasLifeBefore = NetRemainingLives > 0;
        Debug.Log($"[HP] Death server. LivesBefore={NetRemainingLives} (hasLifeBefore={hasLifeBefore}) for {Object.InputAuthority}");

        if (hasLifeBefore)
        {
            NetRemainingLives--;

            // FIX : ne planifier le respawn qu'une seule fois
            if (!_respawnScheduled)
            {
                _respawnScheduled = true;
                Invoke(nameof(RespawnServer), 6f);
            }
        }
        else
        {
            if (!NetFinalDeathNotified)
            {
                NetFinalDeathNotified = true;
                if (Object != null && Object.HasStateAuthority)
                {
                    Debug.Log($"[HP] FINAL DEATH -> NotifyFinalDeath({Object.InputAuthority})");
                    GameRules_Victory_Fusion.NotifyFinalDeath(Object.InputAuthority);
                }
                else
                {
                    Debug.LogError("[HP] Final death MAIS pas StateAuthority -> pas de NotifyFinalDeath.");
                }
            }

            if (playerMovement) playerMovement.enabled = false;
            foreach (var rb in allRB2D) if (rb) rb.bodyType = RigidbodyType2D.Static;
            if (playerWeapon) playerWeapon.enabled = false;
        }
    }

    private void RespawnServer()
    {
        // FIX : vérifications défensives avant d'accéder à l'objet réseau
        if (!this || !gameObject || !gameObject.activeInHierarchy) return;
        if (!Object || !Object.HasStateAuthority) return;

        _respawnScheduled = false;

        CurrentHealth = maxHealth;
        NetIsDead = false;

        foreach (var c in allCols)  if (c)  c.enabled = true;
        foreach (var rb in allRB2D) if (rb) { rb.bodyType = RigidbodyType2D.Dynamic; rb.linearVelocity = Vector2.zero; }

        if (playerMovement) playerMovement.enabled = true;

        if (GameManager_Fusion.Instance != null)
            transform.position = GameManager_Fusion.Instance.GetRandomSpawnPosition();

        RPC_UpdateHealthUI(CurrentHealth, maxHealth);
        RPC_RespawnVisualsAll(true);
        RPC_ClientOnRespawn();
        Debug.Log($"[HP] RespawnServer -> {CurrentHealth}/{maxHealth} LivesLeft={NetRemainingLives}");
    }

    private void SafeSetBool(int hash, bool v) { if (animator) animator.SetBool(hash, v); }
    private void SafeSetTrigger(int hash) { if (animator) animator.SetTrigger(hash); }

    public bool IsDead() => NetIsDead;
    public float GetCurrentHealthValue() => CurrentHealth;
    public int GetRemainingLivesValue() => NetRemainingLives;

    // ======== Lobby reset ========
    public void ResetForLobby()
    {
        // FIX : annule le respawn planifié si on retourne au lobby avant qu'il se déclenche
        if (_respawnScheduled)
        {
            CancelInvoke(nameof(RespawnServer));
            _respawnScheduled = false;
        }

        if (Object != null && Object.HasStateAuthority)
            DoResetForLobby_Server();
        else
            RPC_AskResetForLobby();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_AskResetForLobby() => DoResetForLobby_Server();

    private void DoResetForLobby_Server()
    {
        // FIX : annule aussi le respawn planifié côté serveur
        if (_respawnScheduled)
        {
            CancelInvoke(nameof(RespawnServer));
            _respawnScheduled = false;
        }

        CurrentHealth         = maxHealth;
        NetIsDead             = false;
        NetRemainingLives     = 1;
        NetFinalDeathNotified = false;

        foreach (var c in allCols)  if (c)  c.enabled = true;
        foreach (var rb in allRB2D) if (rb) { rb.bodyType = RigidbodyType2D.Dynamic; rb.linearVelocity = Vector2.zero; }
        if (playerMovement) playerMovement.enabled = true;

        if (playerWeapon)
        {
            if (!playerWeapon.enabled) playerWeapon.enabled = true;
            playerWeapon.UnequipWeapon(drop: false);
        }

        RPC_UpdateHealthUI(CurrentHealth, maxHealth);
        RPC_RespawnVisualsAll(true);
        RPC_ClientOnRespawn();

        Debug.Log("[HP] DoResetForLobby_Server done.");
    }

    // --- BONUS CLASSE : MASTODONTE ---
    public void ApplyMaxHPBonus(int bonus)
    {
        maxHealth += bonus;
        CurrentHealth = Mathf.Clamp(CurrentHealth + bonus, 0, maxHealth);
        RPC_UpdateHealthUI(CurrentHealth, maxHealth);
        Debug.Log($"[HP] ApplyMaxHPBonus +{bonus} -> {CurrentHealth}/{maxHealth}");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnBloodFxForAll(Vector2 dir)
    {
        if (!playerWeapon)
            playerWeapon = GetComponent<PlayerWeapon>();

        if (!playerWeapon) return;

        playerWeapon.SpawnBloodEffect(dir);
    }
}
