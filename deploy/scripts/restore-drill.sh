#!/usr/bin/env bash
# Restore drill: restores a backup into a throwaway Postgres container and compares every table
# (row count + checksum of all rows) with the live database. Production is not touched.
#
#   deploy/scripts/backup.sh                                     # fresh backup, so both match
#   deploy/scripts/restore-drill.sh <backup.dump.age> <age identity file>
#
# Exits non-zero if a table differs. Rows written after the backup show up as differences.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

[[ $# -eq 2 ]] || die "usage: $0 <backup.dump.age> <age identity file>"
backup="$1" identity="$2"
[[ -f "$backup" ]] || die "no such backup: $backup"
[[ -f "$identity" ]] || die "no such identity file: $identity"

db_name="$(env_value POSTGRES_DB expense_insights)"
container="expense-insights-restore-drill"
image="$(compose config --images | grep -m1 pgvector)"

cleanup() { docker rm -f "$container" >/dev/null 2>&1 || true; }
trap cleanup EXIT
cleanup

log "starting a fresh $image container"
docker run -d --rm --name "$container" -e POSTGRES_PASSWORD=drill -e POSTGRES_DB="$db_name" \
  --tmpfs /var/lib/postgresql:rw,size=1g "$image" >/dev/null
ready=0
for _ in $(seq 60); do
  # The entrypoint's temporary init server listens only on the socket; TCP means the final one.
  if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres -d "$db_name" -q; then
    ready=1
    break
  fi
  sleep 1
done
[[ $ready -eq 1 ]] || die "drill database did not start"

log "restoring $(basename "$backup")"
age --decrypt --identity "$identity" "$backup" |
  docker exec -i "$container" pg_restore -U postgres -d "$db_name" --no-owner --exit-on-error

# Builds one query with a row per table: name, row count, md5 of all rows in a stable order.
fingerprint_sql=$(
  cat <<'SQL'
SELECT string_agg(
         format('SELECT %L, count(*), coalesce(md5(string_agg(t::text, %L ORDER BY t::text)), %L) FROM %I.%I t',
                table_name, E'\n', '-', table_schema, table_name),
         ' UNION ALL ' ORDER BY table_name)
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
SQL
)

# psql arguments are appended to the command given in "$@".
fingerprint() {
  local query
  query="$("$@" -X -At -c "$fingerprint_sql")"
  "$@" -X -At -F ' ' -c "$query ORDER BY 1"
}

# shellcheck disable=SC2016 # expanded by the container's shell
live="$(fingerprint compose exec -T db sh -c 'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" "$@"' psql)"
restored="$(fingerprint docker exec "$container" psql -U postgres -d "$db_name")"

printf '%-36s %10s %10s  %s\n' table live restored result
status=0
while read -r table; do
  live_line="$(grep -E "^$table " <<<"$live" || true)"
  restored_line="$(grep -E "^$table " <<<"$restored" || true)"
  if [[ -n "$live_line" && "$live_line" == "$restored_line" ]]; then result=ok; else
    result=DIFFERENT
    status=1
  fi
  printf '%-36s %10s %10s  %s\n' "$table" "$(cut -s -d' ' -f2 <<<"$live_line")" \
    "$(cut -s -d' ' -f2 <<<"$restored_line")" "$result"
done < <(printf '%s\n%s\n' "$live" "$restored" | cut -d' ' -f1 | sort -u)

if [[ $status -eq 0 ]]; then
  log "drill passed: the backup reproduces every table"
else
  log "drill found differences (expected only for rows changed since the backup)"
fi
exit $status
