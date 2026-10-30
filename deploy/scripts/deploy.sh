#!/usr/bin/env bash
# Deploys the images named in deploy/.env (IMAGE_TAG) on the server: pull what is missing, back up
# (migrations run on start), start, wait for /health. Run by ci-deploy.sh from the Deploy
# workflow, or by hand on the server:
#
#   deploy/scripts/deploy.sh               # needs `docker login ghcr.io` for a new IMAGE_TAG
#   deploy/scripts/deploy.sh --no-backup
#
# DOCKER_PULL_CONFIG, if set, is a Docker config directory holding the registry login for the pull.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

backup=1
[[ "${1-}" == --no-backup ]] && backup=0

for image in $(compose config --images); do
  if ! docker image inspect "$image" >/dev/null 2>&1; then
    log "pulling $image"
    DOCKER_CONFIG="${DOCKER_PULL_CONFIG:-${DOCKER_CONFIG:-$HOME/.docker}}" docker pull -q "$image" >/dev/null
  fi
done

if [[ $backup -eq 1 && -n "$(compose ps -q db)" ]]; then
  log "backing up before migrations"
  "$DEPLOY_DIR/scripts/backup.sh"
fi

log "starting $(env_value IMAGE_TAG local)"
compose up -d --no-build --remove-orphans

port="$(env_value API_PORT 8080)"
for _ in $(seq 60); do
  if curl -fs -m 5 -o /dev/null "http://127.0.0.1:$port/health"; then
    log "healthy"
    docker image prune -f >/dev/null
    exit 0
  fi
  sleep 2
done
compose logs --tail 50 api
die "api did not become healthy"
