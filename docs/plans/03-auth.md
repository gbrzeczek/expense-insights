# 03 · Auth & users

**Status:** In review

## Goal

Sign in with Google, invite-only access for a handful of people, and strict per-user data
isolation.

## Design

- ASP.NET Core authentication: the Google OIDC handler with a **cookie session** (HttpOnly,
  Secure, SameSite=Lax). The SPA is served from the same origin, so there are no tokens in
  `localStorage` and no CORS.
- **Allow-list:** only email addresses in an `Invitation` table (seeded with the owner's address
  from configuration) can create an account. Everyone else gets a polite "brak dostępu"
  ("no access") page.
- `User` keyed by an internal GUID; `ExternalLogin(provider, subject)` links Google now and
  Apple later.
- `ICurrentUser` service; an EF Core global query filter on every `IUserOwned` entity.
- Roles: `Owner` (manages invitations), `User`.
- Antiforgery protection for state-changing requests (an Angular XSRF cookie/header pair).

## Tasks

- [x] Google Cloud OAuth client (redirect URIs for localhost and the production host). Setup
      steps documented in `deploy/README.md`.
- [x] `User`, `ExternalLogin`, `Invitation` entities and migration. `User` (with a seeded owner),
      `IUserOwned`, `ICurrentUser` and the query filter already exist from plan 02; replace
      `OwnerCurrentUser` in the API with the signed-in user and link the owner to their Google login.
- [x] Login/logout endpoints; `GET /api/me`.
- [x] Global query filter plus an **isolation test**: user A can't read, update or delete user B's
      transactions, through any endpoint (a parametrized integration test over all routes).
- [x] Owner-only invitations endpoint and a minimal screen.
- [x] Frontend: login screen (Polish), auth guard, 401 interceptor → login.
- [x] Account deletion: deletes all of a user's data (GDPR-style; these are other people's
      bank records).

## Acceptance criteria

- Owner logs in with Google on a phone; an uninvited Google account is rejected.
- The isolation test covers every user-owned entity and passes.
- No auth tokens are accessible to JavaScript.

## Open questions

- ~~Dev login: a fake auth handler enabled only in `Development` for e2e tests.~~ **Yes:**
  `GET /api/auth/dev-login?email=…&returnUrl=…`, mapped only in `Development`, signs in through
  the same allow-list (provider `dev`). The API integration tests sign in with it; the login page
  shows a small form for it in dev builds. The Playwright tests fake `GET /api/me` instead, since
  they run against `ng serve` only.

## As built

- **Flow.** `GET /api/auth/login/google?returnUrl=` challenges the OIDC handler (code + PKCE,
  `prompt=select_account`, `response_mode=query`). Its callback is `/api/auth/callback/google`
  (under `/api` so the Angular dev proxy forwards it). `UserSignIn` maps the Google identity
  (`sub`, verified `email`) to a user and the handler signs in with **our** principal (user id and
  role only, no Google tokens). Uninvited or unverified accounts are redirected to
  `/login?error=noAccess`; failures to `/login?error=failed`.
- **Owner linking.** At startup `Auth:OwnerEmail` becomes an invitation pointing at the seeded
  owner (`User.OwnerId`), so the owner's first Google sign-in links to it. An invited user gets a
  new `User` on first sign-in and the invitation records it.
- **Cookie.** `.ExpenseInsights.Session`: HttpOnly, SameSite=Lax, Secure outside Development,
  30 days sliding. It is re-checked against the database on each request, so a deleted user is
  signed out everywhere. API requests never redirect: 401 and 403 with problem details.
- **Antiforgery.** Every non-GET `/api` request needs `X-XSRF-TOKEN` matching the
  `XSRF-TOKEN` cookie (Angular's default pair). The token is user-bound and reissued on
  authenticated GETs, so it refreshes on `GET /api/me` after sign-in.
- **Invitations.** Owner-only `GET/POST /api/invitations`, `DELETE /api/invitations/{id}`. Only
  pending invitations can be revoked; the owner's comes from configuration.
- **Account deletion.** `DELETE /api/me` deletes the user's transactions, import batches,
  accounts, logins and invitation in one transaction and ends the session. The owner's account
  can't be deleted (it manages invitations). Coming back needs a new invitation.
- **Isolation test.** `RouteIsolationTests` lists every endpoint from the endpoint data source
  and fails if one has no case. Each case has user B use the route against user A's data and checks
  A's data is unchanged; every non-anonymous route also returns 401 without a session.
- **Not done here (follow-ups).** Removing an existing user as the owner (only pending invitations
  can be revoked; users delete themselves). Production cookie/HTTPS settings behind the tunnel
  are plan 05.
