#!/usr/bin/env bash
# Run by the Deploy workflow over SSH. The job copies this commit's version to
# $DEPLOY_ROOT/.ci-deploy.sh first, so nothing has to be on the server beforehand:
#
#   DEPLOY_ROOT=/opt/apps/expense-insights bash /opt/apps/expense-insights/.ci-deploy.sh deploy <commit>
#
# Accepts exactly `deploy <commit>`. Reads a gzipped tar on stdin holding `deploy/` (compose file,
# scripts, systemd units, the generated .env) and `registry-auth` (`<user>:<token>`, the job's
# GHCR token, valid only while the job runs), swaps the new `deploy/` in, and runs deploy.sh.
set -euo pipefail

main() {
  local command="$*"
  if [[ ! "$command" =~ ^deploy\ ([0-9a-f]{40})$ ]]; then
    echo "refused: expected 'deploy <commit>'" >&2
    exit 2
  fi
  local commit="${BASH_REMATCH[1]}"

  local root staging
  root="$(cd "${DEPLOY_ROOT:?}" && pwd)"
  umask 077
  staging="$(mktemp -d "$root/.incoming.XXXXXX")"
  # shellcheck disable=SC2064 # expand now: $staging is local
  trap "rm -rf -- '${staging:?}'" EXIT

  tar -xzf - -C "$staging" --no-same-owner
  [[ -f "$staging/deploy/docker-compose.prod.yml" && -f "$staging/deploy/.env" ]] ||
    { echo "incomplete bundle" >&2; exit 1; }
  grep -qx "IMAGE_TAG='$commit'" "$staging/deploy/.env" ||
    { echo "bundle is not for $commit" >&2; exit 1; }

  # A Docker config of its own for the pull, so no registry login stays on the server.
  if [[ -f "$staging/registry-auth" ]]; then
    mkdir "$staging/docker"
    printf '{"auths":{"ghcr.io":{"auth":"%s"}}}\n' "$(tr -d '\n' <"$staging/registry-auth" | base64 -w0)" \
      >"$staging/docker/config.json"
    export DOCKER_PULL_CONFIG="$staging/docker"
  fi

  # The previous deploy/ stays next to it until the next deploy, for a quick look or a rollback.
  rm -rf -- "${root:?}/deploy.previous"
  if [[ -d "$root/deploy" ]]; then mv "$root/deploy" "$root/deploy.previous"; fi
  mv "$staging/deploy" "$root/deploy"

  "$root/deploy/scripts/deploy.sh"
}

# Parsed in full before it runs, so replacing deploy/ (and this file) above is safe.
main "$@"
exit
