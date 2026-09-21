#!/usr/bin/env bash
# Build the Windows x64 player from WSL / Linux.
#
#   ./build.sh                 -> Build/Windows/RCRACE.exe
#   ./build.sh /some/out/dir   -> <dir>/RCRACE.exe
#   UNITY=/path/to/Unity ./build.sh   (override editor autodetect)
#   STAGE=/mnt/c/some/dir ./build.sh  (override the Windows-side staging folder, see below)
#
# Finds the Unity editor that matches ProjectSettings/ProjectVersion.txt (or, with a warning,
# the newest installed editor of the same major version):
#   1. $UNITY if set
#   2. Linux editor from Unity Hub:  ~/Unity/Hub/Editor/<version>/Editor/Unity
#   3. Windows editor through WSL interop: /mnt/c/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe
# The editor needs the "Windows Build Support (Mono)" module installed from Unity Hub.
#
# Windows editor + repo inside the WSL filesystem: Unity.exe refuses case-sensitive filesystems
# (and \\wsl.localhost is slow), so the project is first synced to a folder on the Windows drive
# (default %LOCALAPPDATA%\RCRACE-build), built there with a cached Library/, and the result copied back.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${1:-$ROOT/Build/Windows}"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
LOG="$ROOT/Build/build.log"
mkdir -p "$ROOT/Build" "$OUT"

LINUX_HUB="$HOME/Unity/Hub/Editor"
WIN_HUB="/mnt/c/Program Files/Unity/Hub/Editor"

# exact version first, then any editor of the same major version (newest first)
find_unity() {
    if [ -n "${UNITY:-}" ]; then echo "$UNITY"; return; fi
    if [ -x "$LINUX_HUB/$VERSION/Editor/Unity" ]; then echo "$LINUX_HUB/$VERSION/Editor/Unity"; return; fi
    if [ -x "$WIN_HUB/$VERSION/Editor/Unity.exe" ]; then echo "$WIN_HUB/$VERSION/Editor/Unity.exe"; return; fi
    local major="${VERSION%%.*}"
    local dir
    while IFS= read -r dir; do
        [ -n "$dir" ] || continue
        if [ -x "$dir/Editor/Unity" ]; then echo "$dir/Editor/Unity"; return; fi
        if [ -x "$dir/Editor/Unity.exe" ]; then echo "$dir/Editor/Unity.exe"; return; fi
    done < <(ls -d "$LINUX_HUB"/"$major".* "$WIN_HUB"/"$major".* 2>/dev/null | sort -rV)
    echo ""
}

UNITY_BIN="$(find_unity)"
if [ -z "$UNITY_BIN" ]; then
    echo "Unity $VERSION not found." >&2
    echo "Install it with Unity Hub (plus 'Windows Build Support (Mono)'), or run: UNITY=/path/to/Unity ./build.sh" >&2
    echo "Looked in: $LINUX_HUB/ and $WIN_HUB/" >&2
    exit 1
fi
case "$UNITY_BIN" in
    *"/$VERSION/"*) ;;
    *) echo "WARNING: project was made with Unity $VERSION, building with $UNITY_BIN" >&2
       echo "         Unity will upgrade the project to this version (ProjectSettings/ProjectVersion.txt changes)." >&2 ;;
esac

# ---- where does Unity see the project? ----
PROJECT="$ROOT"          # folder Unity opens (Linux path)
BUILD_DIR="$OUT"         # folder Unity writes the player to (Linux path)
STAGED=0
case "$UNITY_BIN" in
    *.exe)
        case "$ROOT" in
            /mnt/*) ;;   # already on a Windows drive: build in place
            *)
                STAGED=1
                if [ -z "${STAGE:-}" ]; then
                    LOCALAPPDATA_WIN="$(cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')"
                    STAGE="$(wslpath -u "$LOCALAPPDATA_WIN")/RCRACE-build"
                fi
                PROJECT="$STAGE/project"
                BUILD_DIR="$STAGE/Build/Windows"
                command -v rsync >/dev/null || { echo "rsync is needed to stage the project on the Windows drive: sudo apt install rsync" >&2; exit 1; }
                echo "Staging project to $PROJECT (Unity.exe cannot open projects on the WSL filesystem)"
                mkdir -p "$PROJECT" "$BUILD_DIR"
                for d in Assets Packages ProjectSettings; do
                    rsync -a --delete "$ROOT/$d/" "$PROJECT/$d/"
                done
                ;;
        esac
        ;;
esac

UNITY_LOG="$LOG"                                   # where Unity writes its log (Linux path)
[ "$STAGED" = 1 ] && UNITY_LOG="$STAGE/build.log"
: > "$UNITY_LOG"
PROJECT_ARG="$PROJECT"
OUT_ARG="$BUILD_DIR"
LOG_ARG="$UNITY_LOG"
case "$UNITY_BIN" in
    *.exe)
        PROJECT_ARG="$(wslpath -w "$PROJECT")"
        OUT_ARG="$(wslpath -w "$BUILD_DIR")"
        LOG_ARG="$(wslpath -w "$UNITY_LOG")"
        ;;
esac

echo "Unity:   $UNITY_BIN"
echo "Project: $PROJECT_ARG"
echo "Output:  $OUT_ARG"
echo "Building (the first run imports every asset and takes several minutes)..."

set +e
"$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$PROJECT_ARG" \
    -executeMethod BuildScript.BuildWindows \
    -buildPath "$OUT_ARG" \
    -logFile "$LOG_ARG"
STATUS=$?
set -e

if [ "$STAGED" = 1 ]; then
    cp -f "$UNITY_LOG" "$LOG"
    if [ "$STATUS" -eq 0 ]; then
        rsync -a --delete "$BUILD_DIR/" "$OUT/"
    fi
fi

echo "Log:     $LOG"
grep -E 'BUILD (OK|FAILED)|error CS|Error building|Fatal Error' "$LOG" || true
if [ "$STATUS" -ne 0 ]; then
    echo "Build failed (exit $STATUS). See $LOG" >&2
    exit "$STATUS"
fi
echo "Done: $OUT/RCRACE.exe  (zip the whole $OUT folder to share it)"
if [ "$STAGED" = 1 ]; then
    echo "Run it from the Windows drive, not through \\\\wsl.localhost (DLLs next to the exe do not load from there):"
    echo "  \"$BUILD_DIR/RCRACE.exe\""
fi
