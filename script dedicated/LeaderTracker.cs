using UnityEngine;
using System.Linq;

public class LeaderTracker : MonoBehaviour
{
    [SerializeField, Tooltip("Kills min pour afficher WANTED")]
    private int minKillsForWanted = 1;

    public static int MinKillsForWanted { get; private set; } = 1;
    public static PlayerRanking CurrentLeader { get; private set; }

    private void Awake()
    {
        ApplyMinKills();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyMinKills();
    }
#endif

    private void ApplyMinKills()
    {
        if (minKillsForWanted < 1) minKillsForWanted = 1;
        MinKillsForWanted = minKillsForWanted;
    }

    private void Update()
    {
        var allPlayers = FindObjectsOfType<PlayerRanking>();
        if (allPlayers.Length == 0)
        {
            SetLeader(null);
            return;
        }

        var top = allPlayers.OrderByDescending(p => p.killCount).FirstOrDefault();

        if (top == null || top.killCount < MinKillsForWanted)
        {
            SetLeader(null);
            return;
        }

        if (top != CurrentLeader)
        {
            SetLeader(top);
        }
    }

    private void SetLeader(PlayerRanking newLeader)
    {
        if (CurrentLeader == newLeader) return;
        CurrentLeader = newLeader;
    }
}
