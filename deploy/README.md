# Deployment

Production hardening (tunnel, backups, restore drill) is plan 05. This file covers what is in
place so far: the dev stack and the Google sign-in setup.

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
     - `https://<production host>/api/auth/callback/google` (plan 05)
4. Copy the client id and secret.

### Locally (user-secrets)

```bash
cd src/backend
dotnet user-secrets --project ExpenseInsights.Api set "Auth:OwnerEmail" "you@gmail.com"
dotnet user-secrets --project ExpenseInsights.Api set "Auth:Google:ClientId" "<id>.apps.googleusercontent.com"
dotnet user-secrets --project ExpenseInsights.Api set "Auth:Google:ClientSecret" "<secret>"
```

Sign in at `http://localhost:4200`. Google only accepts `http` redirect URIs for `localhost`, so
signing in from a phone on the LAN (`http://<computer-ip>:4200`) needs the HTTPS tunnel (plan 05);
use the dev login there.

### Docker / production

Put the three values in `deploy/.env` (git-ignored, see `.env.example`); `docker-compose.yml`
passes them to the `api` service. Behind a TLS-terminating proxy (Cloudflare Tunnel), also set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the API sees `https` and builds an `https` redirect
URI. Outside Development the session and antiforgery cookies are `Secure`, so the app must be
served over HTTPS.

### How sessions work

- The session is an HttpOnly, SameSite=Lax cookie (`.ExpenseInsights.Session`, 30 days sliding)
  holding only the internal user id and role. Google tokens are not stored.
- Data Protection keys (which encrypt the cookie) live in the `DataProtectionKeys` table, so
  sessions survive restarts and are included in database backups. Deleting that table signs
  everyone out.
- State-changing requests need the `X-XSRF-TOKEN` header matching the `XSRF-TOKEN` cookie; the
  Angular app does this automatically.
