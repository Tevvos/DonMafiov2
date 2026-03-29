using UnityEngine;
using TMPro;

public class AmmoHUD : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text magText;
    [SerializeField] private TMP_Text reserveText;

    [Header("Refresh")]
    [SerializeField] private float refreshRate = 0.05f; // 20 FPS UI

    private float _t;
    private PlayerWeapon _localPW;

    // FIX PERF : évite de lancer FindObjectsOfType à chaque tick quand aucun joueur n'est trouvé.
    // On ne cherche pas plus d'une fois par seconde.
    private float _findCooldown = 0f;
    private const float FIND_RETRY_INTERVAL = 1f;

    private void Update()
    {
        _t += Time.deltaTime;
        if (_t < refreshRate) return;
        _t = 0f;

        if (_localPW == null)
        {
            // FIX : cooldown sur la recherche pour ne pas appeler FindObjectsOfType 20x/s
            _findCooldown -= refreshRate;
            if (_findCooldown > 0f) return;

            _findCooldown = FIND_RETRY_INTERVAL;
            _localPW = FindLocalPlayerWeapon();

            if (_localPW == null)
            {
                // Pas encore trouvé, on affiche "--" et on attend le prochain retry
                if (magText)     magText.text     = "--";
                if (reserveText) reserveText.text = "--";
                return;
            }
        }

        UpdateUI(_localPW);
    }

    private PlayerWeapon FindLocalPlayerWeapon()
    {
        var all = FindObjectsOfType<PlayerWeapon>(true);
        foreach (var pw in all)
        {
            var no = pw.GetComponentInParent<Fusion.NetworkObject>();
            if (no != null && no.HasInputAuthority)
                return pw;
        }
        return null;
    }

    private void UpdateUI(PlayerWeapon pw)
    {
        var weaponGO = pw.GetCurrentWeapon();
        if (weaponGO == null)
        {
            magText.text = "--";
            reserveText.text = "--";
            return;
        }

        // Pistol
        var pistol = weaponGO.GetComponentInChildren<Pistol>(true);
        if (pistol != null)
        {
            magText.text = pistol.AmmoInMag.ToString();
            reserveText.text = pistol.ServerGetReserve().ToString();
            return;
        }

        // Thompson
        var th = weaponGO.GetComponentInChildren<Thompson>(true);
        if (th != null)
        {
            magText.text = th.AmmoInMag.ToString();
            reserveText.text = th.ServerGetReserve().ToString();
            return;
        }

        // Shotgun
        var sg = weaponGO.GetComponentInChildren<Shotgun>(true);
        if (sg != null)
        {
            magText.text = sg.AmmoInMag.ToString();
            reserveText.text = sg.ServerGetReserve().ToString();
            return;
        }

        magText.text = "--";
        reserveText.text = "--";
    }
}
