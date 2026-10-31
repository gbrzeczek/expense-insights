#!/usr/bin/env bash
# Installs the backup and uptime timers, run as the given user (default: whoever runs this),
# who must be in the docker group. Needs sudo.
#
#   deploy/systemd/install.sh deploy
set -euo pipefail

repo_dir="$(cd "$(dirname "$0")/../.." && pwd)"
user="${1:-$(id -un)}"
id -nG "$user" | grep -qw docker || {
  echo "$user is not in the docker group" >&2
  exit 1
}

for unit in "$(dirname "$0")"/expense-insights-*.{service,timer}; do
  sed -e "s|@USER@|$user|g" -e "s|@REPO_DIR@|$repo_dir|g" "$unit" |
    sudo tee "/etc/systemd/system/$(basename "$unit")" >/dev/null
done
sudo systemctl daemon-reload
sudo systemctl enable --now expense-insights-backup.timer expense-insights-healthcheck.timer
systemctl list-timers 'expense-insights-*'
