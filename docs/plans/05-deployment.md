# 05 · Deployment & ops

**Status:** In progress. Everything is built and tested locally with Docker; the steps that need
the real server are listed in `deploy/README.md` → *What the owner still has to do on the server*.

## Goal

Run the MVP on the home server (`debianhome`, i3-6100T, Debian), reachable from phones
outside the home network, with backups that have actually been restored once.

## Tasks

- [x] `deploy/docker-compose.prod.yml`: `api` (prod image from plan 01) and `db` with a named
      volume. Memory limits: db 512 MB, api 512 MB. Restart policies. Plus `caddy` (128 MB) as the
      TLS edge.
- [x] Remote access, choose one:
  - **Cloudflare Tunnel** (a public hostname, works for invited non-technical users), plus
        optionally Cloudflare Access in front.
  - **Tailscale** (private, but every user must install it). Default: Cloudflare Tunnel.

  **Chosen: Tailscale only** (D23), under `expenses.gbrzeczek.dev`: a Cloudflare DNS-only record
  pointing at the server's Tailscale address, Caddy bound to that address, Let's Encrypt via the
  Cloudflare DNS-01 challenge. Nothing is reachable from the internet or the LAN.
- [x] HTTPS only, HSTS, a secure cookie configuration behind the proxy (`ForwardedHeaders`).
      Caddy redirects HTTP and sends HSTS; `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is covered by an
      integration test (https redirect URI, `Secure` cookies).
- [x] Secrets: Google client secret and DB password in a `.env` (not committed) or Docker
      secrets. The GitHub environment `production` holds them; each deploy writes the server's
      `deploy/.env` (mode 600) from it, and Compose refuses to start with a required value empty
      (D30).
- [x] Backups: nightly `pg_dump` → `age`-encrypted → local disk plus an off-site copy (e.g.
      rclone to cloud storage). Keep 14 daily and 6 monthly. `deploy/scripts/backup.sh` on a
      systemd timer, off-site to Cloudflare R2 (D24).
- [ ] **A restore drill** documented in `deploy/README.md` and done once.
      Documented, scripted (`restore-drill.sh` compares every table's row count and checksum) and
      passed locally on fake data; still to do once on the server from an R2 copy.
- [x] Deploy script: `git pull && docker compose build && docker compose up -d`, run over SSH.
      Optionally a GitHub Actions self-hosted runner later. Done as in sheetsolver instead (D30):
      after CI passes on a push to `main` (D36), the Deploy workflow builds the images on GitHub, pushes them to
      GHCR, joins the tailnet as `tag:ci` and runs `deploy.sh` on the server over SSH with the
      server's shared deploy key (pull, backup before migrations, start, wait for `/health`).
- [x] Basic monitoring: the `/health` endpoint, plus an uptime check (e.g. Uptime Kuma or a
      Cloudflare health check). A 5-minute systemd timer checks `/health` through Caddy and pings
      a dead man's switch (healthchecks.io), as does the backup (D25).
- [x] Log rotation (Docker `json-file` max-size). 3 × 10 MB per container.

## Acceptance criteria

- The owner can log in from a phone on mobile data and install the PWA.
- A backup restore into a fresh container reproduces the data.
- The whole stack idles under 1 GB RAM.

The first two need the server and a phone; the third measured about 160 MB locally (api 92 MB,
db 58 MB, caddy 11 MB) and still has to be confirmed on `debianhome`.
