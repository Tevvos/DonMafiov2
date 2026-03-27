using System.Reflection;
using Fusion;
using UnityEngine;

public class PlayerClassController : NetworkBehaviour
{
    [Header("Refs (optionnel)")]
    [SerializeField] private Animator animator;

    // === Réglages d’équilibrage (éditables Inspector) ===
    [Header("Mastodonte")]
    [SerializeField] private int mastodonteTargetMaxHP = 150;

    [Header("Dog of War (server-only)")]
    [Tooltip("Nom de l'arme à équiper au start (ServerEquipWeaponForAll).")]
    [SerializeField] private string dogOfWarDefaultWeapon = "Pistol";
    [Tooltip("Mun. bonus à ajouter en réserve (Pistol, Shotgun, Thompson).")]
    [SerializeField] private int dogOfWarAmmoPistol   = 60;
    [SerializeField] private int dogOfWarAmmoShotgun  = 24;
    [SerializeField] private int dogOfWarAmmoThompson = 120;

    [Header("Furtif")]
    [Tooltip("Multiplicateur si une arme est équipée.")]
    [SerializeField] private float furtifSpeedMultiplierArmed = 1.15f;
    [Tooltip("Multiplicateur si aucune arme n’est équipée.")]
    [SerializeField] private float furtifSpeedMultiplierUnarmed = 1.00f;

    // === État réseau ===
    [Networked] public PlayerClassType ClassType { get; private set; } = PlayerClassType.None;
    [Networked] private NetworkBool _appliedOnce { get; set; }

    // === Caches ===
    private PlayerHealth _playerHealth;
    private PlayerWeapon _playerWeapon;
    private Component _movementAny; // pour SetSpeedMultiplier / propriétés via réflexion

    // === Internes ===
    private bool _furtifTickEnabled = false;
    private float _lastAppliedSpeedMult = -1f;

    public override void Spawned()
    {
        CacheRefs();
        _appliedOnce = false; // au cas d’un respawn/lobby→jeu
    }

    private void CacheRefs()
    {
        _playerHealth = GetComponentInChildren<PlayerHealth>(true);
        _playerWeapon = GetComponentInChildren<PlayerWeapon>(true);

        _movementAny =
            FindComponentByNameInChildren(gameObject, "PlayerMovement_FusionPro") ??
            FindComponentByNameInChildren(gameObject, "PlayerMovement") ??
            FindComponentByNameInChildren(gameObject, "PlayerMovementFusion") ??
            FindComponentByNameInChildren(gameObject, "PlayerMovement2D");
    }

    public override void FixedUpdateNetwork()
    {
        // 1) Obtenir la classe depuis MatchFlow (source unique, serveur → replicated)
        if (ClassType == PlayerClassType.None && MatchFlow_Fusion.HasInstance && Object)
        {
            var resolved = MatchFlow_Fusion.GetClassFor(Object.InputAuthority);
            if (resolved != PlayerClassType.None)
                ClassType = resolved;
        }

        // 2) Appliquer la classe une seule fois quand le match démarre + classe connue
        if (!_appliedOnce
            && MatchFlow_Fusion.HasInstance
            && MatchFlow_Fusion.Instance.MatchStarted
            && ClassType != PlayerClassType.None)
        {
            ApplyClassLocal(ClassType);
            _appliedOnce = true;
        }

        // 3) Tick Furtif : ajuste vitesse si état armé change
        if (_furtifTickEnabled && ClassType == PlayerClassType.Furtif)
        {
            bool armed = IsWeaponEquipped();
            float target = armed ? furtifSpeedMultiplierArmed : furtifSpeedMultiplierUnarmed;
            if (!Mathf.Approximately(target, _lastAppliedSpeedMult))
            {
                TrySetSpeedMultiplier(target);
                _lastAppliedSpeedMult = target;
            }
        }
    }

    // ===== Application locale =====
    private void ApplyClassLocal(PlayerClassType cls)
    {
        if (animator) animator.SetInteger("ClassId", (int)cls);

        switch (cls)
        {
            case PlayerClassType.Mastodonte:
                ApplyMastodonte_Idempotent();
                _furtifTickEnabled = false;
                break;

            case PlayerClassType.DogOfWar:
                ApplyDogOfWar_ServerOnly();
                _furtifTickEnabled = false;
                break;

            case PlayerClassType.Furtif:
                ApplyFurtif();
                _furtifTickEnabled = true;
                break;
        }
    }

    // --- Mastodonte : porte le max HP effectif à 150, sans doublon ---
    private void ApplyMastodonte_Idempotent()
    {
        if (_playerHealth == null)
        {
            Debug.LogWarning("[Class] Mastodonte: PlayerHealth introuvable.");
            return;
        }

        int currentEffectiveMax = TryGetEffectiveMaxHP(_playerHealth, fallbackBase: 100);
        int target = mastodonteTargetMaxHP;
        int delta = target - currentEffectiveMax;

        if (delta != 0)
        {
            var mi = _playerHealth.GetType().GetMethod("ApplyMaxHPBonus",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(int) }, null);

            if (mi != null)
            {
                mi.Invoke(_playerHealth, new object[] { delta });

                // Re-clamp si un autre bonus se greffe juste après
                int after = TryGetEffectiveMaxHP(_playerHealth, fallbackBase: currentEffectiveMax);
                int over = after - target;
                if (over > 0) mi.Invoke(_playerHealth, new object[] { -over });
            }
            else
            {
                Debug.LogWarning("[Class] Mastodonte: ApplyMaxHPBonus(int) introuvable.");
            }
        }
    }

    // --- Dog of War : serveur only (équipement + munitions) ---
    private void ApplyDogOfWar_ServerOnly()
    {
        if (!Object || !Object.HasStateAuthority) return; // serveur uniquement
        if (_playerWeapon == null)
        {
            Debug.LogWarning("[Class] DogOfWar: PlayerWeapon introuvable (server).");
            return;
        }

        if (!string.IsNullOrWhiteSpace(dogOfWarDefaultWeapon))
        {
            var equip = _playerWeapon.GetType().GetMethod(
                "ServerEquipWeaponForAll",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);

            if (equip != null) equip.Invoke(_playerWeapon, new object[] { dogOfWarDefaultWeapon });
            else Debug.LogWarning("[Class] DogOfWar: ServerEquipWeaponForAll(string) introuvable.");
        }

        var addAll = _playerWeapon.GetType().GetMethod(
            "AddReserveAmmoAll",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(int), typeof(int), typeof(int) }, null);

        if (addAll != null)
            addAll.Invoke(_playerWeapon, new object[] { dogOfWarAmmoPistol, dogOfWarAmmoShotgun, dogOfWarAmmoThompson });
        else
            Debug.LogWarning("[Class] DogOfWar: AddReserveAmmoAll(int,int,int) introuvable.");
    }

    // --- Furtif : applique un multiplicateur selon l’état armé, puis tick ---
    private void ApplyFurtif()
    {
        bool armed = IsWeaponEquipped();
        float multNow = armed ? furtifSpeedMultiplierArmed : furtifSpeedMultiplierUnarmed;

        if (!TrySetSpeedMultiplier(multNow))
            Debug.LogWarning("[Class] Furtif: impossible d’appliquer le speed multiplier.");

        _lastAppliedSpeedMult = multNow;
    }

    // ===== Helpers =====

    private static int TryGetEffectiveMaxHP(object playerHealth, int fallbackBase)
    {
        var t = playerHealth.GetType();

        var mEff = t.GetMethod("GetMaxHealth", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
               ?? t.GetMethod("GetEffectiveMaxHealth", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
               ?? t.GetMethod("GetTotalMaxHealth", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (mEff != null && mEff.ReturnType == typeof(int))
            return (int)mEff.Invoke(playerHealth, null);

        int byProp = ReadIntPropOrField(playerHealth, "EffectiveMaxHealth", "effectiveMaxHealth", "_effectiveMaxHealth", fallbackBase);
        if (byProp != fallbackBase) return byProp;

        int byTotal = ReadIntPropOrField(playerHealth, "TotalMaxHealth", "totalMaxHealth", "_totalMaxHealth", fallbackBase);
        if (byTotal != fallbackBase) return byTotal;

        return ReadIntPropOrField(playerHealth, "MaxHealth", "maxHealth", "_maxHealth", fallbackBase);
    }

    private bool IsWeaponEquipped()
    {
        if (_playerWeapon == null) return false;
        var t = _playerWeapon.GetType();

        bool? b =
            ReadBoolPropOrFieldNullable(_playerWeapon, "IsArmed", "isArmed", "_isArmed") ??
            ReadBoolPropOrFieldNullable(_playerWeapon, "HasWeapon", "hasWeapon", "_hasWeapon");

        if (b.HasValue) return b.Value;

        var pi = t.GetProperty("CurrentWeapon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null) return pi.GetValue(_playerWeapon) != null;

        var fi = t.GetField("currentWeapon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                 t.GetField("_currentWeapon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null) return fi.GetValue(_playerWeapon) != null;

        return false;
    }

    private bool TrySetSpeedMultiplier(float mult)
    {
        if (_movementAny == null) return false;
        var t = _movementAny.GetType();

        var mi = t.GetMethod("SetSpeedMultiplier", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(float) }, null);
        if (mi != null) { mi.Invoke(_movementAny, new object[] { mult }); return true; }

        if (WriteFloatPropOrField(_movementAny, mult, "SpeedMultiplier", "speedMultiplier", "_speedMultiplier")) return true;

        float baseSpeed = ReadFloatPropOrField(_movementAny, "BaseSpeed", "baseSpeed", "_baseSpeed", fallback: -1f);
        if (baseSpeed > 0f)
        {
            float newSpeed = baseSpeed * mult;
            if (WriteFloatPropOrField(_movementAny, newSpeed, "CurrentSpeed", "currentSpeed", "_currentSpeed")) return true;
        }
        return false;
    }

    // --- utils reflection génériques ---
    private static Component FindComponentByNameInChildren(GameObject root, string typeName)
    {
        var type = System.Type.GetType(typeName);
        if (type != null)
        {
            var c = root.GetComponentInChildren(type, true);
            if (c != null) return c;
        }

        var all = root.GetComponentsInChildren<Component>(true);
        foreach (var c in all)
        {
            if (c == null) continue;
            if (string.Equals(c.GetType().Name, typeName, System.StringComparison.Ordinal)) return c;
        }
        return null;
    }

    private static int ReadIntPropOrField(object target, string propName, string fieldName1, string fieldName2, int fallback)
    {
        var t = target.GetType();
        var pi = t.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null && pi.PropertyType == typeof(int))
            return (int)pi.GetValue(target);

        var fi = t.GetField(fieldName1, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                 t.GetField(fieldName2, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null && fi.FieldType == typeof(int))
            return (int)fi.GetValue(target);

        return fallback;
    }

    private static float ReadFloatPropOrField(object target, string propName, string fieldName1, string fieldName2, float fallback)
    {
        var t = target.GetType();
        var pi = t.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null && pi.PropertyType == typeof(float))
            return (float)pi.GetValue(target);

        var fi = t.GetField(fieldName1, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                 t.GetField(fieldName2, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null && fi.FieldType == typeof(float))
            return (float)fi.GetValue(target);

        return fallback;
    }

    private static bool WriteFloatPropOrField(object target, float value, string propName, string fieldName1, string fieldName2)
    {
        var t = target.GetType();
        var pi = t.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null && pi.CanWrite && pi.PropertyType == typeof(float))
        {
            pi.SetValue(target, value);
            return true;
        }

        var fi = t.GetField(fieldName1, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                 t.GetField(fieldName2, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null && fi.FieldType == typeof(float))
        {
            fi.SetValue(target, value);
            return true;
        }
        return false;
    }

    private static bool? ReadBoolPropOrFieldNullable(object target, string propName, string fieldName1, string fieldName2)
    {
        var t = target.GetType();
        var pi = t.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null && pi.PropertyType == typeof(bool))
            return (bool)pi.GetValue(target);

        var fi = t.GetField(fieldName1, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                 t.GetField(fieldName2, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null && fi.FieldType == typeof(bool))
            return (bool)fi.GetValue(target);

        return null;
    }

    // Reset round (retour lobby)
    public void ResetForLobby()
    {
        ClassType = PlayerClassType.None;
        _appliedOnce = false;
        _furtifTickEnabled = false;
        _lastAppliedSpeedMult = -1f;
        if (animator) animator.SetInteger("ClassId", 0);
        TrySetSpeedMultiplier(1f);
    }
}
