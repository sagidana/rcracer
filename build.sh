#!/usr/bin/env bash
# Build the Windows x64 player from WSL / Linux.
#
#   ./build.sh                 -> Build/Windows/RCRACE.exe
#   ./build.sh /some/out/dir   -> <dir>/RCRACE.exe
#   UNITY=/path/to/Unity ./build.sh   (override editor autodetect)
#
# Finds the Unity editor that matches ProjectSettings/ProjectVersion.txt:
#   1. $UNITY if set
#   2. Linux editor from Unity Hub:  ~/Unity/Hub/Editor/<version>/Editor/Unity
#   3. Windows editor through WSL interop: /mnt/c/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe
# The editor needs the "Windows Build Support (Mono)" module installed from Unity Hub.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-$ROOT/Build/Windows}"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
LOG="$ROOT/Build/build.log"
mkdir -p "$ROOT/Build"

find_unity() {
    if [ -n "${UNITY:-}" ]; then echo "$UNITY"; return; fi
    local linux="$HOME/Unity/Hub/Editor/$VERSION/Editor/Unity"
    if [ -x "$linux" ]; then echo "$linux"; return; fi
    local win="/mnt/c/Program Files/Unity/Hub/Editor/$VERSION/Editor/Unity.exe"
    if [ -x "$win" ]; then echo "$win"; return; fi
    echo ""
}

UNITY_BIN="$(find_unity)"
if [ -z "$UNITY_BIN" ]; then
    echo "Unity $VERSION not found." >&2
    echo "Install it with Unity Hub (plus 'Windows Build Support (Mono)'), or run: UNITY=/path/to/Unity ./build.sh" >&2
    echo "Looked in: ~/Unity/Hub/Editor/$VERSION/Editor/Unity and /mnt/c/Program Files/Unity/Hub/Editor/$VERSION/Editor/Unity.exe" >&2
    exit 1
fi

# The Windows editor (through WSL interop) wants Windows-style paths.
PROJECT_ARG="$ROOT"
OUT_ARG="$OUT"
LOG_ARG="$LOG"
case "$UNITY_BIN" in
    *.exe)
        mkdir -p "$OUT"
        PROJECT_ARG="$(wslpath -w "$ROOT")"
        OUT_ARG="$(wslpath -w "$OUT")"
        LOG_ARG="$(wslpath -w "$LOG")"
        ;;
esac

echo "Unity:   $UNITY_BIN"
echo "Project: $PROJECT_ARG"
echo "Output:  $OUT_ARG"
echo "Log:     $LOG"
echo "Building (this takes a few minutes the first time)..."

set +e
"$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$PROJECT_ARG" \
    -executeMethod BuildScript.BuildWindows \
    -buildPath "$OUT_ARG" \
    -logFile "$LOG_ARG"
STATUS=$?
set -e

grep -E 'BUILD (OK|FAILED)|error CS|Error building' "$LOG" || true
if [ "$STATUS" -ne 0 ]; then
    echo "Build failed (exit $STATUS). See $LOG" >&2
    exit "$STATUS"
fi
echo "Done: $OUT/RCRACE.exe  (zip the whole $OUT folder to share it)"
