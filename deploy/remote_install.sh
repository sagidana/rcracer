#!/usr/bin/env bash
# Runs ON the remote server (deploy.sh scp's this alongside the build and calls it over ssh).
# Installs/updates the RCRACE dedicated server as a systemd service.
set -euo pipefail

INSTALL_DIR=/opt/rcracer-server
STAGE_DIR="$1"   # where deploy.sh uploaded the fresh build + service file

sudo mkdir -p "$INSTALL_DIR"
sudo rsync -a --delete "$STAGE_DIR"/RCRACE-server_Data "$STAGE_DIR"/RCRACE-server "$INSTALL_DIR"/
sudo chmod +x "$INSTALL_DIR"/RCRACE-server
sudo cp "$STAGE_DIR"/rcracer-server.service /etc/systemd/system/rcracer-server.service

sudo systemctl daemon-reload
sudo systemctl enable rcracer-server
sudo systemctl restart rcracer-server
sleep 1
sudo systemctl --no-pager status rcracer-server || true
echo "remote_install.sh: done"
