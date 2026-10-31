#!/usr/bin/env bash
# Uptime check, every 5 minutes from expense-insights-healthcheck.timer: requests /health through
# Caddy (so TLS and the certificate are checked too) and pings HEALTH_PING_URL. The ping service
# alerts when pings stop, which also covers the whole server being down.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

host="$(require_value APP_HOST)"
ping="$(env_value HEALTH_PING_URL)"

if curl -fsS -m 10 -o /dev/null "https://$host/health"; then
  ping_url "$ping"
else
  log "https://$host/health failed"
  ping_url "$ping" /fail
  exit 1
fi
