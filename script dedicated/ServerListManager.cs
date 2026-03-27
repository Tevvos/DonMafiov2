using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Affiche la liste des rooms logiques du serveur dédié.
/// Version propre — pas de SetupUI automatique, pas de ForwardScrollToParent récursif.
/// 
/// Setup Unity (tout assigner dans l'Inspector) :
///   - Refresh Button      : le bouton Refresh existant
///   - Server List Content : le Transform "Content" du ScrollView
///   - Server Entry Prefab : ton ServerEntryPrefab existant
/// </summary>
public class ServerListManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button        refreshButton;
    [SerializeField] private RectTransform serverListContent;
    [SerializeField] private GameObject    serverEntryPrefab;

    [Header("Options")]
    [SerializeField] private float autoRefreshSeconds = 4f;
    [SerializeField] private float rowMinHeight       = 56f;

    private RoomListRequester _requester;
    private bool _refreshing = false;

    // ===================== UNITY =====================

    private void Start()
    {
        if (refreshButton)
            refreshButton.onClick.AddListener(RefreshRoomList);

        // Trouve le RoomListRequester sur le FusionRunnerManager
        StartCoroutine(WaitForRequesterAndRefresh());
    }

    private void OnDestroy()
    {
        if (refreshButton)
            refreshButton.onClick.RemoveListener(RefreshRoomList);
    }

    private IEnumerator WaitForRequesterAndRefresh()
    {
        // Attend que le runner soit prêt (instancié par Bootloader)
        float timeout = 10f;
        float t = 0f;
        while (_requester == null && t < timeout)
        {
            _requester = FindObjectOfType<RoomListRequester>();
            t += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (_requester == null)
        {
            ShowInfo("RoomListRequester introuvable sur FusionRunnerManager.");
            yield break;
        }

        // Refresh initial puis toutes les N secondes
        RefreshRoomList();
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(1f, autoRefreshSeconds));
            if (_requester != null)
                RefreshRoomList();
        }
    }

    // ===================== REFRESH =====================

    public void RefreshRoomList()
    {
        if (_refreshing) return;

        if (_requester == null)
            _requester = FindObjectOfType<RoomListRequester>();

        if (_requester == null)
        {
            ShowInfo("En attente du serveur...");
            return;
        }

        _refreshing = true;
        ShowInfo("Chargement...");

        _requester.AskRoomList(rooms =>
        {
            _refreshing = false;
            BuildList(rooms);
        });
    }

    // ===================== BUILD LIST =====================

    private void BuildList(List<RoomInfo> rooms)
    {
        ClearList();

        if (rooms == null || rooms.Count == 0)
        {
            ShowInfo("Aucune room disponible — crée la première !");
            return;
        }

        foreach (var room in rooms)
            BuildRow(room);

        LayoutRebuilder.ForceRebuildLayoutImmediate(serverListContent);
    }

    private void BuildRow(RoomInfo room)
    {
        if (serverEntryPrefab == null || serverListContent == null) return;

        var entry = Instantiate(serverEntryPrefab, serverListContent);
        entry.SetActive(true);

        // Taille minimale de la ligne
        var le = entry.GetComponent<LayoutElement>();
        if (le == null) le = entry.AddComponent<LayoutElement>();
        le.minHeight = rowMinHeight;

        // Textes — on remplit dans l'ordre les TMP_Text trouvés
        var texts = entry.GetComponentsInChildren<TMP_Text>(true);
        if (texts.Length >= 1) texts[0].text = room.RoomId;
        if (texts.Length >= 2) texts[1].text = $"{room.PlayerCount}/{room.MaxPlayers}";
        if (texts.Length >= 3) texts[2].text = room.IsStarted ? "En cours" : "En attente";

        // Bouton rejoindre
        var btn = entry.GetComponentInChildren<Button>(true);
        if (btn != null)
        {
            bool canJoin = !room.IsStarted && room.PlayerCount < room.MaxPlayers;
            btn.interactable = canJoin;

            string roomId = room.RoomId;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => JoinRoom(roomId));
        }
    }

    // ===================== JOIN =====================

    private void JoinRoom(string roomId)
    {
        PlayerPrefs.SetString("DM_RequestedRoom", roomId);
        PlayerPrefs.Save();
        Debug.Log($"[ServerListManager] Rejoindre room '{roomId}'.");

        var fmm = FindObjectOfType<FusionMultiplayerManager>();
        if (fmm != null)
            fmm.OnClickJoinRoom();
        else
            Debug.LogWarning("[ServerListManager] FusionMultiplayerManager introuvable.");
    }

    // ===================== HELPERS =====================

    private void ClearList()
    {
        if (serverListContent == null) return;
        for (int i = serverListContent.childCount - 1; i >= 0; i--)
            Destroy(serverListContent.GetChild(i).gameObject);
    }

    private void ShowInfo(string message)
    {
        ClearList();
        if (serverListContent == null) return;

        var go  = new GameObject("Info", typeof(RectTransform));
        go.transform.SetParent(serverListContent, false);

        var le  = go.AddComponent<LayoutElement>();
        le.minHeight = rowMinHeight;

        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.25f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);

        var rt = textGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var txt       = textGO.AddComponent<Text>();
        txt.text      = message;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color     = Color.white;
        txt.fontSize  = 14;
        txt.raycastTarget = false;
    }
}
