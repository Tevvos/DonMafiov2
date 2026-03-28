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

public class FusionMultiplayerManager : MonoBehaviour, INetworkRunnerCallbacks
{
    // --------- Singleton persistant ---------
    public static FusionMultiplayerManager Instance { get; private set; }

    private void MakeSingleton()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    [Header("UI")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Button createButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button quickPlayButton;
    [SerializeField] private GameObject loadingOverlay;
    [SerializeField] private GameObject menuRoot;

    [Header("Fusion")]
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private int gameSceneBuildIndex = 2;

    [Header("Options")]
    [Tooltip("Historique du dernier nom; n'est plus utilisé pour préremplir l'input.")]
    [SerializeField] private bool autoFillLastRoomName = false; // <- forcé à false
    [SerializeField] private int runnerShutdownTimeoutMs = 3000;
    [SerializeField] private float shutdownCooldownSeconds = 1.0f;   // ← anti double-déclenchement
    [SerializeField] private bool logVerbose = true;
    [SerializeField] private bool forceSafeRespawnAfterMigration = true;

    private const string PREF_LAST_ROOM = "DM_LastRoomName";

    // Runner / state
    private NetworkRunner runner;
    private volatile bool isStarting;
    private volatile bool runnerReady; // true quand on a un runner stable prêt à StartGame
    private SemaphoreSlim startLock = new SemaphoreSlim(1,1);

    // Cooldown après shutdown
    private float _cooldownUntilUnscaled = 0f;

    // Migration state (persistant)
    private bool _hostMigrationInProgress;
    private bool _awaitingMigrationWindow;
    private float _awaitingMigrationDeadline;

    // Cache de rooms existantes (via lobby)
    private readonly HashSet<string> _knownSessionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public event Action<List<SessionInfo>> OnSessionsUpdated;

    // ---------------- LIFECYCLE ----------------
    private void Awake()
    {
        MakeSingleton();

        SetButtons(false);
        SetLoading(true);
        StartCoroutine(InitRunnerAndUi());
    }

    private IEnumerator InitRunnerAndUi()
    {
        // 1) Essaye de rebind un runner existant (Bootloader, etc.)
        float timeout = 3f;
        float t0 = Time.unscaledTime;
        while (runner == null && Time.unscaledTime - t0 < timeout)
        {
            runner = GetComponent<NetworkRunner>() ?? FindObjectOfType<NetworkRunner>();
            if (runner != null) break;
            yield return null;
        }

        // 2) S’il n’y en a aucun -> on le crée proprement
        if (runner == null)
        {
            Log("ℹ️ Aucun NetworkRunner trouvé -> EnsureRunner()");
            EnsureRunner(); // ← création + DontDestroyOnLoad
        }

        // Sécurité: s’il a été détruit ailleurs, re-ensure
        if (runner == null)
        {
            Debug.LogError("❌ Échec de création du NetworkRunner.");
            SetLoading(false);
            SetButtons(false);
            yield break;
        }

        runner.ProvideInput = true;
        runnerReady = true;

        // Input room toujours vide (pas de pré-remplissage)
        if (roomNameInput) roomNameInput.text = string.Empty;

        // UI seulement quand le runner est bien idle
        yield return EnsureRunnerIdleThenEnableUI();
    }

    private void OnEnable()
    {
        StartCoroutine(CheckRunnerRebindLoop());
    }

    private IEnumerator CheckRunnerRebindLoop()
    {
        while (true)
        {
            if (runner == null)
            {
                var found = FindObjectOfType<NetworkRunner>();
                if (found != null)
                {
                    runner = found;
                    runner.ProvideInput = true;
                    Log("🔁 NetworkRunner rebindé dynamiquement.");
                }
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private void OnApplicationQuit()
    {
        if (runner && runner.IsRunning)
            runner.Shutdown(); // pas d'attente bloquante ici
    }

    // ---------------- ENSURE RUNNER ----------------
    /// <summary>
    /// Crée un NetworkRunner + NetworkSceneManagerDefault si on n’en trouve aucun. Le tout en DontDestroyOnLoad.
    /// Idempotent: si un runner existe déjà, ne fait rien.
    /// </summary>
    private void EnsureRunner()
    {
        if (runner != null) return;

        var existing = FindObjectOfType<NetworkRunner>();
        if (existing != null)
        {
            runner = existing;
            if (runner.GetComponent<NetworkSceneManagerDefault>() == null)
                runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            DontDestroyOnLoad(runner.gameObject);
            Log("✅ EnsureRunner: runner existant ré-associé + NSceneMgrOK.");
            return;
        }

        // Création d’un runner propre
        var go = new GameObject("FusionRunnerManager");
        DontDestroyOnLoad(go);

        runner = go.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;

        if (runner.GetComponent<NetworkSceneManagerDefault>() == null)
            runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        Log("✅ EnsureRunner: runner créé + NetworkSceneManagerDefault ajouté.");
    }

    private IEnumerator EnsureRunnerIdleThenEnableUI()
    {
        // Si le runner n’existe plus (détruit pendant le chargement), on le recrée
        if (runner == null)
        {
            Log("⚠️ EnsureRunnerIdle: runner null → EnsureRunner()");
            EnsureRunner();
            // petit yield pour laisser Unity initialiser les comp
            yield return null;
        }

        // S’il est actif, on le coupe proprement pour repartir en idle
        if (runner != null && runner.IsRunning)
        {
            Log("🔻 Ancien runner actif -> Shutdown...");
            runner.Shutdown();
        }

        float timeout = 5f;
        float t0 = Time.realtimeSinceStartup;
        while (runner != null && runner.IsRunning && (Time.realtimeSinceStartup - t0) < timeout)
            yield return null;

        // Active l’UI uniquement hors cooldown
        while (Time.unscaledTime < _cooldownUntilUnscaled)
            yield return null;

        Log("✅ Runner idle. UI réactivée.");
        SetButtons(true);
        SetLoading(false);
        isStarting = false;
    }

    // ---------------- UI ACTIONS ----------------
    public async void OnClickCreateRoom()
    {
        if (!CanStart()) return;
        isStarting = true;
        await StartGameSafe(GameMode.Host);
    }

    public async void OnClickJoinRoom()
    {
        if (!CanStart()) return;
        isStarting = true;
        await StartGameSafe(GameMode.Client);
    }

    public async void OnClickQuickPlay()
    {
        if (!CanStart()) return;
        isStarting = true;
        await StartGameSafe(GameMode.AutoHostOrClient);
    }

    private bool CanStart()
    {
        // Bloque si on sort d’un shutdown (cooldown anti double-clics)
        if (Time.unscaledTime < _cooldownUntilUnscaled) return false;
        if (isStarting) return false;

        if (!playerPrefab.IsValid)
        {
            Debug.LogError("🟥 PlayerPrefab non assigné (NetworkPrefabRef invalide).");
            return false;
        }

        // S’assure qu’on a un runner disponible
        if (runner == null) EnsureRunner();

        if (!runnerReady && runner == null)
        {
            Debug.LogWarning("⚠️ Runner pas prêt. Attends un peu.");
            return false;
        }
        return true;
    }

    // ---------------- START / SHUTDOWN HELPERS ----------------
    private async Task StartGameSafe(GameMode mode, HostMigrationToken migrationToken = null, Action<NetworkRunner> onResume = null)
    {
        await startLock.WaitAsync();
        try
        {
            SetButtons(false);
            SetLoading(true);
            if (menuRoot) menuRoot.SetActive(false);

            // Toujours garantir un runner présent
            if (runner == null) EnsureRunner();
            if (runner == null)
            {
                Debug.LogError("❌ Pas de NetworkRunner disponible (StartGameSafe). Reset UI.");
                ResetUiState();
                return;
            }

            // Si un runner tourne encore, on le coupe avant StartGame
            if (runner.IsRunning)
            {
                Log("⏳ Runner actif -> Shutdown avant StartGame...");
                await ShutdownRunnerWithTimeout(runnerShutdownTimeoutMs);
                // Cooldown post-shutdown
                _cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;
                // Attend la fin du cooldown avant de continuer
                while (Time.unscaledTime < _cooldownUntilUnscaled)
                    await Task.Yield();
            }

            bool isMigration = (migrationToken != null);

            // IMPORTANT : nom unique pour Host / AutoHostOrClient
            bool mustBeUnique = !isMigration && (mode == GameMode.Host || mode == GameMode.AutoHostOrClient);
            string room = isMigration ? GetRoomNameForMigration() : BuildRoomName(mustBeUnique);

            // On enregistre le dernier nom, mais sans préremplir à l’avenir
            PlayerPrefs.SetString(PREF_LAST_ROOM, room);

            var sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
            if (!sceneManager) sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            var args = new StartGameArgs
            {
                GameMode     = mode,
                SessionName  = room,
                SceneManager = sceneManager,
                HostMigrationToken  = migrationToken,
                HostMigrationResume = onResume
            };

            if (!isMigration)
                args.Scene = SceneRef.FromIndex(gameSceneBuildIndex);

            Log($"🚀 StartGame -> Mode={mode} | Room={room} | Scene={(isMigration ? "restored(migration)" : gameSceneBuildIndex.ToString())} | Migration={isMigration}");

            var result = await runner.StartGame(args);

            if (!result.Ok)
            {
                Debug.LogError($"🟥 Échec StartGame : {result.ShutdownReason}");
                ResetUiState();
                return;
            }

            Log($"✅ StartGame lancé : {room} en mode {mode}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"🟥 Exception StartGameSafe: {ex}");
            ResetUiState();
        }
        finally
        {
            startLock.Release();
        }
    }

    private async Task ShutdownRunnerWithTimeout(int timeoutMs)
    {
        try
        {
            if (runner == null) return;
            runner.Shutdown();
            var t0 = Time.realtimeSinceStartup;
            while (runner != null && runner.IsRunning && (Time.realtimeSinceStartup - t0) * 1000f < timeoutMs)
                await Task.Yield();

            if (runner != null && runner.IsRunning)
                Debug.LogWarning("⚠️ Timeout Shutdown runner, on continue quand même.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"⚠️ Shutdown exception: {ex.Message}");
        }
    }

    // Construit un nom (depuis l'input si présent), et garantit l'unicité si demandé
    private string BuildRoomName(bool ensureUnique)
    {
        string src = roomNameInput ? roomNameInput.text : string.Empty;

        // Sanitize
        src = string.IsNullOrWhiteSpace(src) ? string.Empty
            : Regex.Replace(src.Trim(), @"[^A-Za-z0-9_\\-]", "");

        if (string.IsNullOrEmpty(src))
            src = $"Room_{UnityEngine.Random.Range(1000, 9999)}";

        if (!ensureUnique)
            return src;

        // Cherche un nom libre par rapport aux rooms connues
        string candidate = src;
        int attempts = 0;
        while (_knownSessionNames.Contains(candidate) && attempts++ < 32)
            candidate = $"Room_{UnityEngine.Random.Range(1000, 9999)}";

        if (_knownSessionNames.Contains(candidate))
        {
            // Dernier recours ultra-unique
            candidate = $"Room_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(0,999)}";
        }

        return candidate;
    }

    private string GetRoomNameForMigration()
    {
        var last = PlayerPrefs.GetString(PREF_LAST_ROOM, "");
        if (!string.IsNullOrWhiteSpace(last)) return last;
        return $"Room_{UnityEngine.Random.Range(1000, 9999)}";
    }

    // ---------------- SPAWN FLOW ----------------
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Log($"👥 OnPlayerJoined : {player} (Local={runner.LocalPlayer})");

        if (FindObjectOfType<MatchFlow_Fusion>() != null)
        {
            Log("➡️ Spawn délégué à MatchFlow_Fusion.");
            return;
        }

        if (!runner.IsServer)
        {
            Log("ℹ️ OnPlayerJoined ignoré côté client (spawn géré par le serveur).");
            return;
        }

        StartCoroutine(SpawnWhenReady(runner, player));
    }

    private IEnumerator SpawnWhenReady(NetworkRunner runner, PlayerRef player)
    {
        while (GameManager_Fusion.Instance == null)
            yield return null;

        Vector3 spawnPos = GameManager_Fusion.Instance.GetSpawnPosition();
        NetworkObject obj = runner.Spawn(playerPrefab, spawnPos, Quaternion.identity, player);

        Log($"🚀 Spawn joueur : {player} | IA={obj.HasInputAuthority} | SA={obj.HasStateAuthority}");
        GameManager_Fusion.Instance.RegisterPlayer(player, obj);
    }

    // ---------------- INPUT ----------------
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        PlayerInputData data = new PlayerInputData { MoveX = 0, MoveY = 0 };
        if (Input.GetKey(KeyCode.A)) data.MoveX = -1;
        if (Input.GetKey(KeyCode.D)) data.MoveX = 1;
        if (Input.GetKey(KeyCode.W)) data.MoveY = 1;
        if (Input.GetKey(KeyCode.S)) data.MoveY = -1;

        input.Set(data);
    }

    // ---------------- UX HELPERS ----------------
    private void SetButtons(bool enabled)
    {
        // Tant que le runner n’est pas idle OU qu’on est en cooldown -> boutons off
        bool allow = enabled && runner != null && !runner.IsRunning && Time.unscaledTime >= _cooldownUntilUnscaled;

        if (createButton)    createButton.interactable = allow;
        if (joinButton)      joinButton.interactable  = allow;
        if (quickPlayButton) quickPlayButton.interactable = allow;
    }

    private void SetLoading(bool visible)
    {
        if (loadingOverlay) loadingOverlay.SetActive(visible);
    }

    private void ResetUiState()
    {
        if (_awaitingMigrationWindow && Time.unscaledTime < _awaitingMigrationDeadline)
        {
            Log("⏸ UI reset ignoré (fenêtre de migration active).");
            return;
        }

        SetLoading(false);
        SetButtons(true);
        if (menuRoot) menuRoot.SetActive(true);
        isStarting = false;
        _hostMigrationInProgress = false;
        _awaitingMigrationWindow = false;

        // S'assure que l'input reste vide
        if (roomNameInput) roomNameInput.text = string.Empty;
    }

    private void Log(string msg) { if (logVerbose) Debug.Log(msg); }

    // ---------------- SCENE / CONNECTION CALLBACKS ----------------
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Log("🎬 SceneLoadStart");
        SetLoading(true);
        SetButtons(false);
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Log("🎉 SceneLoadDone");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"🔚 OnShutdown : {shutdownReason}");

        // Active un petit cooldown pour éviter les doubles StartGame/Shutdown
        _cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;

        if (_awaitingMigrationWindow && Time.unscaledTime < _awaitingMigrationDeadline)
        {
            Log("⏭ OnShutdown ignoré (fenêtre de migration).");
            return;
        }

        ResetUiState();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"🟥 OnConnectFailed: {reason} @ {remoteAddress}");
        ResetUiState();
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"⚠️ OnDisconnectedFromServer: {reason}");

        if (_awaitingMigrationWindow && Time.unscaledTime < _awaitingMigrationDeadline)
        {
            Log($"⏭ Déconnexion tolérée pendant migration ({reason}).");
            return;
        }

        ResetUiState();
    }

    public void OnSceneLoadFailed(NetworkRunner runner)
    {
        Debug.LogWarning("⚠️ SceneLoadFailed");
        ResetUiState();
    }

    // ---------------- HOST MIGRATION / PLAYER LEFT ----------------
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Log($"👋 OnPlayerLeft : {player}");

        if (runner.IsServer)
            StartCoroutine(CoMaybeShutdownIfEmptyAfterDelay(runner, 5f));
    }

    private IEnumerator CoMaybeShutdownIfEmptyAfterDelay(NetworkRunner r, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        if (r != null && r.IsServer)
        {
            int count = 0;
            foreach (var p in r.ActivePlayers) count++;

            if (count == 0)
            {
                Log("🛑 Room vide après délai -> Shutdown serveur.");
                r.Shutdown();
            }
            else
            {
                Log($"✅ Room toujours active après délai ({count} joueurs), pas de shutdown.");
            }
        }
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token)
    {
        if (_hostMigrationInProgress)
            return;

        _hostMigrationInProgress = true;

        _awaitingMigrationWindow   = true;
        _awaitingMigrationDeadline = Time.unscaledTime + 8f;

        ushort raw = (ushort)runner.LocalPlayer.RawEncoded;
        float delayMs = 200f + (raw % 501);
        Log($"🧭 HostMigration détectée -> tentative dans {delayMs:0} ms (id={raw}).");

        StartCoroutine(CoTryBecomeHostAfterDelay(delayMs / 1000f, token));
    }

    private IEnumerator CoTryBecomeHostAfterDelay(float delay, HostMigrationToken token)
    {
        yield return new WaitForSecondsRealtime(delay);

        Action<NetworkRunner> onResume = (newRunner) =>
        {
            Log("🔁 HostMigrationResume: reprise de l'état...");

            if (forceSafeRespawnAfterMigration)
                StartCoroutine(CoSafeRespawnAfterMigration(newRunner));

            _awaitingMigrationWindow = false;
        };

        _ = StartGameSafe(GameMode.Host, token, onResume);
    }

    private IEnumerator CoSafeRespawnAfterMigration(NetworkRunner newRunner)
    {
        float timeout = 6f;
        while (GameManager_Fusion.Instance == null && timeout > 0f)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (GameManager_Fusion.Instance == null)
        {
            Debug.LogWarning("⚠️ Pas de GameManager_Fusion après migration, respawn ignoré.");
            yield break;
        }

        foreach (var p in newRunner.ActivePlayers)
        {
            if (!newRunner.TryGetPlayerObject(p, out NetworkObject existing) || existing == null)
            {
                Vector3 pos = GameManager_Fusion.Instance.GetSpawnPosition();
                var obj = newRunner.Spawn(playerPrefab, pos, Quaternion.identity, p);
                GameManager_Fusion.Instance.RegisterPlayer(p, obj);
                Log($"🔄 Respawn post-migration pour {p} | IA={obj.HasInputAuthority} | SA={obj.HasStateAuthority}");
            }
        }
    }

    // ---------------- STUBS / MISC ----------------
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) => request.Accept();
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // Met à jour le cache des noms connus pour garantir l’unicité
        _knownSessionNames.Clear();
        foreach (var s in sessionList)
        {
            if (!string.IsNullOrEmpty(s.Name))
                _knownSessionNames.Add(s.Name);
        }

        OnSessionsUpdated?.Invoke(sessionList);
        if (logVerbose) Debug.Log($"🗂 Sessions: {sessionList.Count}");
    }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    // ---------------- API utilitaire ----------------
    /// <summary>
    /// Retour menu FIABLE : force un vrai shutdown (avec délai), détruit le runner si besoin, puis charge le menu.
    /// </summary>
    public async void SafeReturnToMenu()
    {
        await startLock.WaitAsync();
        try
        {
            Log("↩ SafeReturnToMenu: shutdown runner si actif puis load menu.");

            if (runner == null)
                EnsureRunner();

            if (runner != null && runner.IsRunning)
            {
                await ShutdownRunnerWithTimeout(runnerShutdownTimeoutMs);
            }

            // Cooldown léger pour éviter les races (load scene pendant destruction réseau)
            _cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;

            // Si le Runner traîne encore, on détruit le GameObject pour repartir PROPRE
            if (runner != null)
            {
                try { Destroy(runner.gameObject); } catch { }
                runner = null;
            }

            // Attend la fin du cooldown -> état propre garanti
            while (Time.unscaledTime < _cooldownUntilUnscaled)
                await Task.Yield();

            // Retour au menu
            SceneManager.LoadScene(0);
        }
        finally
        {
            startLock.Release();
        }
    }
}
