using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Character_Stats;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Character_Stats : BaseUnityPlugin
{
    private const string PluginGuid = "headclef.CharacterStats";
    private const string PluginName = "Character Stats";
    private const string PluginVersion = "1.2.1";

    internal static Character_Stats Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    // ── Cached stat data (read-only, refreshed automatically) ──
    private static readonly Dictionary<string, Dictionary<string, int>> _playerStats = new();
    private static bool _statsReady;

    // ── Temporary bonus overlay (steamId → key → bonus) ──
    // A live, additive layer on top of the cached stat values, applied at READ time
    // in GetUpgradeLevel / GetAllUpgrades. Mods like Berserk register a transient
    // bonus here (e.g. +5 Strength while berserking) so every consumer (Armor,
    // Increase Tumble Damage, UI…) sees the boosted level instantly — WITHOUT writing
    // it into the game's StatsManager dictionaries. That matters: Improve reconciles
    // those dictionaries directly and never reads this overlay, so a temporary bonus
    // can never be absorbed into Improve's tracking or baked into the .es3 save. The
    // owning mod is responsible for clearing its bonus when the effect ends.
    private static readonly Dictionary<string, Dictionary<string, int>> _tempBonuses = new();

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
    /// Get a specific upgrade level for a player, including any temporary bonus
    /// registered via <see cref="SetTemporaryBonus"/>. Returns 0 if not found.
    /// </summary>
    public static int GetUpgradeLevel(string steamId, string upgradeKey)
    {
        int level = 0;
        if (_playerStats.TryGetValue(steamId, out var upgrades) &&
            upgrades.TryGetValue(upgradeKey, out int cached))
        {
            level = cached;
        }
        return level + GetTemporaryBonus(steamId, upgradeKey);
    }

    /// <summary>
    /// Get all upgrade levels for a player, including any temporary bonuses.
    /// Returns an empty dictionary if not found.
    /// </summary>
    public static Dictionary<string, int> GetAllUpgrades(string steamId)
    {
        var result = new Dictionary<string, int>();
        if (_playerStats.TryGetValue(steamId, out var upgrades))
            foreach (var kv in upgrades)
                result[kv.Key] = kv.Value;

        if (_tempBonuses.TryGetValue(steamId, out var bonuses))
            foreach (var kv in bonuses)
                result[kv.Key] = (result.TryGetValue(kv.Key, out int v) ? v : 0) + kv.Value;

        return result;
    }

    // ── Temporary bonus overlay API ──

    /// <summary>
    /// Register (or update) a temporary additive bonus for a player's upgrade key.
    /// The bonus is layered on top of the real value at read time and is NEVER written
    /// into the game's StatsManager dictionaries, so it stays invisible to Improve and
    /// is never saved. Pass <paramref name="amount"/> 0 to remove the bonus. The caller
    /// owns the lifecycle and must clear it when the effect ends.
    /// Use the SAME upgrade keys as <see cref="GetUpgradeLevel"/> (e.g. "Strength", "Launch").
    /// </summary>
    public static void SetTemporaryBonus(string steamId, string upgradeKey, int amount)
    {
        if (string.IsNullOrEmpty(steamId) || string.IsNullOrEmpty(upgradeKey))
            return;

        if (amount == 0)
        {
            ClearTemporaryBonus(steamId, upgradeKey);
            return;
        }

        if (!_tempBonuses.TryGetValue(steamId, out var bonuses))
        {
            bonuses = new Dictionary<string, int>();
            _tempBonuses[steamId] = bonuses;
        }
        bonuses[upgradeKey] = amount;
    }

    /// <summary>Remove a single temporary bonus.</summary>
    public static void ClearTemporaryBonus(string steamId, string upgradeKey)
    {
        if (_tempBonuses.TryGetValue(steamId, out var bonuses))
        {
            bonuses.Remove(upgradeKey);
            if (bonuses.Count == 0)
                _tempBonuses.Remove(steamId);
        }
    }

    /// <summary>Remove all temporary bonuses for a player.</summary>
    public static void ClearAllTemporaryBonuses(string steamId)
    {
        _tempBonuses.Remove(steamId);
    }

    /// <summary>Read the current temporary bonus for a key (0 if none).</summary>
    public static int GetTemporaryBonus(string steamId, string upgradeKey)
    {
        if (_tempBonuses.TryGetValue(steamId, out var bonuses) &&
            bonuses.TryGetValue(upgradeKey, out int v))
            return v;
        return 0;
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

    // ── Short consumer key → live StatsManager dictionary field name ──
    // Every consumer mod (Agility/Armor/Constitution/Increase Tumble Damage/UI)
    // queries by these short names. We read the live per-player dictionaries on
    // StatsManager directly (the SAME fields the game and stat-applying mods like
    // Improve write to), so a value applied locally — e.g. Improve raising
    // playerUpgradeLaunch — is immediately visible here. The game's own
    // FetchPlayerUpgrades aggregate can read a secondary/stale source (and throws
    // if any per-stat dict has no entry for a player yet), so it is NOT the source
    // of truth — see RefreshStats.
    private static readonly (string Key, string Field)[] _statKeyMap =
    {
        ("Health", "playerUpgradeHealth"),
        ("Stamina", "playerUpgradeStamina"),
        ("Speed", "playerUpgradeSpeed"),
        ("Strength", "playerUpgradeStrength"),
        ("Range", "playerUpgradeRange"),
        ("Throw", "playerUpgradeThrow"),
        ("Extra Jump", "playerUpgradeExtraJump"),
        ("Launch", "playerUpgradeLaunch"),
        ("Crouch Rest", "playerUpgradeCrouchRest"),
        ("Map Player Count", "playerUpgradeMapPlayerCount"),
        ("Tumble Climb", "playerUpgradeTumbleClimb"),
        ("Tumble Wings", "playerUpgradeTumbleWings"),
        ("Death Head Battery", "playerUpgradeDeathHeadBattery"),
    };

    private static readonly Dictionary<string, FieldInfo?> _dictFieldCache = new();

    private static Dictionary<string, int>? GetStatDict(string fieldName)
    {
        if (!_dictFieldCache.TryGetValue(fieldName, out var fi))
        {
            fi = typeof(StatsManager).GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _dictFieldCache[fieldName] = fi;
        }
        return fi?.GetValue(StatsManager.instance) as Dictionary<string, int>;
    }

    // private static float _lastDiagTime;  // TEMP diagnostic throttle (disabled — fix confirmed; needs `using UnityEngine;` if re-enabled)

    // ── Internal: called by StatReaderPatch to refresh cached data ──

    internal static void RefreshStats()
    {
        var sm = StatsManager.instance;
        if (sm == null) return;  // not ready — keep whatever we cached

        var players = SemiFunc.PlayerGetAll();
        if (players == null || players.Count == 0) return;  // keep previous cache

        var fresh = new Dictionary<string, Dictionary<string, int>>();

        foreach (var player in players)
        {
            string steamId = SemiFunc.PlayerGetSteamID(player);
            if (string.IsNullOrEmpty(steamId)) continue;

            var upgrades = new Dictionary<string, int>();

            // Best-effort: the game's aggregate (key naming may differ; guarded
            // because it throws if a per-stat dict lacks an entry for this player).
            try
            {
                var fetched = sm.FetchPlayerUpgrades(steamId);
                if (fetched != null)
                    foreach (var kv in fetched)
                        upgrades[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"FetchPlayerUpgrades threw for {steamId}: {ex.Message}");
            }

            // Authoritative overlay: live named dictionaries win, so Improve-applied
            // values are always reflected for every consumer key.
            foreach (var (key, field) in _statKeyMap)
            {
                var dict = GetStatDict(field);
                if (dict != null && dict.TryGetValue(steamId, out int v))
                    upgrades[key] = v;
            }

            fresh[steamId] = upgrades;
        }

        if (fresh.Count == 0) return;  // nothing valid this pass — keep previous cache

        // Swap contents only after every read succeeded, so a mid-refresh failure
        // never leaves consumers staring at an empty cache.
        _playerStats.Clear();
        foreach (var kv in fresh)
            _playerStats[kv.Key] = kv.Value;
        _statsReady = true;

        // TEMP DIAGNOSTIC (disabled — Improve/tumble fix confirmed). Re-enable (and
        // restore `using UnityEngine;`) to log what the cache reads each refresh.
        // if (Time.time - _lastDiagTime > 3f)
        // {
        //     _lastDiagTime = Time.time;
        //     string? localId = GetLocalSteamId();
        //     int launch = localId != null ? GetUpgradeLevel(localId, "Launch") : -1;
        //     Logger.LogInfo($"[CSDiag] refreshed {_playerStats.Count} player(s); local '{localId}' Launch={launch}");
        // }
    }

    internal static void ClearStats()
    {
        _playerStats.Clear();
        _statsReady = false;
    }
}
