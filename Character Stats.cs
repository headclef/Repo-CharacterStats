using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Character_Stats;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Character_Stats : BaseUnityPlugin
{
    private const string PluginGuid = "headclef.CharacterStats";
    private const string PluginName = "Character Stats";
    private const string PluginVersion = "1.0.0";

    internal static Character_Stats Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    // ── Cached stat data (read-only, refreshed automatically) ──
    private static readonly Dictionary<string, Dictionary<string, int>> _playerStats = new();
    private static bool _statsReady;

    private void Awake()
    {
        Instance = this;
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = UnityEngine.HideFlags.HideAndDontSave;

        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();

        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }

    // ══════════════════════════════════════════════════════
    //  PUBLIC API — Other mods can call these static methods
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// Returns true when stats have been read at least once this level.
    /// </summary>
    public static bool AreStatsReady => _statsReady;

    /// <summary>
    /// Get a specific upgrade level for a player.
    /// Returns 0 if not found.
    /// </summary>
    public static int GetUpgradeLevel(string steamId, string upgradeKey)
    {
        if (_playerStats.TryGetValue(steamId, out var upgrades) &&
            upgrades.TryGetValue(upgradeKey, out int level))
        {
            return level;
        }
        return 0;
    }

    /// <summary>
    /// Get all upgrade levels for a player.
    /// Returns an empty dictionary if not found.
    /// </summary>
    public static Dictionary<string, int> GetAllUpgrades(string steamId)
    {
        if (_playerStats.TryGetValue(steamId, out var upgrades))
            return new Dictionary<string, int>(upgrades);
        return new Dictionary<string, int>();
    }

    /// <summary>
    /// Get all tracked player Steam IDs.
    /// </summary>
    public static IEnumerable<string> GetTrackedPlayers()
    {
        return _playerStats.Keys;
    }

    /// <summary>
    /// Get the local player's Steam ID.
    /// Returns null if not available.
    /// </summary>
    public static string? GetLocalSteamId()
    {
        return PlayerController.instance?.playerSteamID;
    }

    // ── Internal: called by StatReaderPatch to refresh cached data ──

    internal static void RefreshStats()
    {
        _playerStats.Clear();

        var players = SemiFunc.PlayerGetAll();
        if (players == null || players.Count == 0)
        {
            _statsReady = false;
            return;
        }

        foreach (var player in players)
        {
            string steamId = SemiFunc.PlayerGetSteamID(player);
            var upgrades = StatsManager.instance.FetchPlayerUpgrades(steamId);

            if (upgrades != null)
            {
                _playerStats[steamId] = new Dictionary<string, int>(upgrades);

                Logger.LogDebug($"Stats for {steamId}: {string.Join(", ", upgrades)}");
            }
        }

        _statsReady = true;
        Logger.LogInfo($"Character stats refreshed for {_playerStats.Count} player(s).");
    }

    internal static void ClearStats()
    {
        _playerStats.Clear();
        _statsReady = false;
    }
}
