using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.UI;

public class MatchFlow_Fusion : NetworkBehaviour, INetworkRunnerCallbacks
{
    public static MatchFlow_Fusion Instance { get; private set; }
    public static bool HasInstance => Instance != null;

    // Seed global lisible par tous (propagé via RPC)
    public static int LastClassSeed { get; private set; }

    // ---------------- NEW: Helpers de garde-fou victoire ----------------
    /// <summary>Vrai uniquement quand la partie est réellement en cours.</summary>
    public static bool IsInGame()
    {
        return HasInstance && Instance.MatchStarted;
    }

    /// <summary>Autorise la résolution de victoire/points uniquement en game.</summary>
    public static bool ShouldResolveVictory()
    {
        return HasInstance && Instance.MatchStarted;
    }
    // -------------------------------------------------------------------

    [Header("Prefabs & Refs")]
    [SerializeField] private NetworkPrefabRef playerPrefab;

    [Header("Spawn Groups (assign in Inspector)")]
    [SerializeField] private Transform lobbySpawnsParent;
    [SerializeField] private Transform gameSpawnsParent;

    [Header("Lobby Return Spawns (manual list)")]
    [SerializeField] private List<Transform> lobbyReturnSpawns = new();

    [Header("UI (optional)")]
    [SerializeField] private GameObject startButtonRoot;
    [SerializeField] private Button startButton;
    [SerializeField, Min(0f)] private float startBtnFadeTime = 0.25f;
    private CanvasGroup _startBtnCg;
    private Coroutine _startBtnFadeCo;

    [Header("Start Flow (fondu + délais)")]
    [SerializeField] private ScreenFader fader;
    [SerializeField, Min(0f)] private float preStartDelay = 0.4f;
    [SerializeField, Min(0f)] private float overlayFadeDelay = 0.3f;

    [Header("Class Roll (affichée pendant noir)")]
    [SerializeField] private ClassRollUI classRollUI;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip mastodonteClip;
    [SerializeField] private AudioClip dogOfWarClip;
    [SerializeField] private AudioClip furtifClip;
    [SerializeField, Min(0f)] private float classRollDelay = 0.4f;
    [SerializeField, Min(0f)] private float classRollShowDuration = 2.5f;
    [SerializeField, Min(0f)] private float postClassBlackHold = 0.6f;

    [Header("Layering (ordre d’affichage)")]
    [SerializeField] private int fallbackFaderOrder = 10;
    [SerializeField] private int classOverFaderDelta = 100;

    [Header("Game Intro")]
    [SerializeField, Min(0f)] private float introDelayAfterStart = 1.0f;
    [SerializeField] private bool autoIntroEachRound = true;

    // Etat réseau
    [Networked] public NetworkBool MatchStarted { get; set; }
    [Networked] private int lobbyNextIndex { get; set; }
    [Networked] private int gameNextIndex  { get; set; }

    // Map réseau : clé = PlayerRef.RawEncoded, val = classe (byte)
    [Networked, Capacity(64)] public NetworkDictionary<int, byte> ClassByPlayerId => default;

    // ✅ Shared : positions de spawn assignées à chaque joueur (networked → tous les clients les voient)
    // Clé = PlayerRef.RawEncoded, val = position packed (x*1000, y*1000 en int)
    [Networked, Capacity(16)] private NetworkDictionary<int, Vector3> SpawnPositions => default;

    // FIX BUG 2 : compteur au lieu d'un bool.
    // Chaque incrémentation = nouveau signal de spawn à appliquer.
    // Fonctionne au 2e, 3e round contrairement au bool qui restait déjà à true.
    [Networked] private int _spawnVersion { get; set; }

    private readonly List<Transform> _lobbySpawns = new();
    private readonly List<Transform> _gameSpawns  = new();
    private bool _prevMatchStarted;
    private HealthBarUI _healthBar;

    private Coroutine _localCinematicCo;
    private bool _classShownThisRound = false;
    private int _lastShownSeed = int.MinValue;
    private int _currentSeed = int.MinValue;

    // Cache local (UI uniquement, jamais de gameplay)
    private bool _hasCachedLocalClass = false;
    private PlayerClassType _cachedLocalClassForStart = PlayerClassType.Mastodonte;

    // ======================= Lifecycle =======================

    public override void Spawned()
    {
        Instance = this;
        if (Runner != null) Runner.AddCallbacks(this);

        _lobbySpawns.Clear();
        _gameSpawns.Clear();
        if (lobbySpawnsParent) foreach (Transform t in lobbySpawnsParent) _lobbySpawns.Add(t);
        if (gameSpawnsParent)  foreach (Transform t in gameSpawnsParent)  _gameSpawns.Add(t);

        // ✅ Shared : Object.HasStateAuthority pour init les données réseau
        if (Object.HasStateAuthority)
        {
            MatchStarted   = false;
            lobbyNextIndex = 0;
            gameNextIndex  = 0;
            if (ClassByPlayerId.Count > 0) ClassByPlayerId.Clear();
        }

        _prevMatchStarted = MatchStarted;

        if (startButton)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnClickEnterRoom);
        }

        if (startButtonRoot)
        {
            _startBtnCg = startButtonRoot.GetComponent<CanvasGroup>();
            if (_startBtnCg == null) _startBtnCg = startButtonRoot.AddComponent<CanvasGroup>();
            _startBtnCg.alpha = 1f;
            _startBtnCg.interactable = true;
            _startBtnCg.blocksRaycasts = true;
        }

        EnsureAudioSource();
        EnsureLayering();
        RefreshStartButtonVisibility();
        CacheHealthBarIfNeeded();
        ApplyHealthBarVisibility(MatchStarted);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (runner != null) runner.RemoveCallbacks(this);
        if (Instance == this) Instance = null;
    }

    private void BringClassRollOnTop()
    {
        if (classRollUI == null) return;
        var rt = classRollUI.transform as RectTransform;
        if (rt != null) rt.SetAsLastSibling();
    }

    // --- Bouton Enter Room ---
    private void OnClickEnterRoom()
    {
        if (!Runner || MatchStarted) return;

        if (_startBtnFadeCo != null) StopCoroutine(_startBtnFadeCo);
        _startBtnFadeCo = StartCoroutine(CoFadeOutStartButton());

        RpcAskHostStartMatch();
    }

    private IEnumerator CoFadeOutStartButton()
    {
        if (_startBtnCg)
            yield return FadeCanvasGroup(_startBtnCg, 1f, 0f, startBtnFadeTime);

        if (startButtonRoot)
        {
            startButtonRoot.SetActive(false);
            if (_startBtnCg)
            {
                _startBtnCg.interactable = false;
                _startBtnCg.blocksRaycasts = false;
            }
        }
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (!cg || Mathf.Approximately(from, to)) yield break;
        float t = 0f;
        cg.alpha = from;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, duration > 0f ? t / duration : 1f);
            yield return null;
        }
        cg.alpha = to;
    }

    // =============== START FLOW ===============

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RpcAskHostStartMatch()
    {
        if (!Runner)
        {
            Debug.LogError("[MatchFlow] ❌ Start REJECTED: Runner null");
            return;
        }
        if (MatchStarted)
        {
            Debug.LogWarning("[MatchFlow] ⚠️ Start REJECTED: MatchStarted==true");
            return;
        }

        int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        LastClassSeed = seed;

        int playerCount = 0;
        foreach (var _ in Runner.ActivePlayers) playerCount++;
        Debug.Log($"[MatchFlow] ▶️ START REQUEST | stateAuth={Object.HasStateAuthority} | players={playerCount} | seed={seed}");

        RpcBeginStartCinematic(seed, classRollDelay, classRollShowDuration, postClassBlackHold);

        try { ServerPopulateClassMap(seed); }
        catch (Exception e) { Debug.LogError($"[MatchFlow] ❌ ServerPopulateClassMap FAILED: {e}"); }

        if (preStartDelay + classRollDelay + classRollShowDuration + postClassBlackHold > 0f)
            StartCoroutine(CoHostStartMatchFullSequence(seed));
        else
            HostStartMatch();
    }

    private IEnumerator CoHostStartMatchFullSequence(int seed)
    {
        float waitTotal =
            Mathf.Max(0f, classRollDelay) +
            Mathf.Max(0f, classRollShowDuration) +
            Mathf.Max(0f, postClassBlackHold) +
            Mathf.Max(0f, preStartDelay);

        if (waitTotal > 0f)
            yield return new WaitForSecondsRealtime(waitTotal);

        HostStartMatch();
    }

    // ====== Classes : Source unique serveur ======
    private void ServerPopulateClassMap(int seed)
    {
        // ✅ Shared : Object.HasStateAuthority
        if (!Runner || !Object.HasStateAuthority) return;

        if (ClassByPlayerId.Count > 0) ClassByPlayerId.Clear();

        int i = 0;
        foreach (var playerRef in Runner.ActivePlayers)
        {
            int key = playerRef.RawEncoded;
            byte cls = (byte)ResolveDeterministic(seed, key);
            ClassByPlayerId.Set(key, cls);
            Debug.Log($"[MatchFlow] ClassMap[{i}] key={key} -> {(PlayerClassType)cls}");
            i++;
        }
        Debug.Log($"[MatchFlow] ✅ ClassMap populated for {i} players (seed={seed}).");
    }

    // Helper public (utilisé par PlayerClassController)
    public static PlayerClassType GetClassFor(PlayerRef player)
    {
        if (!HasInstance) return PlayerClassType.None;
        return Instance.GetOrAssignClass(player);
    }

    public PlayerClassType GetOrAssignClass(PlayerRef player)
    {
        int key = player.RawEncoded;
        if (ClassByPlayerId.TryGet(key, out byte cls))
            return (PlayerClassType)cls;

        if (Object && Object.HasStateAuthority)
        {
            var chosen = (byte)ResolveDeterministic(LastClassSeed, key);
            ClassByPlayerId.Set(key, chosen);
            return (PlayerClassType)chosen;
        }
        return PlayerClassType.None;
    }

    public static PlayerClassType ResolveDeterministic(int seed, int key)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + seed;
            h = h * 31 + key;
            int idx = Mathf.Abs(h) % 3;
            return idx switch
            {
                0 => PlayerClassType.Mastodonte,
                1 => PlayerClassType.DogOfWar,
                _ => PlayerClassType.Furtif,
            };
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RpcBeginStartCinematic(int seed, float delayBeforeText, float showDuration, float extraBlackHold)
    {
        if (MatchStarted) return;

        _classShownThisRound = false;
        _currentSeed = seed;
        LastClassSeed = seed;

        if (_localCinematicCo != null)
        {
            StopCoroutine(_localCinematicCo);
            _localCinematicCo = null;
        }
        _localCinematicCo = StartCoroutine(CoLocalStartCinematic(seed, delayBeforeText, showDuration, extraBlackHold));
    }

    private IEnumerator CoLocalStartCinematic(int seed, float delayBeforeText, float showDuration, float extraBlackHold)
    {
        if (MatchStarted) yield break;

        if (fader != null)
            yield return fader.FadeOut();

        if (delayBeforeText > 0f)
            yield return new WaitForSeconds(delayBeforeText);

        EnsureLayering();
        yield return new WaitForEndOfFrame();
        EnsureLayering();

        PlayerClassType chosen = GetChosenClassForLocal(seed);

        if (!_classShownThisRound || _lastShownSeed != seed)
        {
            _classShownThisRound = true;
            _lastShownSeed = seed;

            if (classRollUI) classRollUI.Show(chosen);
            PlayClassSfx(chosen);
        }

        if (showDuration > 0f)
            yield return new WaitForSeconds(showDuration);

        if (extraBlackHold > 0f)
            yield return new WaitForSeconds(extraBlackHold);

        if (MatchStarted && classRollUI) classRollUI.HideImmediate();
        _localCinematicCo = null;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RpcDoStartFadeIn(float overlayDelay)
    {
        if (classRollUI) classRollUI.HideImmediate();

        if (fader != null)
        {
            fader.FadeOutOverlay(overlayDelay);
            StartCoroutine(fader.FadeIn());
        }

        _classShownThisRound = false;
        _currentSeed = int.MinValue;
    }

    private void EnsureLayering()
    {
        BringClassRollOnTop();

        var faderCanvas = fader ? fader.GetComponentInParent<Canvas>(true) : null;
        var classCanvas = classRollUI ? classRollUI.GetComponentInParent<Canvas>(true) : null;

        int baseFaderOrder = fallbackFaderOrder;

        if (faderCanvas)
        {
            faderCanvas.overrideSorting = true;
            baseFaderOrder = Mathf.Max(baseFaderOrder, faderCanvas.sortingOrder);
        }

        if (classCanvas)
        {
            classCanvas.overrideSorting = true;
            int desired = baseFaderOrder + Mathf.Max(1, classOverFaderDelta);
            if (classCanvas.sortingOrder != desired)
                classCanvas.sortingOrder = desired;
        }
    }

    // ========================= Flow principal ==========================

    public void HostStartMatch()
    {
        // ✅ Shared : Object.HasStateAuthority au lieu de Runner.IsServer
        if (!Runner || !Object.HasStateAuthority || MatchStarted)
        {
            Debug.LogWarning($"[MatchFlow] HostStartMatch skipped (Runner? {Runner != null}, HasStateAuthority? {Object?.HasStateAuthority}, MatchStarted? {MatchStarted})");
            return;
        }

        Debug.Log("[MatchFlow] 🟢 HostStartMatch: switching to IN-GAME");

        MatchStarted = true;

        if (classRollUI) classRollUI.HideImmediate();

        PlaceAllPlayersOnGameSpawns();

        RpcForceApplyFinalClasses(LastClassSeed);

        RpcDoStartFadeIn(overlayFadeDelay);

        if (autoIntroEachRound)
            RpcTriggerGameIntro(introDelayAfterStart);
    }

    private Transform GetLobbySpawn()
    {
        if (_lobbySpawns.Count == 0) return null;
        int idx = Mathf.Abs(lobbyNextIndex) % _lobbySpawns.Count;
        lobbyNextIndex = idx + 1;
        return _lobbySpawns[idx];
    }

    private Transform GetGameSpawn()
    {
        if (_gameSpawns.Count == 0) return null;
        int idx = Mathf.Abs(gameNextIndex) % _gameSpawns.Count;
        gameNextIndex = idx + 1;
        return _gameSpawns[idx];
    }

    private void PlaceAllPlayersOnGameSpawns()
    {
        // ✅ Shared : n'importe quel client avec StateAuthority sur MatchFlow peut écrire.
        // On stocke les positions dans une NetworkDictionary → tous les clients les reçoivent
        // et chacun se téléporte dans Render() quand il détecte un changement.
        if (!Object.HasStateAuthority) return;

        var gm = GameManager_Fusion.Instance;
        if (gm == null) { Debug.LogError("[MatchFlow] ❌ GameManager_Fusion introuvable."); return; }

        // Signal : incrémente le compteur → tous les clients détectent le changement
        // et se téléportent même au 2e/3e round
        SpawnPositions.Clear();

        foreach (var kv in gm.Players)
        {
            var spawnT = GetGameSpawn();
            if (spawnT == null) continue;
            SpawnPositions.Set(kv.Key.RawEncoded, spawnT.position);
            Debug.Log($"[MatchFlow] 📍 SpawnPos assignée pour {kv.Key} → {spawnT.position}");
        }

        // Incrémente APRÈS avoir rempli le dict
        _spawnVersion++;
    }

    // Version locale du spawn — si différente de _spawnVersion réseau, on se téléporte
    private int _lastAppliedSpawnVersion = -1;

    private void ApplyMySpawnPositionIfReady()
    {
        // FIX BUG 2 : on compare le compteur réseau à notre version locale
        // Si différents → nouveau spawn à appliquer, même au 2e/3e round
        if (_spawnVersion == _lastAppliedSpawnVersion) return;

        if (!Runner) return;
        int key = Runner.LocalPlayer.RawEncoded;
        if (!SpawnPositions.TryGet(key, out Vector3 pos)) return;

        // On note qu'on a traité cette version AVANT de bouger
        // pour éviter de re-appliquer si Render() est rappelé dans la même frame
        _lastAppliedSpawnVersion = _spawnVersion;

        // Cherche notre propre NetworkObject joueur
        NetworkObject myNO = null;
        var gm = GameManager_Fusion.Instance;
        if (gm != null) gm.Players.TryGetValue(Runner.LocalPlayer, out myNO);

        if (!myNO)
        {
            foreach (var no in UnityEngine.Object.FindObjectsOfType<NetworkObject>())
            {
                if (no && no.HasInputAuthority &&
                    (no.GetComponent<PlayerMovement_FusionPro>() || no.GetComponent<PlayerHealth>()))
                { myNO = no; break; }
            }
        }

        if (myNO)
        {
            myNO.transform.position = pos;
            var rb = myNO.GetComponent<Rigidbody2D>();
            if (rb) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }
            Debug.Log($"[MatchFlow] ✅ Téléporté v{_spawnVersion} {Runner.LocalPlayer} → {pos}");
        }
        else
        {
            Debug.LogWarning($"[MatchFlow] ⚠️ ApplyMySpawnPosition: joueur local introuvable !");
        }
    }

    public override void Render()
    {
        if (_prevMatchStarted != MatchStarted)
        {
            _prevMatchStarted = MatchStarted;
            RefreshStartButtonVisibility();
            CacheHealthBarIfNeeded();
            ApplyHealthBarVisibility(MatchStarted);

            if (!MatchStarted && WinPresenter.Instance)
                WinPresenter.Instance.HideImmediate();

            if (MatchStarted && classRollUI) classRollUI.HideImmediate();
        }

        // ✅ Shared : chaque client surveille le dict de spawn et se téléporte quand prêt
        ApplyMySpawnPositionIfReady();
    }

    private void RefreshStartButtonVisibility()
    {
        if (startButtonRoot == null) return;
        // Le bouton ne s’affiche que chez le serveur (le RPC permet aux clients de cliquer si tu veux l'afficher partout).
        // ✅ Shared : le bouton s'affiche chez n'importe quel client (pas besoin d'être "server")
        // ✅ Shared : seul le StateAuthority (créateur = host désigné) voit le bouton Start
        bool show = Object && Runner && Object.HasStateAuthority && !MatchStarted;
        startButtonRoot.SetActive(show);
    }

    private void CacheHealthBarIfNeeded()
    {
        if (_healthBar == null)
            _healthBar = UnityEngine.Object.FindObjectOfType<HealthBarUI>(includeInactive: true);
    }

    private void ApplyHealthBarVisibility(bool inGame)
    {
        if (_healthBar == null) return;
        if (inGame) _healthBar.Show();
        else _healthBar.Hide();
    }

    // ================== Retour au Lobby ===================

    public void RequestReturnToLobby()
    {
        if (!Runner) return;
        // ✅ Shared : StateAuthority exécute, les autres demandent via RPC
        if (Object.HasStateAuthority) DoReturnToLobby_Server();
        else RpcAskReturnToLobby();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RpcAskReturnToLobby() => DoReturnToLobby_Server();

    private void DoReturnToLobby_Server()
    {
        if (lobbyReturnSpawns == null || lobbyReturnSpawns.Count == 0)
        {
            Debug.LogWarning("⚠️ Aucun 'Lobby Return Spawns' assigné.");
            return;
        }

        var gm = GameManager_Fusion.Instance;
        if (gm == null)
        {
            Debug.LogWarning("⚠️ GameManager_Fusion introuvable.");
            return;
        }

        // FIX BUG 2 : incrémente le compteur pour que Render() détecte le changement
        SpawnPositions.Clear();
        int idx = 0;
        foreach (var kv in gm.Players)
        {
            var playerRef = kv.Key;
            var playerNO  = kv.Value;
            if (!playerNO) continue;

            var spawn = lobbyReturnSpawns[idx % lobbyReturnSpawns.Count];
            if (spawn)
                SpawnPositions.Set(playerRef.RawEncoded, spawn.position);

            // FIX BUG 1 : PlayerWeapon.ResetForLobby doit être exécuté côté StateAuthority
            // du JOUEUR (= le client propriétaire en Shared Mode), pas côté StateAuthority de MatchFlow.
            // On passe par un RPC dédié sur le PlayerWeapon pour garantir que c'est bien
            // le bon client qui nettoie son arme et met NetWeaponName = default.
            var pw = playerNO.GetComponent<PlayerWeapon>() ??
                     playerNO.GetComponentInChildren<PlayerWeapon>(true);
            if (pw != null) pw.RPC_ResetForLobbyFromServer();
            else SafeCallOptional(playerNO, "PlayerWeapon", "ResetForLobby");

            SafeCallOptional(playerNO, "PlayerHealth", "ResetForLobby");
            SafeCallOptional(playerNO, "PlayerRanking", "ResetForLobby");

            idx++;
        }
        _spawnVersion++;

        MatchStarted = false;

        // --- NEW: ferme toute UI de victoire quand on revient au lobby ---
        if (WinPresenter.Instance) WinPresenter.Instance.HideImmediate();

        _hasCachedLocalClass = false;
        // ✅ Shared : Object.HasStateAuthority
        if (Runner && Object.HasStateAuthority && ClassByPlayerId.Count > 0)
            ClassByPlayerId.Clear();

        var looseLoot = UnityEngine.Object.FindObjectsOfType<LootableWeapon>(true);
        foreach (var lw in looseLoot)
            if (lw && lw.Object != null && lw.Object.IsValid) Runner.Despawn(lw.Object);

        foreach (var spawner in UnityEngine.Object.FindObjectsOfType<WeaponSpawner>())
            spawner.RespawnAll();

        // ✅ Shared : le bouton se réaffiche chez tout le monde
        if (Runner && startButtonRoot)
        {
            startButtonRoot.SetActive(true);
            if (_startBtnCg)
            {
                _startBtnCg.alpha = 1f;
                _startBtnCg.interactable = true;
                _startBtnCg.blocksRaycasts = true;
            }
            RefreshStartButtonVisibility();
        }

        var gi = UnityEngine.Object.FindObjectOfType<GameIntro>(true);
        if (gi) gi.ResetRound();

        Debug.Log("✅ Retour Lobby : joueurs replacés + états reset.");
    }

    private void SafeCallOptional(NetworkObject root, string componentTypeName, string methodName)
    {
        try
        {
            var comp = root.GetComponentInChildren(Type.GetType(componentTypeName), true);
            if (comp == null)
            {
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                    if (c && c.GetType().Name == componentTypeName) { comp = c; break; }
            }
            if (comp == null) return;

            var mi = comp.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null && mi.GetParameters().Length == 0)
                mi.Invoke(comp, null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SafeCallOptional {componentTypeName}.{methodName} a échoué : {e.Message}");
        }
    }

    // ================== Intro SURVIVE ===================

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RpcTriggerGameIntro(float delay)
    {
        var gi = UnityEngine.Object.FindObjectOfType<GameIntro>(true);
        if (gi)
        {
            gi.useGameRulesToggle = false;
            gi.PlayNow(delay);
        }
    }

    // ================== Helpers audio ===================

    private void EnsureAudioSource()
    {
        if (!audioSource)
        {
            audioSource = GetComponent<AudioSource>();
            if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }
    }

    private void PlayClassSfx(PlayerClassType cls)
    {
        EnsureAudioSource();
        AudioClip clip = null;
        switch (cls)
        {
            case PlayerClassType.Mastodonte: clip = mastodonteClip; break;
            case PlayerClassType.DogOfWar:   clip = dogOfWarClip;   break;
            case PlayerClassType.Furtif:     clip = furtifClip;     break;
        }
        if (audioSource && clip)
            audioSource.PlayOneShot(clip);
    }

    // ================== Choix UI (sans appliquer gameplay) ===================

    private PlayerClassType GetChosenClassForLocal(int seed)
    {
        int key = Runner ? Runner.LocalPlayer.RawEncoded : 0;

        if (ClassByPlayerId.TryGet(key, out byte b))
            return (PlayerClassType)b;

        if (_hasCachedLocalClass) return _cachedLocalClassForStart;

        return ResolveDeterministic(seed, key);
    }

    // ================== VERROU FINAL D’APPLICATION ===================

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RpcForceApplyFinalClasses(int seed)
    {
        var gm = GameManager_Fusion.Instance;
        if (!gm || !Runner) return;

        if (!gm.Players.TryGetValue(Runner.LocalPlayer, out var localNO) || !localNO)
        {
            foreach (var n in FindObjectsOfType<NetworkObject>(true))
                if (n && n.HasInputAuthority && n.InputAuthority == Runner.LocalPlayer) { localNO = n; break; }
        }
        if (!localNO)
        {
            Debug.LogWarning("⚠️ RpcForceApplyFinalClasses: joueur local introuvable.");
            return;
        }

        Component controller = localNO.GetComponentInChildren(Type.GetType("PlayerClassController"), true);
        if (controller == null)
            foreach (var c in localNO.GetComponentsInChildren<Component>(true))
                if (c && c.GetType().Name == "PlayerClassController") { controller = c; break; }

        if (controller == null)
        {
            Debug.LogWarning("⚠️ RpcForceApplyFinalClasses: PlayerClassController introuvable.");
            return;
        }

        int key = localNO.InputAuthority.RawEncoded;
        PlayerClassType cls = ResolveDeterministic(seed, key);

        var t = controller.GetType();

        var pi = t.GetProperty("ClassType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null && pi.CanWrite && (pi.PropertyType == typeof(PlayerClassType) || pi.PropertyType.IsEnum))
            pi.SetValue(controller, cls);

        var mi = t.GetMethod("ApplyClassLocal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi != null && mi.GetParameters().Length == 1)
        {
            try { mi.Invoke(controller, new object[] { cls }); } catch { }
        }

        var fi = t.GetField("_appliedOnce", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fi != null && fi.FieldType == typeof(NetworkBool))
        {
            try { fi.SetValue(controller, (NetworkBool)true); } catch { }
        }

        Debug.Log($"[Class] Final APPLY local -> {cls} (seed={seed}, key={key})");
    }

    // ================== Fusion Callbacks ===================

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (MatchStarted)
        {
            if (Object.HasStateAuthority)
                runner.Disconnect(player);
            return;
        }

        // ✅ Shared : chaque client spawne UNIQUEMENT son propre avatar
        if (player == runner.LocalPlayer)
        {
            var t = GetLobbySpawn();
            Vector3 pos = t ? t.position : Vector3.zero;
            Quaternion rot = t ? t.rotation : Quaternion.identity;

            NetworkObject playerNO = runner.Spawn(playerPrefab, pos, rot, player);
            var gm = GameManager_Fusion.Instance;
            if (gm != null) gm.RegisterPlayer(player, playerNO);

            runner.SetPlayerObject(player, playerNO);
            GetOrAssignClass(player);
        }

        // ✅ Tous les clients écoutent les arrivées pour enregistrer les joueurs distants
        // On lance une coroutine qui attend que l'objet du joueur distant soit disponible
        // (il sera spawné par son propre client via Fusion replication)
        StartCoroutine(CoRegisterRemotePlayer(runner, player));
    }

    private IEnumerator CoRegisterRemotePlayer(NetworkRunner runner, PlayerRef player)
    {
        // Attend que le joueur distant ait spawné son objet (répliqué par Fusion)
        float timeout = 10f;
        float elapsed = 0f;
        NetworkObject remoteNO = null;

        while (elapsed < timeout)
        {
            // Cherche l'objet du joueur par son InputAuthority
            if (runner.TryGetPlayerObject(player, out remoteNO) && remoteNO)
                break;

            // Fallback : scan des NetworkObjects
            foreach (var no in UnityEngine.Object.FindObjectsOfType<NetworkObject>())
            {
                if (no && no.InputAuthority == player &&
                    (no.GetComponent<PlayerMovement_FusionPro>() || no.GetComponent<PlayerHealth>()))
                {
                    remoteNO = no;
                    break;
                }
            }

            if (remoteNO) break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!remoteNO)
        {
            Debug.LogWarning($"[MatchFlow] ⚠️ CoRegisterRemotePlayer: objet de {player} introuvable après {timeout}s");
            yield break;
        }

        // Enregistre dans GameManager (tous les clients ont leur propre dict local)
        var gm = GameManager_Fusion.Instance;
        if (gm != null)
        {
            gm.RegisterPlayer(player, remoteNO);
            Debug.Log($"[MatchFlow] ✅ Joueur distant {player} enregistré dans gm.Players");
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // ✅ Shared : StateAuthority nettoie la ClassMap
        if (Object.HasStateAuthority)
        {
            int key = player.RawEncoded;
            if (ClassByPlayerId.ContainsKey(key))
                ClassByPlayerId.Remove(key);
        }

        var gm = GameManager_Fusion.Instance;
        if (gm != null) gm.UnregisterPlayer(player);

        // NOTE: ne résout PAS de victoire ici. La logique de victoire doit
        // appeler MatchFlow_Fusion.ShouldResolveVictory() avant d'attribuer des points.
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
}
