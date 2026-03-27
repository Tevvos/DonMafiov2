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
    public static FusionMultiplayerManager Instance { get; private set; }

    // UI — assigné automatiquement par LobbyUI.cs au runtime (pas besoin dans le prefab)
    private TMP_InputField roomNameInput;
    private Button         joinButton;
    private Button         quickJoinButton;
    private GameObject     loadingOverlay;
    private GameObject     menuRoot;

    [Header("Fusion")]
    [SerializeField] private NetworkPrefabRef playerPrefab;
    [SerializeField] private int gameSceneBuildIndex = 2;

    [Header("Options")]
    [SerializeField] private int runnerShutdownTimeoutMs = 3000;
    [SerializeField] private float shutdownCooldownSeconds = 1f;
    [SerializeField] private bool logVerbose = true;

    // ===================== ROOM MULTI-SESSIONS =====================
    // Nom de la session Fusion UNIQUE (le serveur dédié).
    // Les rooms logiques sont gérées AU-DESSUS par RoomManager/GameSession.
    private const string DEDICATED_SESSION_NAME = "DonMafioServer";

    // La room que le joueur veut rejoindre/créer (stockée avant connexion).
    // Transmise au serveur dans OnPlayerJoined via PlayerPrefs côté client.
    private const string PREF_REQUESTED_ROOM = "DM_RequestedRoom";
    private const string PREF_LAST_ROOM      = "DM_LastRoomName";
    // ===============================================================

    private NetworkRunner runner;
    private volatile bool isStarting;
    private SemaphoreSlim startLock = new SemaphoreSlim(1, 1);
    private float cooldownUntilUnscaled = 0f;

    private readonly HashSet<string> knownSessionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public event Action<List<SessionInfo>> OnSessionsUpdated;

    private void Awake()
    {
        MakeSingleton();
        SetButtons(false);
        SetLoading(true);
        StartCoroutine(InitRunnerAndUi());
    }

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

    private IEnumerator InitRunnerAndUi()
    {
        EnsureRunner();

        if (runner == null)
        {
            Debug.LogError("❌ Impossible de créer/trouver le runner.");
            yield break;
        }

        runner.ProvideInput = true;

        if (roomNameInput) roomNameInput.text = string.Empty;

        yield return EnsureRunnerIdleThenEnableUI();
    }

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
            return;
        }

        var go = new GameObject("FusionRunnerManager");
        DontDestroyOnLoad(go);
        runner = go.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
    }

    private IEnumerator EnsureRunnerIdleThenEnableUI()
    {
        if (runner != null && runner.IsRunning)
            runner.Shutdown();

        float timeout = 5f;
        float start   = Time.realtimeSinceStartup;

        while (runner != null && runner.IsRunning && (Time.realtimeSinceStartup - start) < timeout)
            yield return null;

        while (Time.unscaledTime < cooldownUntilUnscaled)
            yield return null;

        SetButtons(true);
        SetLoading(false);
        isStarting = false;
    }

    // ===================== BOUTONS UI =====================

    public async void OnClickJoinRoom()
    {
        if (!CanStart()) return;
        isStarting = true;

        // Stocke la room demandée pour que le serveur la lise dans OnPlayerJoined
        string requestedRoom = BuildRoomName();
        PlayerPrefs.SetString(PREF_REQUESTED_ROOM, requestedRoom);
        PlayerPrefs.Save();

        await StartClientGame();
    }

    public async void OnClickQuickJoin()
    {
        if (!CanStart()) return;
        isStarting = true;

        // Quick-join : aucune room spécifique demandée, le serveur choisit
        PlayerPrefs.DeleteKey(PREF_REQUESTED_ROOM);
        PlayerPrefs.Save();

        await StartClientGame();
    }

    public async void OnClickCreateRoom()
    {
        if (!CanStart()) return;
        isStarting = true;

        // Crée une room avec le nom saisi
        string newRoom = BuildRoomName();
        PlayerPrefs.SetString(PREF_REQUESTED_ROOM, newRoom);
        PlayerPrefs.Save();

        await StartClientGame();
    }

    // ===================== CONNEXION =====================

    private bool CanStart()
    {
        if (Time.unscaledTime < cooldownUntilUnscaled) return false;
        if (isStarting) return false;
        EnsureRunner();
        if (runner == null) { Debug.LogError("❌ Runner introuvable."); return false; }
        return true;
    }

    private async Task StartClientGame()
    {
        await startLock.WaitAsync();
        try
        {
            SetButtons(false);
            SetLoading(true);
            if (menuRoot) menuRoot.SetActive(false);

            if (runner == null) EnsureRunner();

            if (runner.IsRunning)
            {
                await ShutdownRunnerWithTimeout(runnerShutdownTimeoutMs);
                cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;
                while (Time.unscaledTime < cooldownUntilUnscaled)
                    await Task.Yield();
            }

            // Tous les clients se connectent à la MÊME session Fusion (le serveur dédié).
            // La séparation en rooms logiques est gérée côté serveur par RoomManager.
            var sceneManager = runner.GetComponent<NetworkSceneManagerDefault>()
                            ?? runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode     = GameMode.Client,
                SessionName  = DEDICATED_SESSION_NAME,
                SceneManager = sceneManager
            });

            if (!result.Ok)
            {
                Debug.LogError($"❌ Connexion échouée : {result.ShutdownReason}");
                ResetUiState();
                return;
            }

            Log($"✅ Connecté au serveur dédié '{DEDICATED_SESSION_NAME}'.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Exception StartClientGame : {ex}");
            ResetUiState();
        }
        finally
        {
            startLock.Release();
        }
    }

    private async Task ShutdownRunnerWithTimeout(int timeoutMs)
    {
        if (runner == null) return;
        runner.Shutdown();
        float start = Time.realtimeSinceStartup;
        while (runner != null && runner.IsRunning && (Time.realtimeSinceStartup - start) * 1000f < timeoutMs)
            await Task.Yield();
    }

    private string BuildRoomName()
    {
        string src = roomNameInput ? roomNameInput.text : string.Empty;
        src = string.IsNullOrWhiteSpace(src)
            ? string.Empty
            : Regex.Replace(src.Trim(), @"[^A-Za-z0-9_\-]", "");
        if (string.IsNullOrEmpty(src)) src = "DonMafioRoom";
        return src;
    }

    // ===================== CALLBACKS FUSION =====================

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Log($"👥 OnPlayerJoined : {player}");
        if (!runner.IsServer) return;

        StartCoroutine(SpawnPlayerInRoom(runner, player));
    }

    private IEnumerator SpawnPlayerInRoom(NetworkRunner runner, PlayerRef player)
    {
        // Attend que GameManager_Fusion et RoomManager soient prêts
        while (GameManager_Fusion.Instance == null)
            yield return null;

        while (RoomManager.Instance == null)
            yield return null;

        // ── Lecture de la room demandée ──────────────────────────────────────
        // Le client a stocké PREF_REQUESTED_ROOM avant de se connecter.
        // Fusion ne fournit pas de mécanisme natif pour lire les PlayerPrefs
        // du client côté serveur, donc on utilise une convention :
        // le client peut envoyer un RPC juste après le spawn pour indiquer sa room.
        // Pour l'instant, on place le joueur via quick-join (premier slot disponible).
        // Si tu veux transmettre le nom de room, utilise RPC_RequestRoom (voir bas du fichier).
        string roomId = RoomManager.Instance.JoinOrCreateRoom(player, requestedRoomId: null);

        if (roomId == null)
        {
            Debug.LogWarning($"[FMM] Impossible de placer {player} dans une room. Connexion refusée.");
            yield break;
        }

        // ── Spawn de l'avatar ────────────────────────────────────────────────
        var session  = RoomManager.Instance.GetSessionForPlayer(player);
        Vector3 pos  = GameManager_Fusion.Instance.GetSpawnPosition();

        NetworkObject avatar = runner.Spawn(playerPrefab, pos, Quaternion.identity, player);
        GameManager_Fusion.Instance.RegisterPlayer(player, avatar);
        RoomManager.Instance.RegisterAvatar(player, avatar);

        // Notifie les règles de victoire (ancienne API conservée pour compatibilité)
        GameRules_Victory_Fusion.NotifySpawn(player);

        Log($"✅ {player} spawné dans room '{roomId}' à {pos}.");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Log($"👋 OnPlayerLeft : {player}");
        if (!runner.IsServer) return;

        // Retire le joueur de sa room logique
        RoomManager.Instance?.RemovePlayer(player);

        // Despawn de l'avatar
        var gm = GameManager_Fusion.Instance;
        if (gm != null)
        {
            var obj = gm.GetPlayerObject(player);
            if (obj != null) runner.Despawn(obj);
            gm.HandleDespawn(player);
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new PlayerInputData { MoveX = 0, MoveY = 0 };
        if (Input.GetKey(KeyCode.A)) data.MoveX = -1;
        if (Input.GetKey(KeyCode.D)) data.MoveX =  1;
        if (Input.GetKey(KeyCode.W)) data.MoveY =  1;
        if (Input.GetKey(KeyCode.S)) data.MoveY = -1;
        input.Set(data);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;
        ResetUiState();
    }

    public void OnSceneLoadStart(NetworkRunner runner) { SetLoading(true); SetButtons(false); }
    public void OnSceneLoadDone(NetworkRunner runner)  { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { ResetUiState(); }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) => request.Accept();
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { ResetUiState(); }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        knownSessionNames.Clear();
        foreach (var s in sessionList)
            if (!string.IsNullOrEmpty(s.Name)) knownSessionNames.Add(s.Name);
        OnSessionsUpdated?.Invoke(sessionList);
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    // ===================== UI HELPERS =====================

    private void SetButtons(bool enabled)
    {
        bool allow = enabled && runner != null && !runner.IsRunning && Time.unscaledTime >= cooldownUntilUnscaled;
        if (joinButton)      joinButton.interactable      = allow;
        if (quickJoinButton) quickJoinButton.interactable = allow;
    }

    private void SetLoading(bool visible)
    {
        if (loadingOverlay) loadingOverlay.SetActive(visible);
    }

    private void ResetUiState()
    {
        SetLoading(false);
        SetButtons(true);
        if (menuRoot) menuRoot.SetActive(true);
        isStarting = false;
        if (roomNameInput) roomNameInput.text = string.Empty;
    }

    private void Log(string msg) { if (logVerbose) Debug.Log(msg); }

    // ===================== RETOUR MENU =====================

    public async void SafeReturnToMenu()
    {
        await startLock.WaitAsync();
        try
        {
            if (runner != null && runner.IsRunning)
                await ShutdownRunnerWithTimeout(runnerShutdownTimeoutMs);

            cooldownUntilUnscaled = Time.unscaledTime + shutdownCooldownSeconds;

            if (runner != null)
            {
                Destroy(runner.gameObject);
                runner = null;
            }

            while (Time.unscaledTime < cooldownUntilUnscaled)
                await Task.Yield();

            SceneManager.LoadScene(0);
        }
        finally
        {
            startLock.Release();
        }
    }
}
