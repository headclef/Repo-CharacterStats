# Character Stats

A lightweight [BepInEx](https://github.com/BepInEx/BepInEx) library mod for **R.E.P.O.** that reads and caches all player character stats. Designed as a shared dependency for other mods that need access to upgrade levels.

## What This Mod Does

- **Reads** all 12 character upgrade stats from the game's `StatsManager`
- **Caches** them in a simple, accessible API for other mods to consume
- **Refreshes** automatically after level generation and network sync
- **Does NOT modify** any stats — this is a **read-only** information provider

> This mod does **nothing** on its own. It exists so that other mods (like [Increase Tumble Damage](https://github.com/headclef/Repo-IncreaseTumbleDamage)) can reliably access player stat data without conflicting with each other.

## Public API

Other mods can reference `Character Stats.dll` and call these static methods:

```csharp
using static Character_Stats.Character_Stats;

// Get a specific upgrade level for a player
int launchLevel = GetUpgradeLevel(steamId, "Launch");

// Get all upgrades for a player
Dictionary<string, int> upgrades = GetAllUpgrades(steamId);

// Check if stats have been read this level
if (AreStatsReady) { ... }

// Get all tracked player Steam IDs
foreach (var id in GetTrackedPlayers()) { ... }

// Get local player's Steam ID
string? myId = GetLocalSteamId();
```

### Available Upgrade Keys

`Health`, `Speed`, `Map Player Count`, `Stamina`, `Extra Jump`, `Range`, `Strength`, `Throw`, `Launch`, `Crouch Rest`, `Tumble Wings`, `Tumble Climb`, `Death Head`

## Timing

Stats are read at these points:
1. **1 second after level generation** completes — ensures all mods (leveling, upgrade sharing, etc.) have finished applying their upgrades
2. **After `ReceiveSyncData`** — re-reads stats when network sync overwrites data (e.g., when another mod shares upgrades)
3. **On progress reset** — clears cached stats

## Requirements

- [BepInEx 5.x](https://github.com/BepInEx/BepInEx) installed for R.E.P.O.

## Installation

1. Download the latest release.
2. Place `Character Stats.dll` into your `BepInEx/plugins` folder.
3. Launch R.E.P.O. — the mod will load and begin reading stats automatically.

## For Mod Developers

To use Character Stats as a dependency in your mod:

1. Reference `Character Stats.dll` in your `.csproj` (compile-only, not bundled):
```xml
<Reference Include="Character Stats">
  <HintPath>path\to\Character Stats.dll</HintPath>
  <Private>false</Private>
</Reference>
```

2. Add the BepInEx dependency attribute:
```csharp
[BepInDependency("headclef.CharacterStats", BepInDependency.DependencyFlags.HardDependency)]
```

3. Add the Thunderstore dependency in your `manifest.json`:
```json
"dependencies": ["headclef-CharacterStats-1.0.0"]
```

## Project Structure
```
├── Character Stats.cs              # Plugin entry point & public API
├── Patches/
│   └── StatReaderPatch.cs          # Harmony hooks for reading stats
└── README.md
```

## Building
```bash
dotnet build
```

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
