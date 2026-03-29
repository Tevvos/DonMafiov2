using UnityEngine;
using System.Linq;

public class LeaderTracker : MonoBehaviour
{
    [SerializeField, Tooltip("Kills min pour afficher WANTED")]
    private int minKillsForWanted = 1;

    // FIX PERF : intervalle de refresh du cache (en secondes)
    [SerializeField, Tooltip("Fréquence de scan des joueurs (secondes). 1s est largement suffisant.")]
    private float cacheRefreshInterval = 1f;

    public static int MinKillsForWanted { get; private set; } = 1;
    public static PlayerRanking CurrentLeader { get; private set; }

    // FIX PERF : cache de la liste des joueurs
    private PlayerRanking[] _cachedPlayers = new PlayerRanking[0];
    private float _cacheTimer = 0f;

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
        // FIX PERF : refresh du cache seulement toutes les N secondes
        // au lieu d'un FindObjectsOfType à chaque frame (très coûteux en réseau)
        _cacheTimer += Time.deltaTime;
        if (_cacheTimer >= cacheRefreshInterval)
        {
            _cacheTimer = 0f;
            _cachedPlayers = FindObjectsOfType<PlayerRanking>();
        }

        if (_cachedPlayers.Length == 0)
        {
            SetLeader(null);
            return;
        }

        var top = _cachedPlayers.OrderByDescending(p => p.killCount).FirstOrDefault();

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
