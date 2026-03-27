using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// NetworkBehaviour spawné une fois par room sur le serveur.
/// Sert de canal RPC isolé : seuls les clients de cette room reçoivent les événements.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class RoomBroadcaster : NetworkBehaviour
{
    // ===================== ÉTAT RÉSEAU =====================

    [Networked] public NetworkString<_16> RoomId { get; set; }

    // ===================== REGISTRE LOCAL =====================

    private static readonly Dictionary<string, RoomBroadcaster> _registry = new();

    public static RoomBroadcaster GetForRoom(string roomId)
    {
        _registry.TryGetValue(roomId, out var bc);
        return bc;
    }

    // ===================== FUSION HOOKS =====================

    public override void Spawned()
    {
        string rid = RoomId.ToString();
        if (!string.IsNullOrEmpty(rid))
        {
            _registry[rid] = this;
            Debug.Log($"[RoomBroadcaster] Enregistré pour room '{rid}'.");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        string rid = RoomId.ToString();
        if (!string.IsNullOrEmpty(rid) && _registry.TryGetValue(rid, out var bc) && bc == this)
        {
            _registry.Remove(rid);
            Debug.Log($"[RoomBroadcaster] Désenregistré pour room '{rid}'.");
        }
    }

    // ===================== API SERVEUR =====================

    /// <summary>
    /// Spawne un RoomBroadcaster pour la room indiquée (côté serveur uniquement).
    /// Nécessite un prefab NetworkObject avec RoomBroadcaster assigné dans RoomManager.
    /// </summary>
    public static RoomBroadcaster SpawnForRoom(NetworkRunner runner, NetworkPrefabRef prefabRef, string roomId)
    {
        if (!runner.IsServer)
        {
            Debug.LogWarning("[RoomBroadcaster] SpawnForRoom appelé hors serveur.");
            return null;
        }

        if (_registry.ContainsKey(roomId))
        {
            Debug.LogWarning($"[RoomBroadcaster] Broadcaster déjà présent pour '{roomId}'.");
            return _registry[roomId];
        }

        var no = runner.Spawn(prefabRef, Vector3.zero, Quaternion.identity);
        if (no == null)
        {
            Debug.LogError($"[RoomBroadcaster] Échec du spawn pour '{roomId}'.");
            return null;
        }

        var bc = no.GetComponent<RoomBroadcaster>();
        if (bc != null)
        {
            bc.RoomId = roomId;
            Debug.Log($"[RoomBroadcaster] Spawné pour room '{roomId}'.");
        }

        return bc;
    }

    // ===================== RPCs VERS CLIENTS =====================

    /// <summary>Diffuse le démarrage de match aux clients de cette room.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_OnMatchStart(string roomId)
    {
        if (!IsForMyRoom(roomId)) return;

        Debug.Log($"[RoomBroadcaster] RPC_OnMatchStart reçu pour room '{roomId}'.");

        // Affiche la barre de vie
        var hpBar = FindObjectOfType<HealthBarUI>(true);
        hpBar?.Show();

        // Cache le ClassRollUI si affiché
        var classRoll = FindObjectOfType<ClassRollUI>(true);
        classRoll?.HideImmediate();
    }

    /// <summary>Diffuse une victoire aux clients de cette room.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_OnVictory(PlayerRef winnerRef, string winnerName)
    {
        if (!IsForMyRoom(RoomId.ToString())) return;

        Debug.Log($"[RoomBroadcaster] RPC_OnVictory -> {winnerName} ({winnerRef}).");

        var wp = EnsureWinPresenter();
        wp?.ShowResult(winnerRef, winnerName);
    }

    /// <summary>Diffuse un stalemate aux clients de cette room.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_OnStalemate()
    {
        if (!IsForMyRoom(RoomId.ToString())) return;

        Debug.Log($"[RoomBroadcaster] RPC_OnStalemate reçu.");

        var wp = EnsureWinPresenter();
        wp?.ShowStalemate();
    }

    /// <summary>Diffuse un reset de round.</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_OnRoundReset()
    {
        if (!IsForMyRoom(RoomId.ToString())) return;

        Debug.Log($"[RoomBroadcaster] RPC_OnRoundReset reçu.");

        WinPresenter.Instance?.HideImmediate();

        var hpBar = FindObjectOfType<HealthBarUI>(true);
        hpBar?.Hide();
    }

    // ===================== HELPERS =====================

    /// <summary>
    /// Vérifie si ce broadcaster concerne la room du joueur local.
    /// </summary>
    private bool IsForMyRoom(string roomId)
    {
        if (Runner == null) return false;

        // Côté serveur : traite toujours
        if (Runner.IsServer) return true;

        // Côté client : vérifie via RoomManager
        if (RoomManager.Instance != null)
        {
            string myRoom = RoomManager.Instance.GetRoomIdForPlayer(Runner.LocalPlayer);
            return myRoom == roomId;
        }

        // Fallback : accepte si c'est le seul broadcaster connu
        return _registry.Count == 1;
    }

    private WinPresenter EnsureWinPresenter()
    {
        var wp = WinPresenter.Instance != null
            ? WinPresenter.Instance
            : FindObjectOfType<WinPresenter>(true);

        if (wp == null) return null;

        if (!wp.gameObject.activeSelf)
            wp.gameObject.SetActive(true);

        var cg = typeof(WinPresenter)
            .GetField("group", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(wp) as CanvasGroup;

        if (cg != null)
        {
            cg.alpha          = Mathf.Max(0f, cg.alpha);
            cg.interactable   = true;
            cg.blocksRaycasts = true;
            if (!cg.gameObject.activeSelf)
                cg.gameObject.SetActive(true);
        }

        return wp;
    }
}
