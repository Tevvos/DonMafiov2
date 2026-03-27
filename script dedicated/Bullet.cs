using UnityEngine;
using Fusion;

/// <summary>
/// Projectile Fusion côté serveur : seul le StateAuthority gère les impacts et la vie.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Bullet : NetworkBehaviour
{
    [Header("Parameters")]
    [SerializeField] private float speed    = 30f;
    [SerializeField] private float damage   = 20f;
    [SerializeField] private float lifetime = 2f;

    public void SetDamage(float d) { damage = d; }

    [Header("Visual Orientation")]
    [Tooltip("0 si le sprite regarde +X, 90 s'il regarde +Y")]
    [SerializeField] private float spriteAngleOffset = 0f;

    [Header("Audio (optionnel)")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] bounceSounds;

    private Vector2 _dir = Vector2.right;
    private float _lifeTimer;
    private GameObject shooterGO;
    private NetworkObject _ownerNO;   // ✅ NetworkObject du tireur
    private PlayerRef _ownerRef;      // ✅ Référence du tireur

    public void Init(Vector2 dir, GameObject shooter = null)
    {
        shooterGO = shooter;
        _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;

        // 🔑 Récupère et stocke le NetworkObject du tireur
        if (shooter)
        {
            _ownerNO = shooter.GetComponent<NetworkObject>();
            if (_ownerNO == null)
                _ownerNO = shooter.GetComponentInParent<NetworkObject>();
            _ownerRef = _ownerNO ? _ownerNO.InputAuthority : default;
        }

        float a = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg + spriteAngleOffset;
        transform.rotation = Quaternion.Euler(0, 0, a);
    }

    public override void Spawned()
    {
        _lifeTimer = 0f;

        var bc2 = GetComponent<Collider2D>();
        if (bc2) bc2.isTrigger = true;

        var rb2d = GetComponent<Rigidbody2D>();
        if (!rb2d)
        {
            rb2d = gameObject.AddComponent<Rigidbody2D>();
            rb2d.isKinematic = true;
            rb2d.gravityScale = 0f;
            rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        // Ignore collisions avec le tireur
        var bc = GetComponent<Collider2D>();
        if (shooterGO && bc)
        {
            var cols = shooterGO.GetComponentsInChildren<Collider2D>(true);
            foreach (var sc in cols)
                if (sc) Physics2D.IgnoreCollision(bc, sc);
        }
    }

    public override void FixedUpdateNetwork()
    {
        _lifeTimer += Runner.DeltaTime;
        if (_lifeTimer >= lifetime)
        {
            if (Object && Object.HasStateAuthority) Runner.Despawn(Object);
            return;
        }

        if (Object && Object.HasStateAuthority)
        {
            transform.position += (Vector3)(_dir * speed * Runner.DeltaTime);
        }
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        // ✅ Uniquement côté StateAuthority : applique les dégâts et despawn
        if (!Object || !Object.HasStateAuthority) return;
        if (col.gameObject == shooterGO) return;

        var hp = col.GetComponent<PlayerHealth>() ?? col.GetComponentInParent<PlayerHealth>();
        if (hp != null)
        {
            // 🔥 Passe le NetworkObject du tireur à PlayerHealth
            if (_ownerNO != null)
                hp.TakeDamage(damage, _ownerNO);
            else
                hp.TakeDamage(damage, shooterGO); // fallback si jamais manquant

            if (Object) Runner.Despawn(Object);
            return;
        }

        TryPlayBounce();
        if (Object) Runner.Despawn(Object);
    }

    private void TryPlayBounce()
    {
        if (!audioSource || bounceSounds == null || bounceSounds.Length == 0) return;
        int i = Random.Range(0, bounceSounds.Length);
        audioSource.PlayOneShot(bounceSounds[i]);
    }
}
