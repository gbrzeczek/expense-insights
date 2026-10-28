# 03 · Auth & users

**Status:** Not started

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

- [ ] Google Cloud OAuth client (redirect URIs for localhost and the production host). Setup
      steps documented in `deploy/README.md`.
- [ ] `User`, `ExternalLogin`, `Invitation` entities and migration.
- [ ] Login/logout endpoints; `GET /api/me`.
- [ ] Global query filter plus an **isolation test**: user A can't read, update or delete user B's
      transactions, through any endpoint (a parametrized integration test over all routes).
- [ ] Owner-only invitations endpoint and a minimal screen.
- [ ] Frontend: login screen (Polish), auth guard, 401 interceptor → login.
- [ ] Account deletion: deletes all of a user's data (GDPR-style; these are other people's
      bank records).

## Acceptance criteria

- Owner logs in with Google on a phone; an uninvited Google account is rejected.
- The isolation test covers every user-owned entity and passes.
- No auth tokens are accessible to JavaScript.

## Open questions

- Dev login: a fake auth handler enabled only in `Development` for e2e tests. Default: yes.
