using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ServerListManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button refreshButton;
    [SerializeField] private RectTransform serverListContent; // MUST be the Content under the Viewport
    [SerializeField] private GameObject serverEntryPrefab;

    [Header("Layout")]
    [SerializeField] private int  gameSceneBuildIndex = 2;
    [SerializeField] private bool autoRefreshOnStart = true;
    [SerializeField] private float rowMinHeight = 56f;
    [SerializeField] private int  contentPadding = 12;
    [SerializeField] private float rowSpacing = 8f;

    [Header("Scroll Feel")]
    [SerializeField] private float scrollWheelSensitivity = 35f;
    [Range(0f, 1f)] [SerializeField] private float elasticity = 0.18f;
    [Range(0.01f, 1f)] [SerializeField] private float inertiaSlowdown = 0.85f;
    [SerializeField] private bool  useElastic = true;
    [SerializeField] private bool  clampSafety = true;

    [Header("Reliability")]
    [SerializeField] private float minRefreshInterval   = 0.6f; // anti-spam
    [SerializeField] private int   lobbyJoinMaxAttempts = 3;    // retry si on clique trop tôt
    [SerializeField] private float lobbyJoinRetryDelay  = 0.5f; // délai entre tentatives
    [SerializeField] private float runnerIdleTimeout    = 2.5f; // attente idle après shutdown

    private NetworkRunner _runner;
    private NetworkSceneManagerDefault _sceneManager;

    private ScrollRect _scroll;
    private RectTransform _viewport;
    private Scrollbar _vScrollbar;
    private int _rowIndex = 0;

    private GameObject _infoRowRef;

    // State
    private bool _isJoining = false;
    private bool _refreshInFlight = false;
    private float _lastRefreshTime = -999f;
    private CancellationTokenSource _refreshCts;

    private void OnEnable()
    {
        if (autoRefreshOnStart)
            Invoke(nameof(RefreshServerList), 0.15f); // laisse l’UI s’initialiser
    }

    private async void Start()
    {
        SetupUI();

        // Tente d’utiliser un runner existant (idéalement celui du FusionMultiplayerManager)
        _runner = FindObjectOfType<NetworkRunner>();
        if (_runner == null)
        {
            // Sécurisé : on en crée un seulement si aucun n’existe (évite doublons)
            var go = new GameObject("FusionRunnerManager_UI");
            DontDestroyOnLoad(go);
            _runner = go.AddComponent<NetworkRunner>();
            _sceneManager = go.AddComponent<NetworkSceneManagerDefault>();
        }
        else
        {
            _sceneManager = _runner.GetComponent<NetworkSceneManagerDefault>() ?? _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        if (refreshButton) refreshButton.onClick.AddListener(RefreshServerList);

        // On entre au lobby (avec retry) AVANT le premier refresh automatique
        await EnsureInLobbyWithRetry();

        if (autoRefreshOnStart) RefreshServerList();
    }

    private void OnDestroy()
    {
        if (refreshButton) refreshButton.onClick.RemoveListener(RefreshServerList);
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_viewport && serverListContent)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(serverListContent);
            ClampContentInsideViewport();
        }
    }

    private void LateUpdate()
    {
        if (!clampSafety || _scroll == null || _viewport == null || serverListContent == null) return;
        ClampContentInsideViewport();
    }

    private void SetupUI()
    {
        _scroll = GetComponentInChildren<ScrollRect>(true);
        if (_scroll == null) _scroll = gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false;
        _scroll.vertical   = true;

        _viewport = _scroll.viewport;
        if (_viewport == null)
        {
            _viewport = _scroll.transform.Find("Viewport") as RectTransform;
            if (_viewport == null)
            {
                var vpGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
                _viewport = vpGO.GetComponent<RectTransform>();
                _viewport.SetParent(_scroll.transform, false);
            }
        }
        if (!_viewport.TryGetComponent<Image>(out var vpImg)) vpImg = _viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(1, 1, 1, 0);
        vpImg.raycastTarget = true;
        if (!_viewport.TryGetComponent<RectMask2D>(out _)) _viewport.gameObject.AddComponent<RectMask2D>();
        _viewport.anchorMin = Vector2.zero;
        _viewport.anchorMax = Vector2.one;
        _viewport.pivot     = new Vector2(0.5f, 0.5f);
        _viewport.offsetMin = Vector2.zero;
        _viewport.offsetMax = Vector2.zero;

        if (serverListContent != null && serverListContent.name.ToLower().Contains("serverlistcontent"))
        {
            var vp = serverListContent.Find("Viewport") as RectTransform;
            var ct = vp ? vp.Find("Content") as RectTransform : null;
            if (ct != null) serverListContent = ct;
        }
        if (serverListContent == null)
        {
            var guessVp = _scroll.transform.Find("Viewport") as RectTransform;
            if (guessVp) serverListContent = guessVp.Find("Content") as RectTransform;
        }
        if (serverListContent == null)
        {
            var cGO = new GameObject("Content", typeof(RectTransform));
            serverListContent = cGO.GetComponent<RectTransform>();
            serverListContent.SetParent(_viewport, false);
        }

        _scroll.viewport = _viewport;
        _scroll.content  = serverListContent;

        _scroll.movementType      = useElastic ? ScrollRect.MovementType.Elastic : ScrollRect.MovementType.Clamped;
        _scroll.elasticity        = Mathf.Clamp01(elasticity);
        _scroll.inertia           = true;
        _scroll.decelerationRate  = Mathf.Clamp01(inertiaSlowdown);
        _scroll.scrollSensitivity = scrollWheelSensitivity;

        EnsureVerticalScrollbar();

        var vlg = serverListContent.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = serverListContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(contentPadding, contentPadding, contentPadding, contentPadding);
        vlg.spacing = rowSpacing;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var fitter = serverListContent.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = serverListContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        serverListContent.anchorMin = new Vector2(0, 1);
        serverListContent.anchorMax = new Vector2(1, 1);
        serverListContent.pivot     = new Vector2(0.5f, 1f);
        serverListContent.offsetMin = Vector2.zero;
        serverListContent.offsetMax = Vector2.zero;

        foreach (var rt in GetComponentsInParent<RectTransform>(true))
            rt.localScale = Vector3.one;

        AddScrollForwarders(serverListContent.transform);
    }

    private void EnsureVerticalScrollbar()
    {
        _vScrollbar = GetComponentInChildren<Scrollbar>(true);
        if (_vScrollbar != null)
        {
            _scroll.verticalScrollbar = _vScrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }
    }

    private void ClampContentInsideViewport()
    {
        var viewH = _viewport.rect.height;
        var contentH = serverListContent.rect.height;

        var pos = serverListContent.anchoredPosition;
        float maxY = Mathf.Max(0f, contentH - viewH);
        pos.y = Mathf.Clamp(pos.y, 0f, maxY);
        serverListContent.anchoredPosition = pos;
    }

    // ---------- Lobby / Runner guards ----------

    private async Task<bool> EnsureRunnerIdle(float timeout)
    {
        if (_runner == null) return false;

        float t0 = Time.realtimeSinceStartup;
        while (_runner.IsRunning && (Time.realtimeSinceStartup - t0) < timeout)
            await Task.Yield();

        return !_runner.IsRunning;
    }

    private async Task<bool> EnsureInLobbyWithRetry()
    {
        if (_runner == null) return false;

        for (int attempt = 1; attempt <= lobbyJoinMaxAttempts; attempt++)
        {
            try
            {
                await _runner.JoinSessionLobby(SessionLobby.Shared);
                return true;
            }
            catch (Exception e)
            {
                Debug.Log($"[ServerListManager] JoinSessionLobby tentative {attempt}/{lobbyJoinMaxAttempts} : {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(lobbyJoinRetryDelay));
            }
        }
        return false;
    }

    // ---------- Public UI entrypoint ----------

    public void RefreshServerList()
    {
        // Anti-spam (min interval) + exécution unique
        if (_refreshInFlight) return;
        if ((Time.unscaledTime - _lastRefreshTime) < minRefreshInterval) return;

        _ = RefreshServerListAsync();
    }

    // ---------- Core refresh flow (robuste) ----------

    private async Task RefreshServerListAsync()
    {
        _refreshInFlight = true;
        _lastRefreshTime = Time.unscaledTime;

        // Annule une éventuelle requête précédente
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;

        // Désactive le bouton pendant l’opération
        if (refreshButton) refreshButton.interactable = false;

        try
        {
            if (_runner == null)
            {
                Debug.LogWarning("[ServerListManager] Pas de NetworkRunner au refresh. J’essaie d’en retrouver un…");
                _runner = FindObjectOfType<NetworkRunner>();
                if (_runner == null)
                {
                    BuildInfoRow("Runner not ready");
                    return;
                }
                _sceneManager = _runner.GetComponent<NetworkSceneManagerDefault>() ?? _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            }

            // Si un runner tourne (ex: était en partie), coupe et attends l’idle
            if (_runner.IsRunning)
            {
                _runner.Shutdown();
                bool idle = await EnsureRunnerIdle(runnerIdleTimeout);
                if (!idle)
                {
                    Debug.LogWarning("[ServerListManager] Runner encore actif après timeout idle, je tente tout de même le lobby.");
                }
            }

            // S’assure d’être dans le lobby (avec retry)
            bool inLobby = await EnsureInLobbyWithRetry();
            if (!inLobby)
            {
                BuildInfoRow("Lobby not ready");
                return;
            }

            // Affiche un état “Loading…” pendant la fetch
            BuildInfoRow("Loading...");

            List<SessionInfo> sessions = null;
            try
            {
                // Selon ta version de Fusion 2, GetSessionList est async (ok).
                sessions = await _runner.GetSessionList();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerListManager] GetSessionList a échoué: {e.Message}");
                BuildInfoRow("Error fetching sessions");
                return;
            }
            if (token.IsCancellationRequested) return;

            // Nettoie et reconstruit
            ClearServerList();
            if (_infoRowRef != null) { Destroy(_infoRowRef); _infoRowRef = null; }

            if (sessions == null || sessions.Count == 0)
            {
                BuildInfoRow("No servers");
                LayoutRebuilder.ForceRebuildLayoutImmediate(serverListContent);
                ResetScrollToTop();
                return;
            }

            _rowIndex = 0;
            foreach (var s in sessions)
            {
                BuildRow(s, _rowIndex);
                _rowIndex++;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(serverListContent);
            ResetScrollToTop();
        }
        finally
        {
            _refreshInFlight = false;
            if (refreshButton) refreshButton.interactable = true;
        }
    }

    private void ResetScrollToTop()
    {
        if (serverListContent) serverListContent.anchoredPosition = Vector2.zero;
        if (_scroll) _scroll.normalizedPosition = new Vector2(0, 1f);
        ClampContentInsideViewport();
    }

    private void ClearServerList()
    {
        if (!serverListContent) return;
        for (int i = serverListContent.childCount - 1; i >= 0; i--)
            Destroy(serverListContent.GetChild(i).gameObject);
        LayoutRebuilder.ForceRebuildLayoutImmediate(serverListContent);
    }

    private void BuildInfoRow(string message)
    {
        if (!serverListContent) return;

        if (_infoRowRef != null)
        {
            Destroy(_infoRowRef);
            _infoRowRef = null;
        }

        var go = new GameObject("Info", typeof(RectTransform));
        go.transform.SetParent(serverListContent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, rowMinHeight);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight       = rowMinHeight;
        le.preferredHeight = rowMinHeight;
        le.preferredWidth  = -1;
        le.flexibleWidth   = 1;

        var img = go.AddComponent<Image>(); img.color = new Color(0,0,0,0.25f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var legacy = textGO.AddComponent<Text>();
        legacy.text = message; legacy.alignment = TextAnchor.MiddleCenter; legacy.raycastTarget = false; legacy.color = Color.white;

        AddScrollForwarders(go.transform);
        _infoRowRef = go;
    }

    private void ApplyRowStyle(GameObject entry, int index)
    {
        var baseCol = (index % 2 == 0) ? new Color(1f,1f,1f,0.06f) : new Color(1f,1f,1f,0.12f);

        var bg = entry.GetComponent<Image>();
        if (bg == null) bg = entry.AddComponent<Image>();
        bg.raycastTarget = true;
        bg.color = baseCol;

        if (!entry.TryGetComponent<EventTrigger>(out var trigger))
            trigger = entry.AddComponent<EventTrigger>();

        trigger.triggers ??= new List<EventTrigger.Entry>();
        trigger.triggers.Clear();

        void AddTrigger(EventTriggerType type, Action cb)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => cb());
            trigger.triggers.Add(e);
        }

        AddTrigger(EventTriggerType.PointerEnter, () =>
        {
            bg.color = baseCol + new Color(0f,0.5f,1f,0.08f);
            entry.transform.localScale = Vector3.one * 1.01f;
        });
        AddTrigger(EventTriggerType.PointerExit, () =>
        {
            bg.color = baseCol;
            entry.transform.localScale = Vector3.one;
        });
    }

    private void BuildRow(SessionInfo s, int index)
    {
        var entry = Instantiate(serverEntryPrefab);
        entry.transform.SetParent(serverListContent, false);

        ApplyRowStyle(entry, index);

        var er = entry.transform as RectTransform;
        if (er != null)
        {
            er.anchorMin = new Vector2(0, 1);
            er.anchorMax = new Vector2(1, 1);
            er.pivot     = new Vector2(0.5f, 1);
            er.offsetMin = Vector2.zero;
            er.offsetMax = Vector2.zero;
            if (er.sizeDelta.y < rowMinHeight) er.sizeDelta = new Vector2(er.sizeDelta.x, rowMinHeight);
            er.localScale = Vector3.one;
        }

        var le = entry.GetComponent<LayoutElement>();
        if (le == null) le = entry.AddComponent<LayoutElement>();
        le.minHeight       = Mathf.Max(le.minHeight, rowMinHeight);
        le.preferredHeight = Mathf.Max(le.preferredHeight, rowMinHeight);
        le.preferredWidth  = -1f;
        le.flexibleWidth   = 1f;

        var panel = entry.transform.Find("Panel") as RectTransform;
        if (panel)
        {
            panel.anchorMin = new Vector2(0, 0);
            panel.anchorMax = new Vector2(1, 1);
            panel.pivot     = new Vector2(0.5f, 0.5f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            if (panel.TryGetComponent<LayoutElement>(out var ple))
            {
                ple.preferredWidth = -1f;
                ple.flexibleWidth  = 1f;
            }

            var hlg = panel.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 12, 8, 8);
            hlg.spacing = 8;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;

            var nameTf = panel.Find("ServerNameText");
            if (nameTf)
            {
                LayoutElement nameLE = nameTf.GetComponent<LayoutElement>();
                if (nameLE == null) nameLE = nameTf.gameObject.AddComponent<LayoutElement>();
                nameLE.flexibleWidth = 1;
                nameLE.preferredWidth = -1;
            }

            var countTf = panel.Find("PlayerCountText");
            if (countTf)
            {
                LayoutElement countLE = countTf.GetComponent<LayoutElement>();
                if (countLE == null) countLE = countTf.gameObject.AddComponent<LayoutElement>();
                countLE.flexibleWidth = 0;
                countLE.preferredWidth = -1;
            }

            var joinTf = panel.Find("JoinButton");
            if (joinTf)
            {
                LayoutElement joinLE = joinTf.GetComponent<LayoutElement>();
                if (joinLE == null) joinLE = joinTf.gameObject.AddComponent<LayoutElement>();
                joinLE.preferredWidth = 80;
                joinLE.flexibleWidth = 0;
            }
        }

        Transform nameNode  = entry.transform.Find("Panel/ServerNameText") ?? entry.transform.Find("ServerNameText");
        Transform countNode = entry.transform.Find("Panel/PlayerCountText") ?? entry.transform.Find("PlayerCountText");
        Transform joinNode  = entry.transform.Find("Panel/JoinButton")     ?? entry.transform.Find("JoinButton");

        if (nameNode)
        {
            if (nameNode.TryGetComponent<Text>(out var t)) t.text = s.Name;
            else if (nameNode.TryGetComponent<TextMeshProUGUI>(out var tmp)) tmp.text = s.Name;
        }

        string countStr = $"{s.PlayerCount}/{s.MaxPlayers}";
        if (countNode)
        {
            if (countNode.TryGetComponent<Text>(out var t2)) t2.text = countStr;
            else if (countNode.TryGetComponent<TextMeshProUGUI>(out var tmp2)) tmp2.text = countStr;
        }

        if (joinNode && joinNode.TryGetComponent<Button>(out var joinBtn))
        {
            var sessionName = s.Name;
            joinBtn.onClick.RemoveAllListeners();
            joinBtn.onClick.AddListener(() => TryJoinSession(sessionName, joinBtn));
        }

        AddScrollForwarders(entry.transform);
        entry.SetActive(true);
    }

    private void AddScrollForwarders(Transform root)
    {
        foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
        {
            if (!sel.gameObject.TryGetComponent<ForwardScrollToParent>(out _))
                sel.gameObject.AddComponent<ForwardScrollToParent>();
        }
        if (!root.gameObject.TryGetComponent<ForwardScrollToParent>(out _))
            root.gameObject.AddComponent<ForwardScrollToParent>();
    }

    private async void TryJoinSession(string sessionName, Button uiButton = null)
    {
        if (_isJoining) return;
        _isJoining = true;
        if (uiButton) uiButton.interactable = false;

        try
        {
            // Si un runner tourne (client/host précédent), coupe proprement
            if (_runner.IsRunning)
            {
                _runner.Shutdown();
                await EnsureRunnerIdle(runnerIdleTimeout);
            }

            var args = new StartGameArgs
            {
                GameMode     = GameMode.Client,
                SessionName  = sessionName,
                SceneManager = _sceneManager
            };

            var result = await _runner.StartGame(args);
            if (!result.Ok)
            {
                Debug.LogError($"[ServerListManager] Join échoué: {result.ShutdownReason}");
                RefreshServerList();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerListManager] Exception Join: {e}");
            RefreshServerList();
        }
        finally
        {
            _isJoining = false;
            if (uiButton) uiButton.interactable = true;
        }
    }
}

/// <summary>
/// Redirige les événements de drag/scroll reçus par un enfant (ex: Button)
/// vers le ScrollRect parent pour permettre le défilement partout.
/// </summary>
public class ForwardScrollToParent : MonoBehaviour,
    IBeginDragHandler, IEndDragHandler, IDragHandler, IScrollHandler,
    IPointerDownHandler, IPointerUpHandler
{
    private ScrollRect _parent;

    private void Awake()
    {
        _parent = GetComponentInParent<ScrollRect>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        ExecuteToParent(eventData, ExecuteEvents.beginDragHandler);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        ExecuteToParent(eventData, ExecuteEvents.endDragHandler);
    }

    public void OnDrag(PointerEventData eventData)
    {
        ExecuteToParent(eventData, ExecuteEvents.dragHandler);
    }

    public void OnScroll(PointerEventData eventData)
    {
        ExecuteToParent(eventData, ExecuteEvents.scrollHandler);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        ExecuteToParent(eventData, ExecuteEvents.pointerDownHandler);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ExecuteToParent(eventData, ExecuteEvents.pointerUpHandler);
    }

    private void ExecuteToParent<T>(PointerEventData data, ExecuteEvents.EventFunction<T> fun) where T : IEventSystemHandler
    {
        if (_parent != null)
        {
            ExecuteEvents.Execute(_parent.gameObject, data, fun);
        }
    }
}
