# DaveDiverExpansion

A mod for **Dave the Diver** built on BepInEx 6 + HarmonyX.

## Features

- **Auto Pickup** — Automatically collects nearby fish, items, and chests while diving
  - Configurable pickup radius
  - Toggle fish / items / chests independently
- **In-Game Config Panel** — Press F1 to open a settings UI overlay
  - Auto-discovers all config entries from all features
  - Toggle, slider, and text input controls based on value type
  - Changes take effect immediately

## Installation (Players)

### 1. Install BepInEx 6

1. Download [BepInEx 6 Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be) — select **Unity.IL2CPP-win-x64**
2. Extract to your game folder: `Steam\steamapps\common\Dave the Diver\`
3. Launch the game once and close it (BepInEx generates required files on first run)

### 2. Install the Mod

1. Download `DaveDiverExpansion.dll` from [Releases](../../releases)
2. Place it in `Dave the Diver\BepInEx\plugins\DaveDiverExpansion\`
3. Launch the game

### 3. Configuration

Press **F1** in-game to open the built-in settings panel. All settings can be adjusted live and are saved automatically.

Alternatively, edit the config file directly:
```
Dave the Diver\BepInEx\config\com.davediver.expansion.cfg
```

#### Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Master toggle for auto-pickup |
| `AutoPickupFish` | `true` | Auto-collect dead fish |
| `AutoPickupItems` | `true` | Auto-collect dropped items |
| `AutoOpenChests` | `true` | Auto-open treasure chests |
| `PickupRadius` | `5.0` | Collection radius (game units) |

---

## Development Setup

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (6.0+)
- [Git](https://git-scm.com/)
- [ilspycmd](https://www.nuget.org/packages/ilspycmd) — `dotnet tool install -g ilspycmd`
- Dave the Diver (Steam)

### Getting Started

```bash
# Clone
git clone https://github.com/YOUR_USERNAME/dave-diver-expansion.git
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
Directory.Build.props              # Build config: references, auto-deploy (in git)
GamePath.user.props                # Your local game path (NOT in git)
scripts/setup-bepinex.sh           # Auto-download & install BepInEx
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

## Technical Notes

- Dave the Diver uses **IL2CPP** compilation (Unity 6000.0.52f1), not Mono
- Game types are accessed through BepInEx-generated interop DLLs in `BepInEx/interop/`
- The plugin targets `net480` and is loaded by BepInEx's .NET 6 runtime via compatibility layer
- Harmony patches target interop wrapper methods (use `typeof(GameClass)` directly)
- All interaction classes (fish, items, chests) use `CheckAvailableInteraction()` + `SuccessInteract()` pattern
- Reference: [devopsdinosaur/dave-the-diver-mods](https://github.com/devopsdinosaur/dave-the-diver-mods)

## License

MIT
