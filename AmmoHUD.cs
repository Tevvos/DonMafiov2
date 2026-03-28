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

    private void Update()
    {
        _t += Time.deltaTime;
        if (_t < refreshRate) return;
        _t = 0f;

        if (_localPW == null)
        {
            _localPW = FindLocalPlayerWeapon();
            if (_localPW == null) return;
        }

        UpdateUI(_localPW);
    }

    private PlayerWeapon FindLocalPlayerWeapon()
    {
        // Cherche tous les PlayerWeapon et prend celui du joueur local
        var all = FindObjectsOfType<PlayerWeapon>(true);
        foreach (var pw in all)
        {
            var no = pw.GetComponentInParent<Fusion.NetworkObject>();
            if (no != null && no.HasInputAuthority) // joueur local
                return pw;
        }
        return null;
    }

    private void UpdateUI(PlayerWeapon pw)
    {
        // Essaie de lire les valeurs depuis l'arme équipée
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
            reserveText.text = pistol.ServerGetReserve().ToString(); // si c'est accessible côté client, sinon on change plus bas
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
