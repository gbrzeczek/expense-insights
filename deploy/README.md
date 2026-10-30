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

- [Production setup](#production-setup-once-on-the-server) · [Deploying](#deploying) ·
  [Backups](#backups) · [Restore drill](#restore-drill) · [Disaster recovery](#disaster-recovery) ·
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

## Production setup (once, on the server)

Run these on `debianhome` as the user that will own the deployment (it must be in the `docker`
group). Paths assume the repository is cloned to `~/expense-insights`.

### 1. Packages and the repository

```bash
sudo apt install git curl age rclone
git clone https://github.com/gbrzeczek/expense-insights.git ~/expense-insights
```

Docker Engine with the Compose plugin is assumed to be there already (the tunnel container runs
on it). Check with `docker compose version`.

### 2. The Tailscale address, and binding to it at boot

```bash
tailscale ip -4                       # e.g. 100.101.102.103, goes into TAILSCALE_IP
```

Docker binds the published ports when the containers start. At boot Docker can start before
Tailscale has brought the address up, and then Caddy's ports fail to bind. Allow binding to an
address that isn't up yet:

```bash
echo 'net.ipv4.ip_nonlocal_bind = 1' | sudo tee /etc/sysctl.d/60-expense-insights.conf
sudo sysctl --system
```

Nothing else may listen on port 443 or 80 on all addresses (`0.0.0.0`); check with
`sudo ss -ltnp '( sport = :443 or sport = :80 )'`. The `cloudflared` container makes only
outbound connections, so it doesn't conflict.

### 3. DNS record and Cloudflare API token

In the Cloudflare dashboard for `gbrzeczek.dev`:

1. **DNS → Records → Add record:** type `A`, name `expenses`, IPv4 = the Tailscale address,
   **Proxy status: DNS only** (grey cloud). Proxied would not work: Cloudflare can't reach a
   tailnet address.
2. **My Profile → API Tokens → Create Token → Edit zone DNS** template: permission
   `Zone · DNS · Edit`, zone resources `Include · Specific zone · gbrzeczek.dev`. Copy the
   token into `CLOUDFLARE_API_TOKEN`. It can change DNS records of this one zone and nothing else.

Some routers block DNS answers that point at private ranges ("DNS rebinding protection"). Phones
on the Tailscale app use Tailscale's resolver, so this usually doesn't matter; if the name doesn't
resolve on the home Wi-Fi, allow `gbrzeczek.dev` in the router or enable MagicDNS.

### 4. Google OAuth redirect URI

In the Google Cloud client (see [Google Cloud OAuth client](#google-cloud-oauth-client)), add
`https://expenses.gbrzeczek.dev/api/auth/callback/google` to **Authorized redirect URIs**.

### 5. An `age` key for the backups

On your laptop, not the server:

```bash
age-keygen -o expense-insights-backup.key
# Public key: age1...   ← goes into AGE_RECIPIENT on the server
```

Keep the private key file in your password manager and on one offline copy (a USB stick in a
drawer). The server only ever has the public key, so a stolen server or a leaked R2 bucket doesn't
expose the backups. Without the private key the backups can't be read; there is no recovery.

### 6. Cloudflare R2 bucket and rclone

1. **R2 → Create bucket:** `expense-insights-backups`, location automatic. Leave public access off.
2. **R2 → Manage API tokens → Create API token:** permission **Object Read & Write**, applied to
   **this bucket only**. Note the access key id, the secret access key and the S3 endpoint
   (`https://<account id>.r2.cloudflarestorage.com`).
3. On the server:

   ```bash
   rclone config create r2 s3 provider=Cloudflare \
     access_key_id=<access key id> secret_access_key=<secret access key> \
     endpoint=https://<account id>.r2.cloudflarestorage.com acl=private no_check_bucket=true
   rclone lsf r2:expense-insights-backups      # empty output, no error
   ```

   `no_check_bucket` is needed because a bucket-scoped token may not list buckets. The config is
   written to `~/.config/rclone/rclone.conf` (mode 600), outside the repository.
4. Set `RCLONE_REMOTE=r2:expense-insights-backups`.

The backups stay well inside R2's free 10 GB (a few hundred KB per dump for years of history).

### 7. `deploy/.env`

```bash
cd ~/expense-insights
cp deploy/.env.prod.example deploy/.env
chmod 600 deploy/.env
openssl rand -base64 32 | tr -d '/+='     # → POSTGRES_PASSWORD
sudo install -d -o "$USER" -m 700 /var/backups/expense-insights
```

Fill in every value; Compose refuses to start while a required one is empty. Keep a copy of the
filled-in file in your password manager: it isn't in the backups.

### 8. First start

```bash
docker compose -f deploy/docker-compose.prod.yml up -d --build
docker compose -f deploy/docker-compose.prod.yml logs -f caddy   # wait for "certificate obtained successfully"
curl -fsS https://expenses.gbrzeczek.dev/health                 # Healthy
deploy/systemd/install.sh                                         # nightly backup + uptime timers
deploy/scripts/backup.sh                                          # first backup now
```

The API migrates the database on start. The owner (`AUTH_OWNER_EMAIL`) signs in with Google and
invites the others from the app.

### 9. Phones and invited people

- **You:** install Tailscale on the phone and sign in to your tailnet. Open
  `https://expenses.gbrzeczek.dev`, sign in with Google, then install the app (Android Chrome: menu
  → *Install app*; iPhone Safari: Share → *Add to Home Screen*).
- **Someone else:** invite their Google address in the app and, while the OAuth consent screen is
  in *Testing*, add it under *Test users*. Then either share the machine with them (Tailscale admin
  console → **Machines → debianhome → Share**; they accept with their own Tailscale account and
  see only this machine) or invite them to your tailnet. They install the Tailscale app and open the
  same address.

## Deploying

From the laptop:

```bash
ssh debianhome '~/expense-insights/deploy/scripts/deploy.sh'
```

`deploy.sh` pulls `main` (fast-forward only), builds the images, takes a backup (the API runs
migrations on start), restarts what changed and waits for `/health`. It prunes dangling images
afterwards. `--no-backup` skips the backup. While the API restarts, Caddy holds requests for up to
30 seconds instead of failing them.

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

Run it by hand with `deploy/scripts/backup.sh`; see past runs with
`journalctl -u expense-insights-backup`.

What the dump contains: every table, including the Data Protection keys, so sessions survive a
restore. What it doesn't: `deploy/.env` and the rclone config (keep them in the password manager),
and Caddy's certificates (re-issued automatically).

## Restore drill

`scripts/restore-drill.sh` restores a backup into a throwaway Postgres container (in memory) and
compares every table with the live database: the row count and an MD5 over all rows. Production is
not touched. Do it once now, and again after anything changes in the backup chain.

```bash
cd ~/expense-insights
deploy/scripts/backup.sh                                   # fresh, so live and backup match
# Off-site copy, to prove R2 and the key work, not just the local file:
rclone copy "r2:expense-insights-backups/daily/expense-insights-$(date +%F).dump.age" /tmp/drill/
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

New or rebuilt server: steps 1–8 of the setup (the same `deploy/.env` from the password manager),
but start only the database before restoring, so the API doesn't create an empty schema first:

```bash
docker compose -f deploy/docker-compose.prod.yml up -d --build db
rclone copy r2:expense-insights-backups/daily/<newest file> /tmp/restore/
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

- [ ] Steps 1–9 above, with the real Cloudflare token, R2 bucket, `age` key and Google redirect URI.
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

Put the three values in `deploy/.env` (git-ignored; see `.env.example` for the dev stack and
`.env.prod.example` for production). In production Caddy terminates TLS, and
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
