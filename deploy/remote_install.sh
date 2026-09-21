#!/usr/bin/env bash
# Runs ON the remote server (deploy.sh scp's this alongside the build and calls it over ssh).
# Installs/updates the RCRACE dedicated server as a systemd service.
set -euo pipefail

INSTALL_DIR=/opt/rcracer-server
STAGE_DIR="$1"   # where deploy.sh uploaded the fresh build + service file

sudo mkdir -p "$INSTALL_DIR"
# sync the WHOLE build output, not a hand-picked subset: Unity ships loose runtime .so files
# (UnityPlayer.so, libdecor-*.so) as siblings of the executable, not inside RCRACE-server_Data,
# and a cherry-picked copy silently missed them ("error while loading shared libraries: UnityPlayer.so").
# Only exclude what deploy.sh staged alongside the build for this script's own use, and the debug-symbols
# folder Unity itself says not to ship.
sudo rsync -a --delete \
    --exclude 'rcracer-server.service' \
    --exclude 'remote_install.sh' \
    --exclude '*_BackUpThisFolder_ButDontShipItWithYourGame' \
    "$STAGE_DIR"/ "$INSTALL_DIR"/
sudo chmod +x "$INSTALL_DIR"/RCRACE-server
sudo cp "$STAGE_DIR"/rcracer-server.service /etc/systemd/system/rcracer-server.service

sudo systemctl daemon-reload
sudo systemctl enable rcracer-server
sudo systemctl restart rcracer-server
sleep 1
sudo systemctl --no-pager status rcracer-server || true
echo "remote_install.sh: done"
