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
    private Rigidbody2D _rb;
    private NetworkObject _ownerNO;
    private PlayerRef _ownerRef;

    // ✅ Shared : on stocke le killerRef en Networked pour que tous les clients le voient
    [Networked] private PlayerRef NetKillerRef { get; set; }

    public void Init(Vector2 dir, GameObject shooter = null, NetworkObject ownerNO = null, PlayerRef ownerRef = default)
    {
        shooterGO = shooter;
        _dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;

        // Priorité aux paramètres explicites (passés depuis PlayerWeapon)
        if (ownerNO != null)
        {
            _ownerNO = ownerNO;
            _ownerRef = ownerRef != default ? ownerRef : ownerNO.InputAuthority;
        }
        else if (shooter)
        {
            _ownerNO = shooter.GetComponent<NetworkObject>() ?? shooter.GetComponentInParent<NetworkObject>();
            _ownerRef = _ownerNO ? _ownerNO.InputAuthority : default;
        }

        // Stocke dans Networked pour que le StateAuthority puisse l'utiliser
        if (Object && Object.HasStateAuthority)
            NetKillerRef = _ownerRef;

        float a = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg + spriteAngleOffset;
        transform.rotation = Quaternion.Euler(0, 0, a);

        // ✅ Appliquer la velocity dès Init (avant Spawned ou après)
        if (_rb == null) _rb = GetComponent<Rigidbody2D>();
        if (_rb != null) _rb.linearVelocity = _dir * speed;
    }

    public override void Spawned()
    {
        _lifeTimer = 0f;

        var bc2 = GetComponent<Collider2D>();
        if (bc2) bc2.isTrigger = true;

        _rb = GetComponent<Rigidbody2D>();
        if (!_rb) _rb = gameObject.AddComponent<Rigidbody2D>();

        // ✅ Dynamic : triggers fiables avec tous les types de RB
        // On désactive la sync réseau de la physique via NetworkRigidbody2D absent
        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.gravityScale = 0f;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        _rb.linearDamping = 0f;
        _rb.angularDamping = 0f;
        // La velocity sera appliquée dans Init() ou au premier FixedUpdateNetwork

        // ✅ Shared : si on est le StateAuthority mais pas l'initiateur,
        // on récupère le killerRef depuis la variable Networked
        if (_ownerRef == default && NetKillerRef != default)
            _ownerRef = NetKillerRef;

        // Ignore collisions avec le tireur
        var bc = GetComponent<Collider2D>();
        if (shooterGO && bc)
        {
            var cols = shooterGO.GetComponentsInChildren<Collider2D>(true);
            foreach (var sc in cols)
                if (sc) Physics2D.IgnoreCollision(bc, sc);
        }
    }

    // Interpolation visuelle entre les ticks réseau
    private Vector3 _renderPrevPos;
    private Vector3 _renderNextPos;

    [Header("Hit Detection")]
    [Tooltip("Rayon du cercle de détection (doit correspondre au collider de la balle)")]
    [SerializeField] private float hitRadius = 0.08f;
    [SerializeField] private LayerMask hitLayers = ~0; // toutes les layers par défaut

    private bool _hit = false; // anti double-hit

    public override void FixedUpdateNetwork()
    {
        if (!Object || !Object.HasStateAuthority) return;

        _lifeTimer += Runner.DeltaTime;
        if (_lifeTimer >= lifetime)
        {
            Runner.Despawn(Object);
            return;
        }

        // Déplacement direct via transform (SA uniquement, pas de sync physique)
        transform.position += (Vector3)(_dir * speed * Runner.DeltaTime);
        // Sync le RB pour les triggers
        if (_rb) _rb.position = transform.position;

        if (_hit) return;

        // ✅ Shared : OverlapCircle fiable entre tous les clients
        // Détecte les joueurs dans le rayon de la balle
        var hits = Physics2D.OverlapCircleAll(transform.position, hitRadius, hitLayers);
        foreach (var col in hits)
        {
            if (col == null) continue;
            if (col.gameObject == shooterGO) continue;

            // Ignore le tireur par InputAuthority
            var colNO = col.GetComponentInParent<NetworkObject>();
            var killerRef = _ownerRef != default ? _ownerRef : NetKillerRef;
            if (colNO != null && killerRef != default && colNO.InputAuthority == killerRef) continue;

            var hp = col.GetComponent<PlayerHealth>() ?? col.GetComponentInParent<PlayerHealth>();
            if (hp != null)
            {
                _hit = true;

                // Résout le killerNO
                NetworkObject killerNO = _ownerNO;
                if (killerNO == null && killerRef != default)
                    Runner.TryGetPlayerObject(killerRef, out killerNO);

                if (killerNO != null) hp.TakeDamage(damage, killerNO);
                else                  hp.TakeDamage(damage);

                Runner.Despawn(Object);
                return;
            }

            // Mur ou obstacle → bounce ou despawn
            if (col.GetComponentInParent<PlayerHealth>() == null)
            {
                _hit = true;
                TryPlayBounce();
                Runner.Despawn(Object);
                return;
            }
        }
    }

    public override void Render()
    {
        // ✅ Interpolation visuelle côté non-SA → balle fluide pour les autres clients
        if (!Object.HasStateAuthority && _rb)
        {
            // On extrapole la position depuis la dernière connue
            transform.position = Vector3.Lerp(transform.position,
                transform.position + (Vector3)(_dir * speed * Time.deltaTime),
                1f);
        }
    }

    // OnTriggerEnter2D gardé comme backup (fonctionne quand les layers sont bien configurées)
    private void OnTriggerEnter2D(Collider2D col)
    {
        if (!Object || !Object.HasStateAuthority || _hit) return;
        if (col.gameObject == shooterGO) return;

        var colNO = col.GetComponentInParent<NetworkObject>();
        var killerRef = _ownerRef != default ? _ownerRef : NetKillerRef;
        if (colNO != null && killerRef != default && colNO.InputAuthority == killerRef) return;

        var hp = col.GetComponent<PlayerHealth>() ?? col.GetComponentInParent<PlayerHealth>();
        if (hp != null)
        {
            _hit = true;
            NetworkObject killerNO = _ownerNO;
            if (killerNO == null && killerRef != default)
                Runner.TryGetPlayerObject(killerRef, out killerNO);

            if (killerNO != null) hp.TakeDamage(damage, killerNO);
            else                  hp.TakeDamage(damage);

            if (Object) Runner.Despawn(Object);
            return;
        }

        _hit = true;
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
