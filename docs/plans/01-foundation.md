# 01 · Foundation

**Status:** In review

## Goal

An empty but production-shaped skeleton: the backend solution, the Angular PWA shell,
Postgres in Docker Compose, CI running on every push, and Polish i18n wired up from the first
commit.

## Tasks

### Backend
- [x] `src/backend/ExpenseInsights.sln` with projects `Api`, `Core`, `Infrastructure`,
      `Import.Ing`, `Classification`, and matching `tests/*.Tests` (xUnit).
- [x] `Directory.Build.props`: `net10.0`, nullable, `TreatWarningsAsErrors`, implicit usings,
      analyzers. `Directory.Packages.props` for central package management.
- [x] EF Core + Npgsql, `AppDbContext` in `Infrastructure`, first migration with the
      `pg_trgm` extension.
- [x] Health endpoint `/health` (DB connectivity).
- [x] ProblemDetails for errors; Serilog (or the built-in console logger) as JSON.
- [x] Testcontainers-based integration test base class (real Postgres).

### Frontend
- [x] `src/frontend`: Angular (latest stable), standalone, signals, strict mode, SCSS.
- [x] `@angular/pwa`: manifest (name, icons, `theme_color`), service worker. App shell only.
      No offline data caching yet.
- [x] Transloco with `pl` (default) and an `en` stub; a lint check that fails on hardcoded
      template text, if a reasonable tool exists.
- [x] Mobile-first layout shell: bottom navigation (Przegląd / Transakcje / Kategorie /
      Import) on small screens, side navigation at ≥ 1024 px.
- [x] Design tokens (CSS custom properties) with light and dark themes, following
      [`docs/design-system.md`](../design-system.md) (Warm Paper Finance).
- [x] Locale `pl-PL` registered; shared money and date pipes.
- [x] Vitest unit tests; Playwright smoke test at a 390×844 viewport.

### Infra
- [x] `deploy/docker-compose.yml` (dev): `db` (Postgres with pgvector image so plan 07
      needs no migration of images), optionally `api`.
- [x] Production `Dockerfile` (multi-stage): builds Angular, serves it from the API's
      `wwwroot`. One origin.
- [x] GitHub Actions: backend build + test, frontend lint + test + build. Cache NuGet and npm.
- [x] Fill in the *Commands* section of `AGENTS.md`.

## Acceptance criteria

- [x] `docker compose up` plus the documented run commands give a working shell on a phone over LAN.
      Verified over the LAN address (prod image on `:8080`, dev server on `:4200`).
- [ ] CI is green; [x] one backend integration test hits real Postgres (Testcontainers:
      migrations + `pg_trgm`, `/health`, ProblemDetails).
- [x] The Lighthouse PWA "installable" check passes. Lighthouse 12 dropped the PWA category, so
      this is checked with Chrome's own installability API (`Page.getInstallabilityErrors`):
      no errors, service worker active.
- [x] The prod image is under ~250 MB (172 MB unpacked; CI fails above 250 MB), and the API
      idles below ~200 MB RAM (~58 MiB measured).

## Notes

- Tests use xUnit v3 on Microsoft.Testing.Platform; `src/backend/global.json` opts `dotnet test`
  into it. Empty test projects ignore exit code 8 ("zero tests") until their first test lands.
- Polish locale: `MoneyPipe` uses Angular's CLDR data, which groups 4-digit amounts
  (`1 234,56 zł`) as required, unlike `Intl.NumberFormat('pl-PL')`.
- The runtime image is `aspnet:10.0-noble-chiseled-extra` (distroless, non-root, with ICU and
  tzdata for `pl-PL`). The SPA is copied into `wwwroot` before `dotnet publish`, so
  `MapStaticAssets` fingerprints and pre-compresses it (Brotli/gzip).
- `/styleguide` (dev builds only) shows the design system on sample data.

## Open questions

- ~~Component library: Angular Material (M3), or a lighter custom set?~~ Decided: the UX
  references arrived as `docs/design-system.md`; a small custom set on design tokens, no
  Material for now (decision D13). Add `@angular/cdk` for overlays and a11y helpers when needed.
