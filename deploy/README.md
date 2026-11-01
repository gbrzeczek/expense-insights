# Deployment

The app runs on the home server (`debianhome`) with Docker Compose and is reachable **only inside
the tailnet**, under a normal Cloudflare-managed name with a real certificate:

```
phone (Tailscale app) ──tailnet──▶ debianhome 100.x.y.z:443
                                   └─ caddy (TLS, Let's Encrypt via Cloudflare DNS-01)
                                      └─ api :8080 (Angular app + /api)
                                         └─ db (Postgres, named volume)
expenses.gbrzeczek.dev  ─ Cloudflare DNS, A record → 100.x.y.z, "DNS only" (grey cloud)
```

- [How a deploy works](#how-a-deploy-works) · [Production setup](#production-setup-once) ·
  [Deploying](#deploying) · [Secrets](#secrets-where-they-live) · [Backups](#backups) · [Restore drill](#restore-drill) · [Disaster recovery](#disaster-recovery) ·
  [Monitoring and logs](#monitoring-and-logs) · [What the owner still has to do](#what-the-owner-still-has-to-do-on-the-server)
- The dev stack (`docker-compose.yml`) is described in `AGENTS.md`; this file covers production
  (`docker-compose.prod.yml`, `scripts/`, `systemd/`) and sign-in.

## Why Tailscale, and a Cloudflare name that isn't a tunnel

The data is bank history, so nothing is published to the internet:

- **The name resolves publicly but only routes inside the tailnet.** `expenses.gbrzeczek.dev` is a
  plain DNS record pointing at the server's Tailscale address (100.64.0.0/10, not routable on the
  internet). Anyone can look it up; only devices in the tailnet can connect.
- **Caddy listens only on the Tailscale address.** Not on the LAN, not on `0.0.0.0`. The home
  router forwards nothing.
- **A real certificate without an open port.** Let's Encrypt's DNS-01 challenge: Caddy creates a
  TXT record through the Cloudflare API, so the browser and the PWA see a normal trusted HTTPS
  site. Google sign-in works too; Google never connects to the server, it only redirects the
  phone's browser to `https://expenses.gbrzeczek.dev/api/auth/callback/google`.
- **Don't add this hostname to the existing Cloudflare Tunnel.** A tunnel route would make it
  public, and the proxied (orange-cloud) mode would send the traffic through Cloudflare. The
  tunnel already running for the other subdomain stays as it is; the two don't interact.
- The trade-off: everyone who uses the app needs the Tailscale app, and the server must be shared
  with them (see step 9).

Things to know: the hostname appears in public Certificate Transparency logs (it reveals that the
name exists, nothing more); any device in the tailnet can reach the login page (sign-in is still
needed; tighten with Tailscale ACLs if the tailnet grows).

## How a deploy works

Every merge to `main` that passes CI deploys itself: on a push to `main`, CI's last job (`release`)
calls `.github/workflows/deploy.yml` once every other job has passed. Pull requests, a fork's
included, never reach it (D36):

1. **Images.** GitHub's runners build the app image and the Caddy image and push both to the
   GitHub Container Registry, tagged with the commit (`ghcr.io/gbrzeczek/expense-insights:<sha>`).
   The packages are private, whatever the repository's visibility (set separately per package).
2. **Tailscale.** The deploy job joins the tailnet for its duration as `tag:ci`, through a
   Tailscale OAuth client, and may reach only the server's SSH port.
3. **Settings.** It writes `deploy/.env` from the GitHub environment `production` (secrets and
   variables, see step 8), so GitHub is where settings are edited and the server only holds the
   generated copy.
4. **SSH.** It sends `deploy/` (compose file, scripts, systemd units, the new `.env`) and the job's
   short-lived registry token to the server, together with this commit's
   `deploy/scripts/ci-deploy.sh`. That script swaps the new `deploy/` in (keeping the
   old one as `deploy.previous`) and runs `deploy/scripts/deploy.sh`: pull the new images, back up
   (the API runs migrations on start), start, wait for `/health`. A failed health check fails
   the job.

The server never builds anything and needs no access to GitHub. The deploy job is skipped until
the repository variable `DEPLOY_ENABLED` is `true`, so the images build before the server is ready.

## Production setup (once)

Server commands run as an admin user with `sudo`, unless they say `sudo -u deploy`. The app lives
in `/opt/apps/expense-insights`, owned by a `deploy` user (the same one sheetsolver uses is fine).

### 1. Server: packages, user, directories

```bash
sudo apt install curl age rclone
id deploy || sudo adduser --disabled-password --gecos '' deploy
sudo usermod -aG docker deploy
sudo install -d -o deploy -m 750 /opt/apps/expense-insights
sudo install -d -o deploy -m 700 /var/backups/expense-insights
```

Docker Engine with the Compose plugin and Tailscale are assumed to be there already. Nothing
else has to be copied by hand: the first deploy brings `deploy/` along.

### 2. The Tailscale address, and binding to it at boot

```bash
tailscale ip -4                       # e.g. 100.101.102.103 → the TAILSCALE_IP variable
```

Docker binds the published ports when the containers start. At boot Docker can start before
Tailscale has brought the address up, and then Caddy's ports fail to bind. Allow binding to an
address that isn't up yet:

```bash
echo 'net.ipv4.ip_nonlocal_bind = 1' | sudo tee /etc/sysctl.d/60-expense-insights.conf
sudo sysctl --system
```

Nothing else may listen on port 443 or 80 on all addresses (`0.0.0.0`); check with
`sudo ss -ltnp '( sport = :443 or sport = :80 )'`. `cloudflared` only connects outwards, so the
sheetsolver tunnel doesn't conflict.

### 3. DNS record and Cloudflare API token

In the Cloudflare dashboard for `gbrzeczek.dev`:

1. **DNS → Records → Add record:** type `A`, name `expenses`, IPv4 = the Tailscale address,
   **Proxy status: DNS only** (grey cloud). Proxied would not work: Cloudflare can't reach a
   tailnet address.
2. **My Profile → API Tokens → Create Token → Edit zone DNS** template: permission
   `Zone · DNS · Edit`, zone resources `Include · Specific zone · gbrzeczek.dev`. It can change DNS
   records of this one zone and nothing else. → secret `CLOUDFLARE_API_TOKEN`.

Some routers block DNS answers that point at private ranges ("DNS rebinding protection"). Phones
on the Tailscale app use Tailscale's resolver, so this usually doesn't matter; if the name doesn't
resolve on the home Wi-Fi, allow `gbrzeczek.dev` in the router or enable MagicDNS.

### 4. Google OAuth redirect URI

In the Google Cloud client (see [Google Cloud OAuth client](#google-cloud-oauth-client)), add
`https://expenses.gbrzeczek.dev/api/auth/callback/google` to **Authorized redirect URIs**. The
same client serves development and production.

### 5. An `age` key for the backups

On your laptop, not the server:

```bash
age-keygen -o expense-insights-backup.key
# Public key: age1...   → the AGE_RECIPIENT variable
```

Keep the private key file in your password manager and on one offline copy (a USB stick in a
drawer). It never goes to the server or to GitHub, so a stolen server, a leaked bucket or a leaked
GitHub secret doesn't expose the backups. Without it the backups can't be read; there is no
recovery.

### 6. Cloudflare R2 bucket

1. **R2 → Create bucket:** `expense-insights-backups`, location automatic. Leave public access off.
2. **R2 → Manage API tokens → Create API token:** permission **Object Read & Write**, applied to
   **this bucket only**. → secrets `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY`; the S3 endpoint
   (`https://<account id>.r2.cloudflarestorage.com`) → variable `R2_ENDPOINT`.

No `rclone config` is needed: the scripts define the `r2` remote from `deploy/.env`
(`deploy/scripts/r2.sh` runs rclone with it by hand). The backups stay well inside R2's free 10 GB.

### 7. Deploy key and Tailscale access for the job

**SSH key.** Reuse the server's shared deploy key, the one sheetsolver's workflow uses: its
private half → secret `DEPLOY_SSH_KEY`. It is a plain shell as `deploy`, which is in the `docker`
group and so effectively root on the server; a leak of this secret (or sheetsolver's) gives the
whole server. That is accepted because SSH is reachable only from the tailnet, and only `tag:ci`
may reach port 22. If the key is ever rotated, update the secret in every repository that uses it.

**Host key**, from a machine in the tailnet: `ssh-keyscan <name>` → secret `DEPLOY_KNOWN_HOSTS`,
so the job knows it talks to the real server. SSH looks the server up by the exact name it
connects to, so `<name>` must be the same string as the `DEPLOY_HOST` secret (step 8), e.g.
`debianhome.<tailnet>.ts.net` as in sheetsolver; otherwise the job fails with
`Host key verification failed`.

**Tailscale.** Reuse sheetsolver's setup: the `tag:ci` tag, its OAuth client (copy the id and
secret into this repository's secrets), and an access rule letting `tag:ci` reach only port 22 on
the server:

```json
"tagOwners": { "tag:ci": ["autogroup:admin"] },
"grants": [
  { "src": ["tag:ci"], "dst": ["debianhome"], "ip": ["tcp:22"] }
]
```

(Use the server's tag instead of its name if it has one.) Runners join as ephemeral nodes and
disappear after the job. Port 22 only: the job never needs the app itself.

### 8. GitHub: the `production` environment

Repository → **Settings → Environments → New environment** `production`. Under **Deployment
branches and tags**, choose *Selected branches* and add `main`, so only workflows on `main` can
read these values. Then add:

| Kind | Name | Value |
|---|---|---|
| Secret | `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET` | Tailscale OAuth client (step 7) |
| Secret | `DEPLOY_SSH_KEY` | The shared deploy key's private half, whole (step 7) |
| Secret | `DEPLOY_KNOWN_HOSTS` | Output of `ssh-keyscan <DEPLOY_HOST>` (step 7) |
| Secret | `POSTGRES_PASSWORD` | `openssl rand -base64 32 \| tr -d '/+='`. Set once: the database keeps it |
| Secret | `GOOGLE_CLIENT_SECRET` | Google OAuth client |
| Secret | `CLOUDFLARE_API_TOKEN` | Step 3 |
| Secret | `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY` | Step 6 |
| Secret | `R2_ENDPOINT` | Step 6 (it holds the Cloudflare account id) |
| Secret | `BACKUP_PING_URL`, `HEALTH_PING_URL` | healthchecks.io ping URLs (optional, see *Monitoring*) |
| Secret | `DEPLOY_HOST` | The server's MagicDNS name, e.g. `debianhome.<tailnet>.ts.net`, the name `DEPLOY_KNOWN_HOSTS` was scanned with |
| Secret | `AUTH_OWNER_EMAIL` | Your Google address |
| Variable | `DEPLOY_USER` | `deploy` |
| Variable | `DEPLOY_DIR` | Optional; default `/opt/apps/expense-insights` |
| Variable | `GOOGLE_CLIENT_ID` | Google OAuth client id (public) |
| Variable | `APP_HOST` | `expenses.gbrzeczek.dev` |
| Variable | `TAILSCALE_IP` | Step 2 |
| Variable | `ACME_EMAIL` | Your email, for Let's Encrypt expiry notices |
| Variable | `AGE_RECIPIENT` | The `age1…` public key (step 5) |
| Variable | `RCLONE_REMOTE` | `r2:expense-insights-backups` |
| Variable | `BACKUP_DIR` | Optional; default `/var/backups/expense-insights` |

Variables are printed in the job log, which anyone can read in a public repository; secrets are
masked. So anything personal or naming the tailnet is a secret, even when it isn't a credential.
Values must not contain a single quote or a line break (the job refuses them). Finally, a
**repository** variable (Settings → Secrets and variables → Actions → Variables), because the
job's `if` is decided before the environment is loaded: `DEPLOY_ENABLED` = `true` turns deploys on.

### 9. First deploy, timers, first backup

**Actions → Deploy → Run workflow** on `main` (or merge something). Then on the server:

```bash
sudo -u deploy docker compose -f /opt/apps/expense-insights/deploy/docker-compose.prod.yml logs caddy | grep -i certificate
curl -fsS https://expenses.gbrzeczek.dev/health                          # Healthy
sudo /opt/apps/expense-insights/deploy/systemd/install.sh deploy              # nightly backup + uptime timers
sudo -u deploy /opt/apps/expense-insights/deploy/scripts/backup.sh            # first backup now
```

The API migrates the database on start. The owner (`AUTH_OWNER_EMAIL`) signs in with Google and
invites the others from the app. Re-run `install.sh` only if a deploy changes the units in
`deploy/systemd/`.

### 10. Phones and invited people

- **You:** install Tailscale on the phone and sign in to your tailnet. Open
  `https://expenses.gbrzeczek.dev`, sign in with Google, then install the app (Android Chrome: menu
  → *Install app*; iPhone Safari: Share → *Add to Home Screen*).
- **Someone else:** invite their Google address in the app and, while the OAuth consent screen is
  in *Testing*, add it under *Test users*. Then either share the machine with them (Tailscale admin
  console → **Machines → debianhome → Share**; they accept with their own Tailscale account and
  see only this machine) or invite them to your tailnet. They install the Tailscale app and open the
  same address.

## Deploying

- **Normally:** merge to `main`. CI runs, then *Deploy*. While the API restarts, Caddy holds
  requests for up to 30 seconds instead of failing them.
- **Again, e.g. after changing a secret:** Actions → Deploy → *Run workflow* (deploys `main`).
- **Roll back:** open an older successful *CI* run on `main` → *Re-run all jobs*; it tests and
  deploys that run's commit, whose images are still in GHCR. A rollback doesn't undo database migrations; if the
  newer version migrated the schema, restore the backup taken before it (see *Disaster recovery*).
- **By hand on the server**, e.g. with GitHub down: images already pulled are reused, a new tag
  needs a `docker login ghcr.io` with a token that can read packages first.

  ```bash
  sudo -u deploy /opt/apps/expense-insights/deploy/scripts/deploy.sh        # --no-backup to skip the backup
  ```

- **From source**, without GHCR: in a checkout with a `deploy/.env`,
  `docker compose -f deploy/docker-compose.prod.yml up -d --build` (tags the images `local`).

## Secrets: where they live

| Where | What | Role |
|---|---|---|
| GitHub environment `production` | Everything in step 8 | Where you edit settings; write-only, can't be read back |
| Password manager | All of step 8 except the deploy key, plus the `age` private key | The master copy, for rebuilding everything |
| Server, `/opt/apps/expense-insights/deploy/.env` (mode 600) | Generated by each deploy | Runtime copy; overwritten on every deploy |
| Nowhere near the server or GitHub | The `age` private key | Only for restores, briefly in `/dev/shm` |

To change or rotate a value: change it where it was issued (Google, Cloudflare, R2, healthchecks.io),
update the password manager and the GitHub environment, then run *Deploy*. The exception is
`POSTGRES_PASSWORD`: the database keeps its own copy, so first run
`ALTER USER expense_insights PASSWORD '<new>'` in the db container
(`docker compose … exec db psql -U expense_insights`), then update GitHub and deploy.

Development doesn't use any of this: locally the Google client comes from `dotnet user-secrets`,
and CI builds and tests without secrets.

## Backups

`scripts/backup.sh`, run nightly at about 03:15 by `expense-insights-backup.timer` (and after boot
if the server was off then):

1. `pg_dump --format=custom` inside the db container, piped straight into `age` with the public
   key. The plaintext never touches the disk.
2. Written to `$BACKUP_DIR/daily/expense-insights-YYYY-MM-DD.dump.age`; the first backup of each
   month is also copied to `monthly/expense-insights-YYYY-MM.dump.age`.
3. Retention: the newest 14 daily and 6 monthly files, locally and in R2.
4. `rclone copy` to R2, then the same retention applied there. It is a copy, never a sync, so a
   wiped local disk can't delete the off-site files.
5. On success it pings `BACKUP_PING_URL`, on failure `BACKUP_PING_URL/fail`.

Run it by hand with `sudo -u deploy /opt/apps/expense-insights/deploy/scripts/backup.sh`; see past runs
with `journalctl -u expense-insights-backup`.

What the dump contains: every table, including the Data Protection keys, so sessions survive a
restore. What it doesn't: the settings (they come from GitHub on every deploy, master copy in the
password manager) and Caddy's certificates (re-issued automatically).

## Restore drill

`scripts/restore-drill.sh` restores a backup into a throwaway Postgres container (in memory) and
compares every table with the live database: the row count and an MD5 over all rows. Production is
not touched. Do it once now, and again after anything changes in the backup chain.

```bash
sudo -iu deploy
cd /opt/apps/expense-insights
deploy/scripts/backup.sh                                   # fresh, so live and backup match
# Off-site copy, to prove R2 and the key work, not just the local file:
deploy/scripts/r2.sh copy "r2:expense-insights-backups/daily/expense-insights-$(date +%F).dump.age" /tmp/drill/
# The private key, only for the drill, in memory:
install -m 600 /dev/null /dev/shm/backup.key && nano /dev/shm/backup.key   # paste the key
deploy/scripts/restore-drill.sh /tmp/drill/expense-insights-$(date +%F).dump.age /dev/shm/backup.key
rm -f /dev/shm/backup.key /tmp/drill/*.dump.age
```

The script prints a table per table (`ok` / `DIFFERENT`) and exits 0 only if everything matches.
Rows written after the backup show up as `DIFFERENT`, so run it right after the backup.

Drill log:

| Date | Where | Backup | Result |
|---|---|---|---|
| 2026-10-30 | Local Docker (development machine), fake data | daily, via an rclone remote | Passed: 13 tables, 335 transactions; a one-cent change was detected |
| _to do_ | `debianhome`, real data | daily, from R2 | |

## Disaster recovery

New or rebuilt server: steps 1, 2 and 7 of the setup on the new machine (update `TAILSCALE_IP`,
`DEPLOY_KNOWN_HOSTS` and the DNS record if they changed), run *Deploy* (it starts with an empty
database), then restore over it as `deploy` in `/opt/apps/expense-insights`:

```bash
deploy/scripts/r2.sh copy r2:expense-insights-backups/daily/<newest file> /tmp/restore/
install -m 600 /dev/null /dev/shm/backup.key && nano /dev/shm/backup.key
deploy/scripts/restore.sh /tmp/restore/<newest file> /dev/shm/backup.key   # asks you to type "restore"
rm -f /dev/shm/backup.key
```

`restore.sh` decrypts first (a wrong key fails before anything is dropped), stops the API,
recreates the database from the dump and starts the stack. It works the same on a running
server to roll back to a backup.

## Monitoring and logs

- `/health` checks the API and its database connection. `expense-insights-healthcheck.timer`
  requests `https://expenses.gbrzeczek.dev/health` every 5 minutes, through Caddy, so the
  certificate is checked too.
- Alerts come from a dead man's switch: create two free checks at <https://healthchecks.io>
  (`expense-insights-health`: period 5 min, grace 10 min; `expense-insights-backup`: period 1
  day, grace 2 h) and put their ping URLs in `HEALTH_PING_URL` and `BACKUP_PING_URL`. If the
  server is off or the backup fails, the pings stop and healthchecks.io emails you. An external
  checker couldn't reach a tailnet-only address anyway, so the server reports out instead.
- Logs: `docker compose -f deploy/docker-compose.prod.yml logs api` (JSON). Docker rotates them
  (`json-file`, 3 × 10 MB per container). Caddy keeps no access log, because URIs can contain
  search terms; errors still show in `logs caddy`. Timer runs: `journalctl -u 'expense-insights-*'`.
- Memory limits: db 512 MB, api 512 MB, caddy 128 MB. Locally the idle stack used about 160 MB
  (api 92, db 58, caddy 11) after importing the fake data.

## What the owner still has to do on the server

These can't be done from a development machine:

- [ ] Steps 1–10 above: server user and directories, DNS, Cloudflare token, Google redirect URI,
      `age` key, R2 bucket, deploy key and Tailscale rule, the `production` environment, a first
      deploy.
- [ ] The restore drill once on `debianhome` from an R2 copy; add a row to the drill log.
- [ ] Sign in from a phone **on mobile data** (Wi-Fi off, Tailscale on) and install the PWA.
- [ ] Check that the app isn't reachable off the tailnet: with Tailscale off on the phone,
      `https://expenses.gbrzeczek.dev` must not load. Also from the LAN:
      `curl -m 5 https://<server LAN IP>/` must fail to connect.
- [ ] Idle RAM: `docker stats --no-stream` an hour after a deploy; the three containers together
      must stay under 1 GB.
- [ ] Reboot the server once and check that the app comes back (`curl https://expenses.gbrzeczek.dev/health`).

## Sign-in configuration

The API reads these settings (user-secrets locally, environment variables in Docker; never commit
them):

| Setting | Env var | What |
|---|---|---|
| `Auth:OwnerEmail` | `Auth__OwnerEmail` | The owner's Google address. Seeded into the invitation allow-list at startup; it signs in as the owner. Without it nobody new can sign in. |
| `Auth:Google:ClientId` | `Auth__Google__ClientId` | OAuth client id from Google Cloud. |
| `Auth:Google:ClientSecret` | `Auth__Google__ClientSecret` | OAuth client secret. |

Without the Google settings the login page says Google sign-in isn't set up. In Development you
can still sign in with the dev login (the form under the Google button, or
`/api/auth/dev-login?email=<invited address>`); it exists only in Development.

### Google Cloud OAuth client

1. Open <https://console.cloud.google.com/>, create a project (e.g. `expense-insights`).
2. **APIs & Services → OAuth consent screen** (Google Auth Platform): user type **External**,
   app name `Wydatki`, your support email. Scopes: `openid`, `email`, `profile` (non-sensitive, so
   no verification is needed). While the app is in **Testing**, add every invited person under
   **Test users** (max 100), or publish the app; the app's own invitation list still decides who
   gets in.
3. **Clients → Create client → Web application**, name `expense-insights`.
   - **Authorized JavaScript origins:** none needed (the browser never calls Google directly).
   - **Authorized redirect URIs:**
     - `http://localhost:4200/api/auth/callback/google` (`npm start`, proxied to the API)
     - `http://localhost:5080/api/auth/callback/google` (API on its own)
     - `https://expenses.gbrzeczek.dev/api/auth/callback/google` (production)
4. Copy the client id and secret.

### Locally (user-secrets)

```bash
cd src/backend
dotnet user-secrets --project ExpenseInsights.Api set "Auth:OwnerEmail" "you@gmail.com"
dotnet user-secrets --project ExpenseInsights.Api set "Auth:Google:ClientId" "<id>.apps.googleusercontent.com"
dotnet user-secrets --project ExpenseInsights.Api set "Auth:Google:ClientSecret" "<secret>"
```

Sign in at `http://localhost:4200`. Google only accepts `http` redirect URIs for `localhost`, so
signing in from a phone on the LAN (`http://<computer-ip>:4200`) needs the production HTTPS setup;
use the dev login there.

### Docker / production

For the dev stack, put the three values in `deploy/.env` (git-ignored, see `.env.example`). In
production they come from the GitHub environment `production`, and the Deploy workflow writes them
to the server's `deploy/.env` (see *Production setup*, step 8). In production Caddy terminates TLS, and
`docker-compose.prod.yml` sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the API trusts its
`X-Forwarded-Proto` and builds an `https` redirect URI (the API port is reachable only from Caddy
and from the server's loopback). Outside Development the session and antiforgery cookies are
`Secure`, so the app must be served over HTTPS; Caddy redirects HTTP and sends HSTS.

### How sessions work

- The session is an HttpOnly, SameSite=Lax cookie (`.ExpenseInsights.Session`, 30 days sliding)
  holding only the internal user id and role. Google tokens are not stored.
- Data Protection keys (which encrypt the cookie) live in the `DataProtectionKeys` table, so
  sessions survive restarts and are included in database backups. Deleting that table signs
  everyone out.
- State-changing requests need the `X-XSRF-TOKEN` header matching the `XSRF-TOKEN` cookie; the
  Angular app does this automatically.
