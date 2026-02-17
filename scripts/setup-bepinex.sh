#!/bin/bash
# Setup BepInEx 6 Bleeding Edge (IL2CPP) for Dave the Diver
# Usage: bash scripts/setup-bepinex.sh [game_path]

set -e

# Resolve game path
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Try to read GamePath from GamePath.user.props
if [ -f "$PROJECT_ROOT/GamePath.user.props" ]; then
    GAME_PATH=$(grep -oP '(?<=<GamePath>).*(?=</GamePath>)' "$PROJECT_ROOT/GamePath.user.props" | head -1)
fi

# Override with argument if provided
if [ -n "$1" ]; then
    GAME_PATH="$1"
fi

if [ -z "$GAME_PATH" ]; then
    echo "ERROR: Game path not found."
    echo "Either:"
    echo "  1. Create GamePath.user.props in project root"
    echo "  2. Pass game path as argument: bash scripts/setup-bepinex.sh \"C:/path/to/game\""
    exit 1
fi

# Normalize path for MSYS/Git Bash on Windows
GAME_PATH=$(echo "$GAME_PATH" | sed 's|\\|/|g')

echo "Game path: $GAME_PATH"

# Verify game directory
if [ ! -f "$GAME_PATH/DaveTheDiver.exe" ]; then
    echo "ERROR: DaveTheDiver.exe not found in $GAME_PATH"
    exit 1
fi

# Check if BepInEx is already installed
if [ -d "$GAME_PATH/BepInEx/core" ]; then
    echo "BepInEx already appears to be installed at $GAME_PATH/BepInEx"
    echo "To reinstall, delete the BepInEx folder first."

    if [ -d "$GAME_PATH/BepInEx/interop" ]; then
        echo "Interop DLLs found. Ready for development."
    else
        echo ""
        echo "WARNING: Interop DLLs not found."
        echo "Please launch the game once to generate them."
    fi
    exit 0
fi

# BepInEx 6 BE download
BEPINEX_VERSION="6.0.0-be.753+0d275a4"
BEPINEX_URL="https://builds.bepinex.dev/projects/bepinex_be/753/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.753%2B0d275a4.zip"
TEMP_ZIP="/tmp/bepinex-be.zip"

echo ""
echo "Downloading BepInEx $BEPINEX_VERSION ..."
curl -L -o "$TEMP_ZIP" "$BEPINEX_URL"

if [ ! -f "$TEMP_ZIP" ] || [ ! -s "$TEMP_ZIP" ]; then
    echo "ERROR: Download failed."
    exit 1
fi

echo "Extracting to $GAME_PATH ..."
unzip -o "$TEMP_ZIP" -d "$GAME_PATH"

echo "Cleaning up..."
rm -f "$TEMP_ZIP"

echo ""
echo "========================================"
echo "  BepInEx 6 BE installed successfully!"
echo "========================================"
echo ""
echo "NEXT STEPS:"
echo "  1. Launch Dave the Diver once"
echo "  2. Wait for the game to fully load (BepInEx generates interop DLLs)"
echo "  3. Close the game"
echo "  4. Verify: ls \"$GAME_PATH/BepInEx/interop/Assembly-CSharp.dll\""
echo "  5. Build your mod: dotnet build src/DaveDiverExpansion/DaveDiverExpansion.csproj"
echo ""
