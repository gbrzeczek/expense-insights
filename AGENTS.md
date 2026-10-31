# AGENTS.md

Guidance for AI coding agents (and humans) working in this repository.

## Project

**expense-insights**: a small-scale, self-hosted web app that imports bank CSV exports,
classifies transactions into categories, and shows clear, mobile-friendly spending
summaries and insights.

- Users: the owner plus a few invited people. Not a SaaS; optimise for simplicity.
- Banks: **ING Bank Śląski only** for now. The import layer is designed so other banks can be
  added later (see `docs/architecture.md` → *Bank import abstraction*). Don't add another bank
  without a plan in `docs/plans/`.
- UI language: **Polish first**. Every user-facing string goes through i18n; no hardcoded text.
- Runs on a home server: Intel i3-6100T (2C/4T, AVX2), 8 GB RAM (16 GB planned), Debian.
  Everything must stay lightweight. Assume a ~3 GB memory budget for the app stack, plus up to
  ~3 GB for the local LLM (plan 08, a ≤ 3B model).

Read `docs/architecture.md` and the relevant plan in `docs/plans/` before starting work.
For any UI work, also read `docs/design-system.md` (**Warm Paper Finance**): it is the
source of truth for colours, typography, spacing, components and UX writing.

## Stack

| Area | Choice |
|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core minimal APIs, EF Core |
| Database | PostgreSQL with `pg_trgm` (+ `pgvector` from plan 07) |
| Frontend | Angular (latest stable, standalone components, signals), PWA, mobile-first |
| Charts | Small CSS bar charts for now (D31); ECharts via `ngx-echarts` when a chart needs it |
| i18n | Transloco, default `pl`, `en` later |
| Auth | Google OIDC (Apple later: it needs a paid Apple Developer account) |
| Background work | `BackgroundService` + `System.Threading.Channels` |
| Local LLM | llama.cpp server (OpenAI-compatible API), small quantized model (≤ 3B) |
| Cloud LLM (opt-in) | Any OpenAI-compatible API with tool calling, prepaid (plan 09) |
| Tests | xUnit + Testcontainers (backend), Vitest (frontend), Playwright with a mobile viewport (e2e) |
| Deployment | Docker Compose on the home server, Caddy, reachable only via Tailscale |

## Repository layout (target)

```
src/
  backend/
    ExpenseInsights.slnx             # XML solution format (.NET 9+)
    ExpenseInsights.Api/             # HTTP endpoints, auth, composition root
    ExpenseInsights.Core/            # domain model, application services (no infra deps)
    ExpenseInsights.Infrastructure/  # EF Core, Postgres, external services
    ExpenseInsights.Import.Ing/      # ING CSV parser (one project per bank)
    ExpenseInsights.Classification/  # rules, similarity, embeddings, LLM fallback
    tests/
      ExpenseInsights.*.Tests/
      ExpenseInsights.Testing/       # shared test helpers (Testcontainers Postgres fixture)
  frontend/                          # Angular PWA
test-data/                           # FAKE data + generator only
docs/
  architecture.md
  plans/                             # numbered implementation plans
deploy/                              # docker-compose (dev + prod), Caddy, backup/deploy scripts
```


## Hard rules

1. **Never commit real bank data.** Only generated fake data goes in `test-data/`.
   `.gitignore` blocks `*.csv` everywhere else; don't work around it. Don't paste real
   transactions into code, tests, docs, commit messages or issues.
2. **Money is `decimal`** (`numeric(14,2)` in Postgres), always paired with a currency. Never
   `double`/`float`. Signs: expenses are negative, income positive, matching the bank.
3. **Dates are `DateOnly`** for transaction/booking/purchase dates. Use `DateTimeOffset` (UTC)
   only for system timestamps.
4. **Bank-specific logic stays in `Import.<Bank>` projects.** Everything downstream works on the
   normalized `ImportedTransaction` model and must not know which bank a row came from.
5. **Keep raw rows.** Store the original CSV row (as JSON) with every transaction so it can be
   re-parsed when the parser improves.
6. **Numbers come from code, never from an LLM.** All amounts, sums, percentages and
   comparisons are computed in code. An LLM may pick a category from a fixed list, decide what
   to compute through fixed query tools, and write free text, but every number it shows is
   inserted by code from a computed result (placeholders). Text with a digit the model typed
   is rejected.
7. **Every query is scoped to the current user.** Use an EF Core global query filter; add a test
   whenever you add a new user-owned entity.
8. **No secrets in the repo.** Use user-secrets locally and environment variables or Docker
   secrets in deployment.
9. **Don't log account numbers or full card numbers.**

## Conventions

- Code, identifiers, comments, commits and docs are in **English**. UI text is in Polish via
  i18n keys (`transactions.list.empty`, etc.). URL routes, query parameters, CSS classes and
  i18n keys are English too (`/transactions`, not `/transakcje`); only displayed values are Polish.
- Format Polish locale explicitly (`pl-PL`): `1 234,56 zł`, `28.10.2026`.
- Prefer small, boring solutions. Add a dependency only when it clearly earns its place;
  mention it in the PR description.
- Backend: nullable enabled, warnings as errors, file-scoped namespaces, `sealed` by default.
- Frontend: standalone components, signals for state, `OnPush`, no NgModules. Design for a
  360 px width first; touch targets ≥ 44 px; no hover-only interactions.
- Styling: only design tokens from `src/frontend/src/styles/_tokens.scss` (no raw hex values or
  ad-hoc font sizes in components). Serif (`--font-editorial`) only for rare headings; every
  amount uses the UI sans with tabular numerals. Never mark expenses as bad with red; show
  direction with signs, words or icons, not colour alone.
- Tests: every parser quirk gets a test. Parser and classification tests run against
  `test-data/fake_ing.csv`; classification accuracy is measured against
  `test-data/fake_ing.labels.csv`.
- Commits: Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
- Update the plan's checklist and status in the same PR that completes a task. If you change an
  architectural decision, update `docs/architecture.md` (decision log) too.

## Commands

Prerequisites: .NET SDK 10 (`src/backend/global.json`), Node 24 (`src/frontend/.nvmrc`), and a
Docker engine (Docker Desktop, Rancher Desktop with the moby engine, or Docker on Linux). Docker
is needed for the dev database and for the backend integration tests.

### Dev stack

```bash
cp deploy/.env.example deploy/.env    # optional; the defaults match appsettings.Development.json
docker compose -f deploy/docker-compose.yml up -d db
dotnet run --project src/backend/ExpenseInsights.Api    # http://localhost:5080, migrates on start
npm --prefix src/frontend start                         # http://localhost:4200, proxies /api and /health
```

Sign-in: set `Auth:OwnerEmail` (and the Google client id/secret) with user-secrets as described in
`deploy/README.md`. Without Google credentials, sign in with the Development-only dev login on the
login page (any invited address; the owner's address is invited automatically).

`npm start` listens on `0.0.0.0`, so a phone on the same LAN can open `http://<computer-ip>:4200`.
The service worker only registers on `localhost` or HTTPS, so install/offline checks need one of
those (production has HTTPS, see `deploy/README.md`).

### Backend (`src/backend`)

```bash
dotnet build                     # warnings are errors
dotnet test                      # unit + Testcontainers integration tests (needs Docker)
dotnet tool restore              # once, for dotnet-ef
dotnet ef migrations add <Name> -p ExpenseInsights.Infrastructure -s ExpenseInsights.Api -o Persistence/Migrations
```

Tests run on Microsoft.Testing.Platform (xUnit v3); `global.json` opts `dotnet test` into it.
Import speed benchmarks are explicit tests (skipped by default and in CI):
`dotnet test --project tests/ExpenseInsights.Infrastructure.Tests -c Release -- --explicit only`.
Classification accuracy on the fake data (coverage, precision, per tier, confusion):
`dotnet run --project ExpenseInsights.Classification.Eval -- --errors`. Pass `--csv` and `--labels`
for a real export plus the labels from `GET /api/classification/labels`, kept outside the repo.

### Frontend (`src/frontend`)

```bash
npm ci
npm run lint                     # ESLint, including the "no hardcoded template text" i18n rule
npm run format:check             # Prettier (npm run format to fix)
npm test                         # Vitest
npm run build                    # production build into dist/
npx playwright install chromium  # once
npm run e2e                      # Playwright smoke test at 390×844 (starts ng serve)
npm run icons                    # re-render PWA icons + favicon.ico from branding/*.svg
```

The design system's style guide is at `http://localhost:4200/styleguide` (dev builds only).

### Production image

```bash
docker compose -f deploy/docker-compose.yml --profile api up -d --build   # db + api on :8080
docker build -t expense-insights .                                         # image only
```

### Test data

```bash
python3 test-data/generate_ing_csv.py --months 6 --seed 42 --out test-data/fake_ing.csv
python3 test-data/generate_ing_csv.py --months 2 --seed 10 --overlap --out test-data/fake_ing_overlap.csv
```

The second command writes two overlapping exports (`fake_ing_overlap_a.csv`, `_b.csv`) for the
dedup and pending → booked tests.
