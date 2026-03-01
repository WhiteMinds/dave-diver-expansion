# DaveDiverExpansion

[![NexusMods](https://img.shields.io/badge/NexusMods-DaveDiverExpansion-orange?logo=nexusmods)](https://www.nexusmods.com/davethediver/mods/20)
[![GitHub Release](https://img.shields.io/github/v/release/WhiteMinds/dave-diver-expansion)](https://github.com/WhiteMinds/dave-diver-expansion/releases)

A mod for **Dave the Diver** built on BepInEx 6 + HarmonyX.

## Features

- **Auto Pickup** — Automatically collects nearby fish, items, and chests while diving
  - Configurable pickup radius
  - Toggle fish / items / chests independently
  - Smart filtering: skips weapons to prevent swap loops
  - Pauses during cutscenes/scenarios with cooldown to prevent quest-breaking pickups
- **Dive Map** — Minimap HUD and full-level map overlay while diving
  - Minimap in configurable screen corner, follows the player with adjustable zoom
  - Press M to toggle a full-level enlarged map — scroll wheel to zoom toward cursor, mouse drag to pan
  - Distinct marker shapes: aggressive fish (red triangle), ores (pink diamond), chests (square), escape pods (diamond), normal fish/catchable fish (circle)
  - Ore and mining node markers on the map
  - Distant fish markers — shows fish streamed out by the game, frozen at last known position
  - Big map legend panel and operation instructions
  - Independent toggles for normal / aggressive / catchable fish markers
  - Minimap markers scale with zoom level for consistent visibility
  - ESC closes big map; map hides during cutscenes
  - Auto-disables in Merfolk Village (which has its own map)
- **iDiver Extension** — Custom upgrade options in the iDiver upgrade panel (disabled by default)
  - Harpoon Damage Enhancement — increase harpoon damage per level
  - Movement Speed Enhancement — boost player swim speed per level
  - Booster Speed Enhancement — increase booster speed per level
  - Booster Duration Enhancement — extend booster duration per level
  - Upgrade levels persist even when the feature is disabled
- **Auto Seahorse Race** — Automatically controls your seahorse during races (disabled by default)
  - Auto acceleration — maintains optimal gauge speed (avoids overrun)
  - Auto dodge — detects obstacles and triggers jump/crawl with precise timing
  - Auto tag — uses tag input for speed boosts and relay handoffs (waits for Perfect timing)
- **Betting Expansion** — Expands casino mini-game betting options from 10/50/100 to 10/50/100/500/1000/5000
  - Applies to all standard casino games: Match Game, Hermit Crab, Shark Teeth
  - Does not affect Balatro (which has its own betting system)
- **Quick Scene Switch** — Press F2 to open the scene-switch menu from anywhere (no need to walk to the exit)
  - Works in any non-dive scene (Lobby, Sushi Bar, Farm, etc.)
  - Same menu as the native exit trigger, with all unlocked destinations
- **In-Game Config Panel** — Press F1 to open a settings UI overlay
  - Auto-discovers all config entries from all features
  - Toggle, slider, dropdown, and text input controls based on value type
  - In-game key rebinding — click any hotkey button and press a new key
  - Hover any setting row to see its full description
  - Scroll wheel support and "Reset All Settings" button
  - Changes take effect immediately

## Installation (Players)

1. Download [BepInEx 6 Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be) — select **Unity.IL2CPP-win-x64**
2. Extract to your game folder: `Steam\steamapps\common\Dave the Diver\`
3. Download `DaveDiverExpansion-vX.Y.Z.zip` from [NexusMods](https://www.nexusmods.com/davethediver/mods/20) or [GitHub Releases](https://github.com/WhiteMinds/dave-diver-expansion/releases)
4. Extract the zip into the same game folder (it creates `BepInEx\plugins\DaveDiverExpansion\`)
5. Launch the game

### 3. Configuration

Press **F1** in-game to open the built-in settings panel. All settings can be adjusted live and are saved automatically.

Alternatively, edit the config file directly:
```
Dave the Diver\BepInEx\config\com.davediver.expansion.cfg
```

#### Settings

**ConfigUI**

| Setting | Default | Description |
|---------|---------|-------------|
| `ToggleKey` | `F1` | Key to open/close the config panel |

**QuickSceneSwitch**

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable quick scene switching |
| `ToggleKey` | `F2` | Key to open the scene-switch menu |

**AutoPickup**

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Master toggle for auto-pickup |
| `AutoPickupFish` | `false` | Auto-collect dead fish |
| `AutoPickupItems` | `true` | Auto-collect dropped items |
| `AutoOpenChests` | `false` | Auto-open treasure chests |
| `PickupRadius` | `1.0` | Collection radius (game units) |

**DiveMap**

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Enable the dive map HUD |
| `ToggleKey` | `M` | Key to toggle the enlarged map view |
| `MiniMapEnabled` | `true` | Show the minimap overlay during diving |
| `MiniMapPosition` | `TopRight` | Screen corner for the minimap |
| `MiniMapOffsetX` | `16` | Minimap horizontal offset from screen edge (0–500) |
| `MiniMapOffsetY` | `16` | Minimap vertical offset from screen edge (0–500) |
| `MapSize` | `0.3` | Minimap size (fraction of screen height) |
| `MiniMapZoom` | `3.0` | Minimap zoom level |
| `MapOpacity` | `0.8` | Map opacity |
| `ShowEscapePods` | `true` | Show escape pod/mirror markers |
| `ShowOres` | `true` | Show ore/mineral markers |
| `ShowFish` | `false` | Show normal fish markers (non-aggressive, non-catchable) |
| `ShowAggressiveFish` | `true` | Show aggressive fish markers (e.g. sharks, piranhas) |
| `ShowCatchableFish` | `true` | Show catchable fish markers (e.g. shrimp, seahorse) |
| `ShowDistantFish` | `false` | Show markers for distant streamed-out fish |
| `ShowItems` | `false` | Show item markers |
| `ShowChests` | `false` | Show chest markers |
| `ShowCrabTraps` | `true` | Show fish trap spot markers |
| `MarkerScale` | `1.0` | Scale multiplier for all map markers (0.5–3x) |

**iDiverExtension**

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `false` | Enable extra iDiver upgrade options (harpoon damage, move speed, booster speed, booster duration, crab trap count, crab trap efficiency, drone count) |

**AutoSeahorseRace**

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `false` | Automatically control seahorse during racing |

**BettingExpansion**

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Expand casino mini-game betting from 10/50/100 to 10/50/100/500/1000/5000 |

---

## Development Setup

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (6.0+)
- [Git](https://git-scm.com/)
- [ilspycmd](https://www.nuget.org/packages/ilspycmd) — `dotnet tool install -g ilspycmd`
- Dave the Diver (Steam)

### Getting Started

```bash
# Clone (requires Git LFS for reference DLLs)
git lfs install
git clone https://github.com/WhiteMinds/dave-diver-expansion.git
cd dave-diver-expansion

# Create local game path config (edit the path to match your installation)
cat > GamePath.user.props << 'EOF'
<Project>
  <PropertyGroup>
    <GamePath>C:\Program Files (x86)\Steam\steamapps\common\Dave the Diver</GamePath>
  </PropertyGroup>
</Project>
EOF

# Install BepInEx 6 to the game directory
bash scripts/setup-bepinex.sh

# Launch the game once to generate interop DLLs, then close it

# Build (auto-deploys to game directory)
dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj
```

### Project Structure

```
.github/workflows/release.yml     # CI: build + GitHub Release on v* tags
Directory.Build.props              # Build config: references, auto-deploy (in git)
GamePath.user.props                # Your local game path (NOT in git)
lib/                               # Reference DLLs for CI builds (Git LFS)
scripts/
  setup-bepinex.sh                 # Auto-download & install BepInEx
  update-lib.sh                    # Copy reference DLLs from game dir to lib/
src/DaveDiverExpansion/
  Plugin.cs                        # BepInEx entry point (BasePlugin)
  Features/                        # Feature modules (config + Harmony patches per file)
  Helpers/                         # Shared utilities (IL2CPP reflection, etc.)
```

### Build & Test Cycle

```bash
# Build → auto-deploy → launch game → check logs
dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj

# View BepInEx log after running the game
cat "<GamePath>/BepInEx/LogOutput.log"
```

### Decompiling Game Code

Use `ilspycmd` to explore game internals when developing new patches:

```bash
# Search for a class by name
ilspycmd -l type "<GamePath>/BepInEx/interop/Assembly-CSharp.dll" | grep -i "keyword"

# Decompile a specific class
ilspycmd -t PlayerCharacter "<GamePath>/BepInEx/interop/Assembly-CSharp.dll"

# List public methods only
ilspycmd -t PlayerCharacter "<GamePath>/BepInEx/interop/Assembly-CSharp.dll" | grep "public unsafe void [A-Z]"
```

### Adding a New Feature

1. Decompile target classes with `ilspycmd` to find patch targets
2. Create `src/DaveDiverExpansion/Features/YourFeature.cs` with:
   - A static class with `ConfigEntry` bindings and `Init(ConfigFile)` method
   - `[HarmonyPatch]` classes in the same file
3. Call `YourFeature.Init(Config)` in `Plugin.cs` → `Load()`
4. Add any new interop references to `Directory.Build.props`
5. `dotnet build` → launch game → verify in logs

## Releasing

```bash
# 1. Update PLUGIN_VERSION in Plugin.cs
# 2. Commit and tag
git tag v1.0.0
git push origin main --tags
# 3. GitHub Actions builds and creates a Release with the zip
#    and auto-uploads to NexusMods (if NEXUSMODS_FILE_ID is set)
```

### NexusMods Auto-Upload

The CI workflow automatically uploads new versions to NexusMods when:
1. The repository variable `NEXUSMODS_FILE_ID` is set (the numeric file ID from NexusMods)
2. The repository secret `NEXUSMODS_API_KEY` is configured

Set these in GitHub repo Settings > Secrets and variables > Actions.

After a game update, refresh the reference DLLs used for CI builds:

```bash
bash scripts/update-lib.sh
git add lib/
git commit -m "Update reference DLLs for game version X.Y.Z"
git push
```

## Technical Notes

- Dave the Diver uses **IL2CPP** compilation (Unity 6000.0.52f1), not Mono
- Game types are accessed through BepInEx-generated interop DLLs in `BepInEx/interop/`
- The plugin targets `net480` and is loaded by BepInEx's .NET 6 runtime via compatibility layer
- Harmony patches target interop wrapper methods (use `typeof(GameClass)` directly)
- All interaction classes (fish, items, chests) use `CheckAvailableInteraction()` + `SuccessInteract()` pattern
- CI builds use reference DLLs from `lib/` (tracked via Git LFS) when `GamePath` is not set
- Reference: [devopsdinosaur/dave-the-diver-mods](https://github.com/devopsdinosaur/dave-the-diver-mods)

## License

MIT
