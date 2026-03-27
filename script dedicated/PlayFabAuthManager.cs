// PlayFabAuthManager.cs — Don Mafio (version simplifiée sans vérification email)
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Net.Mail;
using PlayFab;
using PlayFab.ClientModels;

public class PlayFabAuthManager : MonoBehaviour
{
    [Header("PlayFab")]
    [SerializeField] private string playFabTitleId = "1DA3D3";

    [Header("UI - Auth")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Toggle rememberMeToggle;
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Auto-Login")]
    [SerializeField] private bool autoLoginOnStart = true;
    [SerializeField, Min(1)] private int autologinRetries = 3;
    [SerializeField, Min(0.1f)] private float autologinInitialDelay = 0.8f;
    [SerializeField, Min(0f)] private float autologinMaxJitter = 0.35f;
    [SerializeField, Min(1f)] private float requestTimeoutSeconds = 8f;
    [SerializeField] private bool sharedDeviceMode = false;

    [Header("Cosmetic feedback")]
    [SerializeField] private Animator gunAnimator;
    [SerializeField] private string animationTriggerName = "Play";
    [SerializeField] private AudioSource gunAudioSource;

    [Header("Settings (optional)")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private TMP_Dropdown languageDropdown;
    [SerializeField] private Toggle logoToggle;
    [SerializeField] private GameObject logoObject;
    [SerializeField] private Toggle musicToggle;
    [SerializeField] private AudioSource musicAudio;

    private bool isLoggedIn = false;
    private bool hasTriedManualAction = false;
    private string currentCid = null;

    private const string KEY_CUSTOM_ID = "DM_CustomID";
    private const string KEY_LANG = "DM_Language";
    private const string KEY_LOGO = "DM_LogoOn";
    private const string KEY_MUSIC = "DM_MusicOn";

    public event Action OnLoggedIn;
    public event Action OnLoggedOut;

    #region SecurePrefs (simple XOR)
    private static class SecurePrefs
    {
        private static readonly byte[] Salt = System.Text.Encoding.UTF8.GetBytes("DonMafio#LocalSalt@2025");
        public static void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) { PlayerPrefs.DeleteKey(key); return; }
            var b = System.Text.Encoding.UTF8.GetBytes(value);
            var x = Xor(b, Salt);
            PlayerPrefs.SetString(key, Convert.ToBase64String(x));
        }
        public static string GetString(string key)
        {
            var s = PlayerPrefs.GetString(key, null);
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                var b = Convert.FromBase64String(s);
                var x = Xor(b, Salt);
                return System.Text.Encoding.UTF8.GetString(x);
            }
            catch { return null; }
        }
        private static byte[] Xor(byte[] data, byte[] salt)
        {
            var r = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) r[i] = (byte)(data[i] ^ salt[i % salt.Length]);
            return r;
        }
    }
    #endregion

    private void Awake()
    {
        if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId))
            PlayFabSettings.staticSettings.TitleId = playFabTitleId;

        if (playButton) playButton.gameObject.SetActive(false);
        if (settingsPanel) settingsPanel.SetActive(false);

        if (logoObject) logoObject.SetActive(PlayerPrefs.GetInt(KEY_LOGO, 1) == 1);
        if (musicAudio) musicAudio.mute = PlayerPrefs.GetInt(KEY_MUSIC, 1) == 0;

        currentCid = SecurePrefs.GetString(KEY_CUSTOM_ID);
    }

    private void Start()
    {
        if (autoLoginOnStart && !sharedDeviceMode)
            TryAutoLoginCustomID();
        else
            ShowLoginUI();
    }

    private void Update()
    {
        if (settingsPanel && settingsPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            ToggleSettings(false);
    }

    private void ShowLoginUI()
    {
        if (loginPanel) loginPanel.SetActive(true);
        if (playButton) playButton.gameObject.SetActive(false);
        WriteStatus("Connecte-toi ou crée un compte.");
    }

    #region Auto-Login CustomID
    private void TryAutoLoginCustomID()
    {
        if (string.IsNullOrEmpty(currentCid))
        {
            currentCid = Guid.NewGuid().ToString("N");
            SecurePrefs.SetString(KEY_CUSTOM_ID, currentCid);
            PlayerPrefs.Save();
        }
        StartCoroutine(Co_AutoLogin());
    }

    private IEnumerator Co_AutoLogin()
    {
        float delay = autologinInitialDelay;
        for (int i = 0; i < autologinRetries; i++)
        {
            WriteStatus("Connexion automatique...");
            var req = new LoginWithCustomIDRequest { CreateAccount = true, CustomId = currentCid };
            bool done = false;

            PlayFabClientAPI.LoginWithCustomID(req, res =>
            {
                OnLoginSuccessCommon(res, fromAuto: true);
                done = true;
            },
            err =>
            {
                Debug.LogWarning("[AutoLogin] " + err.GenerateErrorReport());
                done = true;
            });

            float jitter = UnityEngine.Random.Range(0f, autologinMaxJitter);
            float t = 0f;
            while (!done && t < requestTimeoutSeconds) { t += Time.deltaTime; yield return null; }
            if (isLoggedIn) yield break;

            yield return new WaitForSeconds(delay + jitter);
            delay *= 1.6f;
        }
        ShowLoginUI();
    }
    #endregion

    #region Register / Login / Logout
    public void Register()
    {
        hasTriedManualAction = true;

        if (!IsValidEmail(emailInput?.text)) { WriteStatus("Adresse email invalide."); return; }
        if (string.IsNullOrEmpty(passwordInput?.text)) { WriteStatus("Mot de passe manquant."); return; }

        if (playButton) playButton.gameObject.SetActive(false);
        WriteStatus("Inscription en cours...");

        var request = new RegisterPlayFabUserRequest
        {
            Email = emailInput.text,
            Password = passwordInput.text,
            RequireBothUsernameAndEmail = false
        };
        PlayFabClientAPI.RegisterPlayFabUser(request, OnRegisterSuccess_Continue, OnError);
    }

    public void Login()
    {
        hasTriedManualAction = true;

        if (!IsValidEmail(emailInput?.text)) { WriteStatus("Adresse email invalide."); return; }
        if (string.IsNullOrEmpty(passwordInput?.text)) { WriteStatus("Mot de passe manquant."); return; }

        if (playButton) playButton.gameObject.SetActive(false);
        WriteStatus("Connexion en cours...");

        var request = new LoginWithEmailAddressRequest
        {
            Email = emailInput.text,
            Password = passwordInput.text
        };
        PlayFabClientAPI.LoginWithEmailAddress(request, OnEmailLoginSuccess_LinkCustomID, OnError);
    }

    private void OnRegisterSuccess_Continue(RegisterPlayFabUserResult result)
    {
        Debug.Log("[Auth] Inscription OK: " + result.PlayFabId);
        var req = new LoginWithEmailAddressRequest { Email = emailInput.text, Password = passwordInput.text };
        PlayFabClientAPI.LoginWithEmailAddress(req, OnEmailLoginSuccess_LinkCustomID, OnError);
    }

    private void OnEmailLoginSuccess_LinkCustomID(LoginResult result)
    {
        if (rememberMeToggle && rememberMeToggle.isOn && !sharedDeviceMode)
        {
            if (string.IsNullOrEmpty(currentCid))
            {
                currentCid = Guid.NewGuid().ToString("N");
                SecurePrefs.SetString(KEY_CUSTOM_ID, currentCid);
            }

            var linkReq = new LinkCustomIDRequest { CustomId = currentCid, ForceLink = true };
            PlayFabClientAPI.LinkCustomID(linkReq, _ =>
            {
                SecurePrefs.SetString(KEY_CUSTOM_ID, currentCid);
                PlayerPrefs.Save();
                OnLoginSuccessCommon(result, fromAuto: false);
            },
            err =>
            {
                Debug.LogWarning("[CustomID] Link erreur: " + err.GenerateErrorReport());
                OnLoginSuccessCommon(result, fromAuto: false);
            });
        }
        else
        {
            OnLoginSuccessCommon(result, fromAuto: false);
        }
    }

    void OnLoginSuccessCommon(LoginResult result, bool fromAuto)
    {
        isLoggedIn = true;
        WriteStatus(fromAuto ? "Connexion automatique réussie." : "Connexion réussie.");

        if (loginPanel) loginPanel.SetActive(false);

        if (gunAnimator)
        {
            gunAnimator.ResetTrigger(animationTriggerName);
            gunAnimator.SetTrigger(animationTriggerName);
        }
        if (gunAudioSource) gunAudioSource.Play();

        if (playButton) playButton.gameObject.SetActive(true);

        try { ReputationManager.Instance?.InitializeFromPlayFab(); } catch {}

        OnLoggedIn?.Invoke();
    }

    public void Logout()
    {
        isLoggedIn = false;
        WriteStatus("Déconnexion...");
        PlayFabClientAPI.ForgetAllCredentials();
        if (!sharedDeviceMode)
        {
            if (!(rememberMeToggle && rememberMeToggle.isOn))
                SecurePrefs.SetString(KEY_CUSTOM_ID, null);
        }
        if (loginPanel) loginPanel.SetActive(true);
        if (playButton) playButton.gameObject.SetActive(false);
        OnLoggedOut?.Invoke();
        WriteStatus("Déconnecté.");
    }
    #endregion

    #region Settings
    public void ToggleSettings(bool on)
    {
        if (settingsPanel) settingsPanel.SetActive(on);
    }
    public void OnChangeLanguage(int idx)
    {
        PlayerPrefs.SetInt(KEY_LANG, idx);
        PlayerPrefs.Save();
    }
    public void OnToggleLogo(bool on)
    {
        if (logoObject) logoObject.SetActive(on);
        PlayerPrefs.SetInt(KEY_LOGO, on ? 1 : 0);
        PlayerPrefs.Save();
    }
    public void OnToggleMusic(bool on)
    {
        if (musicAudio) musicAudio.mute = !on;
        PlayerPrefs.SetInt(KEY_MUSIC, on ? 1 : 0);
        PlayerPrefs.Save();
    }
    #endregion

    private bool IsValidEmail(string email)
    {
        try { var m = new MailAddress(email); return m.Address == email; }
        catch { return false; }
    }

    private void WriteStatus(string msg)
    {
        if (statusText) statusText.text = msg ?? "";
    }

    private void OnError(PlayFabError error)
    {
        Debug.LogError("[PlayFabAuth] " + error.GenerateErrorReport());
        WriteStatus("Erreur: " + error.ErrorMessage);
    }
}
