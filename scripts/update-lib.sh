#!/bin/bash
# Usage: bash scripts/update-lib.sh
# Copies reference DLLs from game dir to lib/ for CI builds.
# Reads GamePath from GamePath.user.props.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PROPS_FILE="$PROJECT_DIR/GamePath.user.props"

if [ ! -f "$PROPS_FILE" ]; then
  echo "Error: GamePath.user.props not found at $PROPS_FILE"
  echo "Create it first — see README.md"
  exit 1
fi

# Extract GamePath value from XML
GAME_PATH=$(grep -oP '(?<=<GamePath>)[^<]+' "$PROPS_FILE")
if [ -z "$GAME_PATH" ]; then
  echo "Error: Could not parse GamePath from $PROPS_FILE"
  exit 1
fi

echo "Game path: $GAME_PATH"

BEPINEX_CORE="$GAME_PATH/BepInEx/core"
INTEROP="$GAME_PATH/BepInEx/interop"
LIB="$PROJECT_DIR/lib"

mkdir -p "$LIB/bepinex" "$LIB/interop"

# BepInEx core DLLs
BEPINEX_DLLS=(
  "0Harmony.dll"
  "BepInEx.Core.dll"
  "BepInEx.Unity.IL2CPP.dll"
  "Il2CppInterop.Runtime.dll"
)

# Interop DLLs
INTEROP_DLLS=(
  "Assembly-CSharp.dll"
  "Il2Cppmscorlib.dll"
  "UnityEngine.dll"
  "UnityEngine.CoreModule.dll"
  "UnityEngine.PhysicsModule.dll"
  "UnityEngine.Physics2DModule.dll"
  "UnityEngine.UI.dll"
  "UnityEngine.UIModule.dll"
  "UnityEngine.InputLegacyModule.dll"
  "UnityEngine.TextRenderingModule.dll"
)

echo "Copying BepInEx core DLLs..."
for dll in "${BEPINEX_DLLS[@]}"; do
  cp "$BEPINEX_CORE/$dll" "$LIB/bepinex/"
  echo "  $dll"
done

echo "Copying interop DLLs..."
for dll in "${INTEROP_DLLS[@]}"; do
  cp "$INTEROP/$dll" "$LIB/interop/"
  echo "  $dll"
done

echo "Done. Run 'git add lib/' to stage changes."
