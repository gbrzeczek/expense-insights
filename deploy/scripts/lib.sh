# Shared helpers for the deploy scripts. Sourced, not executed.
# shellcheck shell=bash

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck disable=SC2034 # used by the scripts that source this file
REPO_DIR="$(cd "$DEPLOY_DIR/.." && pwd)"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env}"

log() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >&2; }
die() {
  log "error: $*"
  exit 1
}

# Prints a value from deploy/.env, unless the variable is already set in the environment.
# The file is read, not sourced, so passwords with shell characters stay inert.
env_value() {
  local name="$1" default="${2-}"
  if [[ -n "${!name-}" ]]; then
    printf '%s' "${!name}"
    return
  fi
  local line value
  line="$(grep -E "^${name}=" "$ENV_FILE" 2>/dev/null | tail -n 1 || true)"
  value="${line#*=}"
  value="${value%\"}" value="${value#\"}"
  value="${value%\'}" value="${value#\'}"
  printf '%s' "${value:-$default}"
}

require_value() {
  local value
  value="$(env_value "$1")"
  [[ -n "$value" ]] || die "$1 is not set (deploy/.env)"
  printf '%s' "$value"
}

compose() {
  docker compose --env-file "$ENV_FILE" -f "$DEPLOY_DIR/docker-compose.prod.yml" "$@"
}

# Pings a dead man's switch URL (healthchecks.io style: <url> on success, <url>/fail on failure).
ping_url() {
  local url="$1" suffix="${2-}"
  [[ -n "$url" ]] || return 0
  curl -fsS -m 10 --retry 3 -o /dev/null "${url}${suffix}" || log "warning: monitoring ping failed"
}

# Defines the rclone remote `r2` from deploy/.env, so no rclone.conf is needed. Without
# R2_ACCESS_KEY_ID, rclone falls back to its own config file.
export_r2_remote() {
  local key
  key="$(env_value R2_ACCESS_KEY_ID)"
  [[ -n "$key" ]] || return 0
  export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare RCLONE_CONFIG_R2_ACL=private
  # A token scoped to one bucket may not list buckets.
  export RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
  export RCLONE_CONFIG_R2_ACCESS_KEY_ID="$key"
  RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$(require_value R2_SECRET_ACCESS_KEY)"
  RCLONE_CONFIG_R2_ENDPOINT="$(require_value R2_ENDPOINT)"
  export RCLONE_CONFIG_R2_SECRET_ACCESS_KEY RCLONE_CONFIG_R2_ENDPOINT
}

# Reads file names (one per line) and prints those past the newest $1. Names sort by date.
beyond_retention() {
  sort -r | tail -n +"$(($1 + 1))"
}
