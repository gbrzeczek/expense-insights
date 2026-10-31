#!/usr/bin/env bash
# Disaster recovery: replaces the production database with a backup. Stops the API meanwhile.
#
#   deploy/scripts/restore.sh <backup.dump.age> <age identity file>
#
# For a drill that leaves production alone, use restore-drill.sh instead.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

[[ $# -eq 2 ]] || die "usage: $0 <backup.dump.age> <age identity file>"
backup="$1" identity="$2"
[[ -f "$backup" ]] || die "no such backup: $backup"
[[ -f "$identity" ]] || die "no such identity file: $identity"

# Decrypt first, so a wrong key fails before anything is dropped. The plaintext stays in
# memory-backed /dev/shm and is removed on exit.
plain="$(mktemp /dev/shm/expense-insights-restore.XXXXXX)"
trap 'rm -f -- "${plain:?}"' EXIT
age --decrypt --identity "$identity" --output "$plain" "$backup"

read -r -p "This replaces the production database with $(basename "$backup"). Type 'restore' to continue: " answer
[[ "$answer" == restore ]] || die "aborted"

log "stopping api"
compose stop api
compose up -d --wait db

# shellcheck disable=SC2016 # expanded by the container's shell
{
  log "recreating database"
  compose exec -T db sh -c 'dropdb -U "$POSTGRES_USER" --if-exists --force "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
  compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --exit-on-error' <"$plain"
}

log "starting api"
compose up -d
log "restored $(basename "$backup")"
