# 05 · Deployment & ops

**Status:** Not started

## Goal

Run the MVP on the home server (`debianhome`, i3-6100T, Debian), reachable from phones
outside the home network, with backups that have actually been restored once.

## Tasks

- [ ] `deploy/docker-compose.prod.yml`: `api` (prod image from plan 01) and `db` with a named
      volume. Memory limits: db 512 MB, api 512 MB. Restart policies.
- [ ] Remote access, choose one:
  - **Cloudflare Tunnel** (a public hostname, works for invited non-technical users), plus
        optionally Cloudflare Access in front.
  - **Tailscale** (private, but every user must install it). Default: Cloudflare Tunnel.
- [ ] HTTPS only, HSTS, a secure cookie configuration behind the proxy (`ForwardedHeaders`).
- [ ] Secrets: Google client secret and DB password in a `.env` (not committed) or Docker
      secrets.
- [ ] Backups: nightly `pg_dump` → `age`-encrypted → local disk plus an off-site copy (e.g.
      rclone to cloud storage). Keep 14 daily and 6 monthly.
- [ ] **A restore drill** documented in `deploy/README.md` and done once.
- [ ] Deploy script: `git pull && docker compose build && docker compose up -d`, run over SSH.
      Optionally a GitHub Actions self-hosted runner later.
- [ ] Basic monitoring: the `/health` endpoint, plus an uptime check (e.g. Uptime Kuma or a
      Cloudflare health check).
- [ ] Log rotation (Docker `json-file` max-size).

## Acceptance criteria

- The owner can log in from a phone on mobile data and install the PWA.
- A backup restore into a fresh container reproduces the data.
- The whole stack (without the LLM) idles under 1 GB RAM.
