#!/usr/bin/env bash
# Deploys the current main on the server: pull, back up (migrations run on start), build, start,
# wait for /health. Run it on the server, or from a laptop over SSH:
#
#   ssh debianhome '~/expense-insights/deploy/scripts/deploy.sh'
#   deploy/scripts/deploy.sh --no-backup
set -euo pipefail
source "$(dirname "$0")/lib.sh"

backup=1
[[ "${1-}" == --no-backup ]] && backup=0

log "pulling"
git -C "$REPO_DIR" pull --ff-only

log "building"
compose build --pull

if [[ $backup -eq 1 && -n "$(compose ps -q db)" ]]; then
  log "backing up before migrations"
  "$DEPLOY_DIR/scripts/backup.sh"
fi

log "starting"
compose up -d --remove-orphans

port="$(env_value API_PORT 8080)"
for _ in $(seq 60); do
  if curl -fsS -m 5 -o /dev/null "http://127.0.0.1:$port/health"; then
    log "healthy at $(git -C "$REPO_DIR" rev-parse --short HEAD)"
    docker image prune -f >/dev/null
    exit 0
  fi
  sleep 2
done
compose logs --tail 50 api
die "api did not become healthy"
