#!/usr/bin/env bash
# rclone with the `r2` remote from deploy/.env, for fetching backups by hand:
#
#   deploy/scripts/r2.sh lsf r2:expense-insights-backups/daily
#   deploy/scripts/r2.sh copy r2:expense-insights-backups/daily/<file> /tmp/drill/
set -euo pipefail
source "$(dirname "$0")/lib.sh"

export_r2_remote
exec rclone "$@"
