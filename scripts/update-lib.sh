#!/bin/bash
# Usage: bash scripts/update-lib.sh
# Copies reference DLLs from game dir to lib/ for CI builds.
# DLL list is auto-extracted from Directory.Build.props — no manual sync needed.
# Reads GamePath from GamePath.user.props.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
PROPS_FILE="$PROJECT_DIR/GamePath.user.props"
BUILD_PROPS="$PROJECT_DIR/Directory.Build.props"

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

# Auto-extract DLL lists from Directory.Build.props HintPath entries
echo "Reading DLL references from Directory.Build.props..."

echo "Copying BepInEx core DLLs..."
grep -o '$(BepInExPath)\\[^<]*\.dll' "$BUILD_PROPS" | sed 's/.*\\//' | while read -r dll; do
  cp "$BEPINEX_CORE/$dll" "$LIB/bepinex/"
  echo "  $dll"
done

echo "Copying interop DLLs..."
grep -o '$(InteropPath)\\[^<]*\.dll' "$BUILD_PROPS" | sed 's/.*\\//' | while read -r dll; do
  cp "$INTEROP/$dll" "$LIB/interop/"
  echo "  $dll"
done

echo "Done. Run 'git add lib/' to stage changes."
