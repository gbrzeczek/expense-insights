#!/usr/bin/env bash
# Nightly backup: pg_dump (custom format) → age-encrypted → $BACKUP_DIR, then an off-site copy
# with rclone. Keeps $KEEP_DAILY daily and $KEEP_MONTHLY monthly files, locally and off-site.
#
#   deploy/scripts/backup.sh            # run by expense-insights-backup.timer, or by hand
set -euo pipefail
source "$(dirname "$0")/lib.sh"

backup_dir="$(require_value BACKUP_DIR)"
recipient="$(require_value AGE_RECIPIENT)"
remote="$(env_value RCLONE_REMOTE)"
keep_daily="$(env_value KEEP_DAILY 14)"
keep_monthly="$(env_value KEEP_MONTHLY 6)"
ping="$(env_value BACKUP_PING_URL)"

trap 'ping_url "$ping" /fail' ERR

mkdir -p "$backup_dir/daily" "$backup_dir/monthly"
chmod 700 "$backup_dir"

daily="$backup_dir/daily/expense-insights-$(date +%F).dump.age"
monthly="$backup_dir/monthly/expense-insights-$(date +%Y-%m).dump.age"
partial="$daily.partial"

log "dumping database to $daily"
# pg_dump runs inside the db container (local socket, no password) and the dump never touches
# the disk unencrypted.
# shellcheck disable=SC2016 # expanded by the container's shell
compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom --compress=6' |
  age --encrypt --recipient "$recipient" --output "$partial"
[[ -s "$partial" ]] || die "empty dump"
mv -f "$partial" "$daily"

if [[ ! -e "$monthly" ]]; then
  cp "$daily" "$monthly"
  log "monthly copy $monthly"
fi

keep_for() { if [[ "$1" == daily ]]; then echo "$keep_daily"; else echo "$keep_monthly"; fi; }

for kind in daily monthly; do
  find "$backup_dir/$kind" -maxdepth 1 -name 'expense-insights-*.dump.age' -printf '%f\n' |
    beyond_retention "$(keep_for "$kind")" |
    while read -r name; do
      log "pruning local $kind/$name"
      rm -f -- "${backup_dir:?}/${kind:?}/${name:?}"
    done
done

if [[ -n "$remote" ]]; then
  log "copying to $remote"
  # copy, never sync: losing the local disk must not delete the off-site files.
  rclone copy "$backup_dir" "$remote" --include 'daily/*.dump.age' --include 'monthly/*.dump.age'
  for kind in daily monthly; do
    rclone lsf "$remote/$kind" --files-only --include 'expense-insights-*.dump.age' |
      beyond_retention "$(keep_for "$kind")" |
      while read -r name; do
        log "pruning remote $kind/$name"
        rclone deletefile "$remote/$kind/$name"
      done
  done
else
  log "warning: RCLONE_REMOTE is empty, no off-site copy"
fi

log "backup done ($(du -h "$daily" | cut -f1))"
ping_url "$ping"
