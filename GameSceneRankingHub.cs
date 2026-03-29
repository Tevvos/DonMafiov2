using Fusion;
using UnityEngine;

/// <summary>
/// GameSceneRankingHub — GameMode.Shared
///
/// En Shared il n'y a pas de runner.IsServer global.
/// L'autorité est par objet : on utilise Object.HasStateAuthority.
/// Les RPCs vers StateAuthority fonctionnent normalement en Shared.
/// </summary>
[DisallowMultipleComponent]
public class GameSceneRankingHub : NetworkBehaviour
{
    public static GameSceneRankingHub Instance;

    [Header("Logs")]
    [SerializeField] private bool verbose = true;

    void Awake() => Instance = this;

    /// <summary>
    /// En Shared : on considère "autorité" = StateAuthority de CET objet.
    /// (Runner.IsServer n'existe pas en Shared — supprimé.)
    /// </summary>
    bool HasServerAuth() => Object && Object.HasStateAuthority;

    // ===================== API PUBLIQUE =====================

    public static void ReportKill(PlayerRef killer, PlayerRef victim)
    {
        if (!Instance) { Debug.LogWarning("[RankingHub] Instance absente."); return; }
        Instance.RPC_ServerReportKill(killer, victim);
    }

    public static void ReportVictory(PlayerRef winner)
    {
        if (!Instance) { Debug.LogWarning("[RankingHub] Instance absente."); return; }
        Instance.RPC_ServerReportVictory(winner);
    }

    // ===================== RPCs =====================

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ServerReportKill(PlayerRef killer, PlayerRef victim)
    {
        if (!HasServerAuth()) return;
        if (killer == default || killer.IsNone) { VLog("Kill ignoré: killer invalide."); return; }
        if (!victim.IsNone && killer == victim)  { VLog("Kill ignoré: self-kill."); return; }

        var killerPR = PlayerRanking.FindByPlayerRef(killer);
        if (killerPR != null)
        {
            killerPR.RPC_ServerRegisterKill();
            VLog($"Kill → +points à {killer}.");
        }
        else VLog($"[WARN] Killer PR introuvable pour {killer}.");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ServerReportVictory(PlayerRef winner)
    {
        if (!HasServerAuth()) return;
        if (winner == default || winner.IsNone) return;
        PlayerRanking.AwardVictoryTo(winner);
        VLog($"Victoire → +points à {winner}.");
    }

    void VLog(string msg) { if (verbose) Debug.Log("[RankingHub] " + msg); }
}
