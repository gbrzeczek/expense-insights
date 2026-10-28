# 01 · Foundation

**Status:** Not started

## Goal

An empty but production-shaped skeleton: the backend solution, the Angular PWA shell,
Postgres in Docker Compose, CI running on every push, and Polish i18n wired up from the first
commit.

## Tasks

### Backend
- [ ] `src/backend/ExpenseInsights.sln` with projects `Api`, `Core`, `Infrastructure`,
      `Import.Ing`, `Classification`, and matching `tests/*.Tests` (xUnit).
- [ ] `Directory.Build.props`: `net10.0`, nullable, `TreatWarningsAsErrors`, implicit usings,
      analyzers. `Directory.Packages.props` for central package management.
- [ ] EF Core + Npgsql, `AppDbContext` in `Infrastructure`, first migration with the
      `pg_trgm` extension.
- [ ] Health endpoint `/health` (DB connectivity).
- [ ] ProblemDetails for errors; Serilog (or the built-in console logger) as JSON.
- [ ] Testcontainers-based integration test base class (real Postgres).

### Frontend
- [ ] `src/frontend`: Angular (latest stable), standalone, signals, strict mode, SCSS.
- [ ] `@angular/pwa`: manifest (name, icons, `theme_color`), service worker. App shell only.
      No offline data caching yet.
- [ ] Transloco with `pl` (default) and an `en` stub; a lint check that fails on hardcoded
      template text, if a reasonable tool exists.
- [ ] Mobile-first layout shell: bottom navigation (Przegląd / Transakcje / Kategorie /
      Import) on small screens, side navigation at ≥ 1024 px.
- [ ] Design tokens (CSS custom properties) with light and dark themes.
- [ ] Locale `pl-PL` registered; shared money and date pipes.
- [ ] Vitest unit tests; Playwright smoke test at a 390×844 viewport.

### Infra
- [ ] `deploy/docker-compose.yml` (dev): `db` (Postgres with pgvector image so plan 07
      needs no migration of images), optionally `api`.
- [ ] Production `Dockerfile` (multi-stage): builds Angular, serves it from the API's
      `wwwroot`. One origin.
- [ ] GitHub Actions: backend build + test, frontend lint + test + build. Cache NuGet and npm.
- [ ] Fill in the *Commands* section of `AGENTS.md`.

## Acceptance criteria

- `docker compose up` plus the documented run commands give a working shell on a phone over LAN.
- CI is green; one backend integration test hits real Postgres.
- The Lighthouse PWA "installable" check passes.
- The prod image is under ~250 MB, and the API idles below ~200 MB RAM.

## Open questions

- Component library: Angular Material (M3), or a lighter custom set? Decide once the UX
  references arrive. Default: Angular Material + CDK, themed.
