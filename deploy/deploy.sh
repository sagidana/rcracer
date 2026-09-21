#!/usr/bin/env bash
# Build the dedicated Linux server and deploy it to the remote server as a systemd service.
#
#   deploy/deploy.sh              deploy to `ssh game` (the preconfigured host)
#   deploy/deploy.sh myhost       deploy to `ssh myhost` instead
#   deploy/deploy.sh game -track=Desert   change the default track the service hosts (edit the
#                                          .service file's ExecStart afterwards to make it stick)
#
# Requires the "Linux Dedicated Server Build Support" module installed via Unity Hub, and the repo
# checked out on a Windows drive (Unity.exe cannot open a project on the WSL filesystem - see
# build.sh/README.md for the same limitation on the client build).
#
# Requires passwordless (key-based) SSH to the target already working, i.e. `ssh <host>` logs in with
# no prompt, and that user has sudo (remote_install.sh installs a systemd unit).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST="${1:-game}"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
OUT="$ROOT/Build/LinuxServer"
LOG="$ROOT/Build/deploy-build.log"
mkdir -p "$ROOT/Build" "$OUT"

case "$ROOT" in
    /mnt/*) ;;   # a Windows drive: Unity.exe can open it directly
    *) echo "deploy.sh must run from a repo checked out on a Windows drive (e.g. /mnt/c/...)." >&2
       echo "See README.md - Unity.exe refuses a project on the WSL filesystem." >&2
       exit 1 ;;
esac

LINUX_HUB="$HOME/Unity/Hub/Editor"
WIN_HUB="/mnt/c/Program Files/Unity/Hub/Editor"

find_unity() {
    if [ -n "${UNITY:-}" ]; then echo "$UNITY"; return; fi
    if [ -x "$LINUX_HUB/$VERSION/Editor/Unity" ]; then echo "$LINUX_HUB/$VERSION/Editor/Unity"; return; fi
    if [ -x "$WIN_HUB/$VERSION/Editor/Unity.exe" ]; then echo "$WIN_HUB/$VERSION/Editor/Unity.exe"; return; fi
    local major="${VERSION%%.*}" dir
    while IFS= read -r dir; do
        [ -n "$dir" ] || continue
        if [ -x "$dir/Editor/Unity" ]; then echo "$dir/Editor/Unity"; return; fi
        if [ -x "$dir/Editor/Unity.exe" ]; then echo "$dir/Editor/Unity.exe"; return; fi
    done < <(ls -d "$LINUX_HUB"/"$major".* "$WIN_HUB"/"$major".* 2>/dev/null | sort -rV)
    echo ""
}

UNITY_BIN="$(find_unity)"
if [ -z "$UNITY_BIN" ]; then
    echo "Unity $VERSION not found. Install it with Unity Hub, or run: UNITY=/path/to/Unity deploy/deploy.sh" >&2
    exit 1
fi

PROJECT_ARG="$ROOT"; OUT_ARG="$OUT"; LOG_ARG="$LOG"
case "$UNITY_BIN" in
    *.exe) PROJECT_ARG="$(wslpath -w "$ROOT")"; OUT_ARG="$(wslpath -w "$OUT")"; LOG_ARG="$(wslpath -w "$LOG")" ;;
esac

echo "Unity:   $UNITY_BIN"
echo "Target:  ssh $HOST"
echo "Building the Linux dedicated server..."
set +e
"$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$PROJECT_ARG" \
    -executeMethod BuildScript.BuildLinuxServer \
    -buildPath "$OUT_ARG" \
    -logFile "$LOG_ARG"
STATUS=$?
set -e
grep -E 'BUILD (OK|FAILED)|error CS|Error building|not supported' "$LOG" || true
if [ "$STATUS" -ne 0 ] || [ ! -x "$OUT/RCRACE-server" ]; then
    echo "Build failed (exit $STATUS). See $LOG" >&2
    echo "If the log mentions the target/module: install 'Linux Dedicated Server Build Support' via Unity Hub." >&2
    exit 1
fi

echo "Uploading to $HOST ..."
STAGE="rcracer-deploy-$(date +%s)"
ssh "$HOST" "mkdir -p /tmp/$STAGE"
rsync -a "$OUT/" "$HOST:/tmp/$STAGE/"
scp -q "$ROOT/deploy/rcracer-server.service" "$ROOT/deploy/remote_install.sh" "$HOST:/tmp/$STAGE/"

echo "Installing as a systemd service on $HOST ..."
ssh "$HOST" "chmod +x /tmp/$STAGE/remote_install.sh && /tmp/$STAGE/remote_install.sh /tmp/$STAGE && rm -rf /tmp/$STAGE"

echo
echo "Deployed. Server: ssh $HOST 'journalctl -u rcracer-server -f'"
echo
echo "Firewall: open inbound UDP port 7777 (game traffic, see NetConfig.cs) on $HOST."
echo "  ufw:      sudo ufw allow 7777/udp"
echo "  firewalld: sudo firewall-cmd --permanent --add-port=7777/udp && sudo firewall-cmd --reload"
echo "  iptables:  sudo iptables -A INPUT -p udp --dport 7777 -j ACCEPT"
echo "If $HOST sits behind a cloud provider's own firewall/security group (Hetzner, AWS, ...), open"
echo "UDP 7777 there too - the host-level rule above is not enough on its own."
