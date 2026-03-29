using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

/// <summary>
/// FusionMultiplayerManager — GameMode.Shared (style Among Us)
///
/// Chaque room = une session Fusion indépendante (SessionName).
/// Pas de host : tous égaux, StateAuthority par objet.
/// La partie continue si quelqu'un quitte.
/// Rooms totalement isolées.
///
/// Boutons UI :
///   CreateRoom  → Shared, nom saisi ou aléatoire (unicité garantie)
///   JoinRoom    → Shared, nom exact saisi
///   QuickPlay   → Shared, room aléatoire existante ou nouvelle
/// </summary>
public class FusionMultiplayerManager : MonoBehaviour, INetworkRunnerCallbacks
{
    // ─── Singleton ────────────────────────────────────────────────────
    public static FusionMultiplayerManager Instance { get; private set; }

    private void MakeSingleton()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─── UI ───────────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Button createButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button quickPlayButton;
    [SerializeField] private GameObject loadingOverlay;
    [SerializeField] private GameObject menuRoot;

    // ─── Fusion ───────────────────────────────────────────────────────
    [Header("Fusion")]
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private int gameSceneBuildIndex = 2;

    // ─── Options ──────────────────────────────────────────────────────
    [Header("Options")]
    [SerializeField] private int runnerShutdownTimeoutMs = 3000;
    [SerializeField] private float shutdownCooldownSeconds = 1.0f;
    [SerializeField] private bool logVerbose = true;

    private const string PREF_LAST_ROOM = "DM_LastRoomName";

    // ─── Etat interne ─────────────────────────────────────────────────
    private NetworkRunner runner;
    private volatile bool isStarting;
    private volatile bool runnerReady;
    private SemaphoreSlim startLock = new SemaphoreSlim(1, 1);
    private float _cooldownUntilUnscaled = 0f;

    private readonly HashSet<string> _knownSessionNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public event Action<List<SessionInfo>> OnSessionsUpdated;

    // =================================================================
    //  LIFECYCLE
    // =================================================================

    private void Awake()
    {
        MakeSingleton();
        SetButtons(false);
        SetLoading(true);
        StartCoroutine(InitRunnerAndUi());
    }

    private IEnumerator InitRunnerAndUi()
    {
        float timeout = 3f, t0 = Time.unscaledTime;
        while (runner == null && Time.unscaledTime - t0 < timeout)
        {
            runner = GetComponent<NetworkRunner>() ?? FindObjectOfType<NetworkRunner>();
            if (runner != null) break;
            yield return null;
        }

        if (runner == null) { Log("ℹ️ Aucun runner → EnsureRunner()"); EnsureRunner(); }

        if (runner == null)
        {
            Debug.LogError("❌ Échec création NetworkRunner.");
            SetLoading(false); SetButtons(false); yield break;
        }

        runner.ProvideInput = true;
        runnerReady = true;

        if (roomNameInput) roomNameInput.text = string.Empty;
        yield return EnsureRunnerIdleThenEnableUI();
    }

    private void OnEnable() => StartCoroutine(CheckRunnerRebindLoop());

    private IEnumerator CheckRunnerRebindLoop()
    {
        while (true)
        {
            if (runner == null)
            {
                var found = FindObjectOfType<NetworkRunner>();
                if (found != null) { runner = found; runner.ProvideInput = true; Log("🔁 Runner rebindé."); }
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private void OnApplicationQuit()
    {
        if (runner && runner.IsRunning) runner.Shutdown();
    }

    // =================================================================
    //  ENSURE RUNNER
    // =================================================================

    private void EnsureRunner()
    {
        if (runner != null) return;

        var existing = FindObjectOfType<NetworkRunner>();
        if (existing != null)
        {
            runner = existing;
            if (!runner.GetComponent<NetworkSceneManagerDefault>())
                runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            DontDestroyOnLoad(runner.gameObject);
            Log("✅ EnsureRunner: runner existant ré-associé.");
            return;
        }

        var go = new GameObject("FusionRunnerManager");
        DontDestroyOnLoad(go);
        runner = go.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        if (!runner.GetComponent<NetworkSceneManagerDefault>())
            runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        Log("✅ EnsureRunner: nouveau runner créé.");
    }

    private IEnumerator EnsureRunnerIdleThenEnableUI()
    {
        if (runner == null) { EnsureRunner(); yield return null; }

        if (runner != null && runner.IsRunning)
        {
            Log("🔻 Runner actif → Shutdown...");
            runner.Shutdown();
        }

        float t0 = Time.realtimeSinceStartup;
        while (runner != null && runner.IsRunning && Time.realtimeSinceStartup - t0 < 5f)
            yield return null;

        while (Time.unscaledTime < _cooldownUntilUnscaled)
            yield return null;

        Log("✅ Runner idle. UI activée.");
        SetButtons(true);
        SetLoading(false);
        isStarting = false;
    }

    // =================================================================
    //  BOUTONS UI — tous GameMode.Shared
    // =================================================================

    public async void OnClickCreateRoom()
    {
        if (!CanStart()) return;
        isStarting = true;
        await StartGameSafe(isCreate: true);
    }

    public async void OnClickJoinRoom()
    {
        if (!CanStart()) return;
        isStarting = true;
        await StartGameSafe(isCreate: false);
    }

    public async void OnClickQuickPlay()
    {
        if (!CanStart()) return;
        isStarting = true;
        await StartGameSafe(isCreate: false, quickPlay: true);
    }

    private bool CanStart()
    {
        if (Time.unscaledTime < _cooldownUntilUnscaled) return false;
        if (isStarting) return false;
        if (!playerPrefab.IsValid) { Debug.LogError("🟥 PlayerPrefab non assigné."); return false; }
        if (runner == null) EnsureRunner();
        if (!runnerReady && runner == null) { Debug.LogWarning("⚠️ Runner pas prêt."); return false; }
        return true;
    }

    // =================================================================
    //  COEUR : StartGameSafe — GameMode.Shared
    // =================================================================

    private async Task StartGameSafe(bool isCreate, bool quickPlay = false)
    {
        await startLock.WaitAsync();
        try
        {
            SetButtons(false);
            SetLoading(true);
            if (menuRoot) menuRoot.SetActive(false);

            if (runner == null) EnsureRunner();
            if (runner == null) { Debug.LogError("❌ Pas de runner. Reset UI."); ResetUiState(); return; }

            if (runner.IsRunning)
            {
                Log("⏳ Runner actif → Shutdown...");
                await ShutdownRunnerWithTimeout(runnerShutdownTimeoutMs);
                _cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;
                while (Time.unscaledTime < _cooldownUntilUnscaled) await Task.Yield();
            }

            string room = BuildRoomName(isCreate, quickPlay);
            PlayerPrefs.SetString(PREF_LAST_ROOM, room);

            var sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
            if (!sceneManager) sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            var args = new StartGameArgs
            {
                GameMode     = GameMode.Shared,           // ← TOUJOURS SHARED
                SessionName  = room,
                SceneManager = sceneManager,
                Scene        = SceneRef.FromIndex(gameSceneBuildIndex),
            };

            Log($"🚀 StartGame → Shared | Room={room} | Create={isCreate} | Quick={quickPlay}");

            var result = await runner.StartGame(args);

            if (!result.Ok)
            {
                Debug.LogError($"🟥 Échec StartGame : {result.ShutdownReason}");
                ResetUiState(); return;
            }

            Log($"✅ Session Shared OK : {room}");
        }
        catch (Exception ex) { Debug.LogError($"🟥 Exception StartGameSafe: {ex}"); ResetUiState(); }
        finally { startLock.Release(); }
    }

    private async Task ShutdownRunnerWithTimeout(int timeoutMs)
    {
        try
        {
            if (runner == null) return;
            runner.Shutdown();
            float t0 = Time.realtimeSinceStartup;
            while (runner != null && runner.IsRunning && (Time.realtimeSinceStartup - t0) * 1000f < timeoutMs)
                await Task.Yield();
            if (runner != null && runner.IsRunning) Debug.LogWarning("⚠️ Timeout Shutdown.");
        }
        catch (Exception ex) { Debug.LogWarning($"⚠️ Shutdown exception: {ex.Message}"); }
    }

    // ─── BuildRoomName ────────────────────────────────────────────────

    private string BuildRoomName(bool isCreate, bool quickPlay)
    {
        if (quickPlay)
        {
            // Tente de rejoindre une room existante connue, sinon en crée une
            if (_knownSessionNames.Count > 0)
            {
                var list = new List<string>(_knownSessionNames);
                return list[UnityEngine.Random.Range(0, list.Count)];
            }
            return $"Room_{UnityEngine.Random.Range(1000, 9999)}";
        }

        string src = roomNameInput ? roomNameInput.text : string.Empty;
        src = string.IsNullOrWhiteSpace(src)
            ? string.Empty
            : Regex.Replace(src.Trim(), @"[^A-Za-z0-9_\-]", "");

        if (string.IsNullOrEmpty(src))
            src = $"Room_{UnityEngine.Random.Range(1000, 9999)}";

        // Join : nom exact
        if (!isCreate) return src;

        // Create : unicité
        string candidate = src;
        int attempts = 0;
        while (_knownSessionNames.Contains(candidate) && attempts++ < 32)
            candidate = $"{src}_{UnityEngine.Random.Range(10, 99)}";
        if (_knownSessionNames.Contains(candidate))
            candidate = $"Room_{DateTime.UtcNow:HHmmss}_{UnityEngine.Random.Range(0, 999)}";

        return candidate;
    }

    // =================================================================
    //  SPAWN — GameMode.Shared
    //
    //  En Shared, OnPlayerJoined est appelé chez TOUS pour chaque joueur.
    //  On ne spawn QUE pour runner.LocalPlayer.
    // =================================================================

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Log($"👥 OnPlayerJoined : {player} (Local={runner.LocalPlayer})");

        // Délègue à MatchFlow_Fusion si présent (il gère le spawn + la logique de match)
        if (FindObjectOfType<MatchFlow_Fusion>() != null)
        {
            Log("➡️ Spawn délégué à MatchFlow_Fusion.");
            return;
        }

        // En Shared : chaque client spawne uniquement son propre avatar
        if (player != runner.LocalPlayer)
        {
            Log($"ℹ️ Joueur distant {player}, pas de spawn local.");
            return;
        }

        StartCoroutine(SpawnLocalPlayer(runner, player));
    }

    private IEnumerator SpawnLocalPlayer(NetworkRunner runner, PlayerRef player)
    {
        // Attend que la scène de jeu soit chargée et le GameManager prêt
        while (GameManager_Fusion.Instance == null)
            yield return null;

        Vector3 spawnPos = GameManager_Fusion.Instance.GetSpawnPosition();

        // Spawn : on passe player comme inputAuthority → cet objet appartient à ce client
        NetworkObject obj = runner.Spawn(playerPrefab, spawnPos, Quaternion.identity, player);

        Log($"🚀 Spawn local OK : {player} | IA={obj.HasInputAuthority} | SA={obj.HasStateAuthority}");

        GameManager_Fusion.Instance.RegisterPlayer(player, obj);

        // Lie l'objet au runner (pour TryGetPlayerObject / GetPlayerObject)
        runner.SetPlayerObject(player, obj);
    }

    // =================================================================
    //  INPUT
    // =================================================================

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new PlayerInputData();
        if (Input.GetKey(KeyCode.A)) data.MoveX = -1;
        if (Input.GetKey(KeyCode.D)) data.MoveX =  1;
        if (Input.GetKey(KeyCode.W)) data.MoveY =  1;
        if (Input.GetKey(KeyCode.S)) data.MoveY = -1;
        input.Set(data);
    }

    // =================================================================
    //  UX HELPERS
    // =================================================================

    private void SetButtons(bool enabled)
    {
        bool allow = enabled && runner != null && !runner.IsRunning
                     && Time.unscaledTime >= _cooldownUntilUnscaled;
        if (createButton)    createButton.interactable    = allow;
        if (joinButton)      joinButton.interactable      = allow;
        if (quickPlayButton) quickPlayButton.interactable = allow;
    }

    private void SetLoading(bool visible) { if (loadingOverlay) loadingOverlay.SetActive(visible); }

    private void ResetUiState()
    {
        SetLoading(false);
        SetButtons(true);
        if (menuRoot) menuRoot.SetActive(true);
        isStarting = false;
        if (roomNameInput) roomNameInput.text = string.Empty;
    }

    private void Log(string msg) { if (logVerbose) Debug.Log(msg); }

    // =================================================================
    //  CALLBACKS RÉSEAU
    // =================================================================

    public void OnSceneLoadStart(NetworkRunner runner) { Log("🎬 SceneLoadStart"); SetLoading(true); SetButtons(false); }
    public void OnSceneLoadDone(NetworkRunner runner)  { Log("🎉 SceneLoadDone"); }
    public void OnSceneLoadFailed(NetworkRunner runner) { Debug.LogWarning("⚠️ SceneLoadFailed"); ResetUiState(); }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"🔚 OnShutdown : {shutdownReason}");
        _cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;
        ResetUiState();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Log($"👋 OnPlayerLeft : {player}");
        // En Shared la partie continue — on nettoie juste le registre local
        if (GameManager_Fusion.Instance != null)
            GameManager_Fusion.Instance.HandleDespawn(player);
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"🟥 OnConnectFailed: {reason}");
        ResetUiState();
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"⚠️ OnDisconnectedFromServer: {reason}");
        ResetUiState();
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _knownSessionNames.Clear();
        foreach (var s in sessionList)
            if (!string.IsNullOrEmpty(s.Name)) _knownSessionNames.Add(s.Name);
        OnSessionsUpdated?.Invoke(sessionList);
        if (logVerbose) Debug.Log($"🗂 Sessions: {sessionList.Count}");
    }

    // =================================================================
    //  API PUBLIQUE
    // =================================================================

    /// <summary>Retour menu propre : shutdown Shared + load scène 0.</summary>
    public async void SafeReturnToMenu()
    {
        await startLock.WaitAsync();
        try
        {
            Log("↩ SafeReturnToMenu");
            if (runner == null) EnsureRunner();
            if (runner != null && runner.IsRunning)
                await ShutdownRunnerWithTimeout(runnerShutdownTimeoutMs);

            _cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;

            if (runner != null)
            {
                try { Destroy(runner.gameObject); } catch { }
                runner = null;
            }

            while (Time.unscaledTime < _cooldownUntilUnscaled) await Task.Yield();
            SceneManager.LoadScene(0);
        }
        finally { startLock.Release(); }
    }

    // =================================================================
    //  STUBS INetworkRunnerCallbacks
    // =================================================================

    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) => request.Accept();
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    // Pas de host migration en Shared → stub vide
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token) { }
}
