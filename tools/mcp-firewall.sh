#!/usr/bin/env bash
# Open / close the Windows firewall for the MCP servers this WSL setup talks to.
#
#   tools/mcp-firewall.sh open     allow inbound TCP 8080 (Unity MCP) and 9877 (Blender MCP),
#                                  and forward 0.0.0.0:9877 -> 127.0.0.1:9876 (the Blender addon
#                                  listens on localhost:9876 only; WSL connects to 9877)
#   tools/mcp-firewall.sh close    remove those rules and the port forward
#   tools/mcp-firewall.sh status   show what is currently there (no admin needed)
#
#   PORTS="8080 9876 3118" tools/mcp-firewall.sh open      other ports
#
# open/close launch an elevated PowerShell: expect a UAC prompt on the Windows side.
set -euo pipefail

ACTION="${1:-status}"
PORTS="${PORTS:-8080 9877}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PS1_WIN="$(wslpath -w "$HERE/mcp-firewall.ps1")"
PORTS_PS="$(echo "$PORTS" | tr ' ' ',')"
LOG_WIN="$(cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r')\\wsl-mcp-firewall.log"
LOG="$(wslpath -u "$LOG_WIN")"

case "$ACTION" in
    open|close)
        : > "$LOG"
        powershell.exe -NoProfile -Command \
            "Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','\"$PS1_WIN\"','-Action','$ACTION','-Ports','$PORTS_PS'"
        cat "$LOG"
        ;;
    status)
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PS1_WIN" -Action status | tr -d '\r'
        ;;
    *)
        echo "usage: $0 open | close | status" >&2
        exit 1
        ;;
esac
