using Fusion;
using UnityEngine;

/// <summary>
/// Hub d'autorité serveur pour le scoring (kills & victoire).
/// - À placer dans la scène GameScene01 sur un GameObject avec NetworkObject.
/// - Centralise: crédit des kills (toujours côté serveur), attribution des points de victoire.
/// - Fonctionne même si l'événement vient de la victime (côté StateAuthority de la victime).
/// </summary>
[DisallowMultipleComponent]
public class GameSceneRankingHub : NetworkBehaviour
{
    public static GameSceneRankingHub Instance;

    [Header("Logs")]
    [SerializeField] private bool verbose = true;

    void Awake() => Instance = this;

    bool HasServerAuth()
    {
        // On considère "serveur" = StateAuthority de CE hub ou Runner.IsServer
        if (Object && Object.HasStateAuthority) return true;
        if (Runner && Runner.IsServer) return true;
        return false;
    }

    // ==================== API PUBLIQUE (static) ====================

    /// <summary>
    /// Appelle ceci quand une mort est confirmée.
    /// </summary>
    public static void ReportKill(PlayerRef killer, PlayerRef victim)
    {
        if (!Instance) { Debug.LogWarning("[RankingHub] Instance absente dans la scène."); return; }

        // On délègue via RPC pour être CERTAIN que le traitement se fasse côté serveur.
        Instance.RPC_ServerReportKill(killer, victim);
    }

    /// <summary>
    /// Appelle ceci quand un gagnant est déterminé.
    /// </summary>
    public static void ReportVictory(PlayerRef winner)
    {
        if (!Instance) { Debug.LogWarning("[RankingHub] Instance absente dans la scène."); return; }
        Instance.RPC_ServerReportVictory(winner);
    }

    // ==================== RPC vers autorité serveur ====================

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ServerReportKill(PlayerRef killer, PlayerRef victim)
    {
        if (!HasServerAuth()) return;

        // Killer valide & pas self-kill
        if (killer == default || killer.IsNone) { VLog("Kill ignoré: killerRef invalide."); return; }
        if (!victim.IsNone && killer == victim) { VLog("Kill ignoré: killer==victim."); return; }

        // Trouve le ranking du TUEUR et crédite côté StateAuthority du TUEUR
        var killerPR = PlayerRanking.FindByPlayerRef(killer);
        if (killerPR != null)
        {
            killerPR.RPC_ServerRegisterKill(); // +1 kill + pointsPerKill via AddPoints côté StateAuthority du killer
            VLog($"Kill → +points à {killer}.");
        }
        else
        {
            VLog($"[WARN] Killer PR introuvable pour {killer}.");
        }

        // (Optionnel) Ici, tu peux appliquer une pénalité victime “simple”
        // var victimPR = PlayerRanking.FindByPlayerRef(victim);
        // if (victimPR != null) victimPR.RPC_ServerApplyDeathPenalty();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ServerReportVictory(PlayerRef winner)
    {
        if (!HasServerAuth()) return;
        if (winner == default || winner.IsNone) return;

        PlayerRanking.AwardVictoryTo(winner); // ajoute pointsOnVictory via RPC sur le PR du gagnant
        VLog($"Victoire → +points victoire à {winner}.");
    }

    // ==================== Utils ====================
    void VLog(string msg) { if (verbose) Debug.Log("[RankingHub] " + msg); }
}
