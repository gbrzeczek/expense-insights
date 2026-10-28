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
  Everything must stay lightweight. Assume a ~3 GB memory budget for the app stack, excluding
  the optional local LLM.

Read `docs/architecture.md` and the relevant plan in `docs/plans/` before starting work.

## Stack

| Area | Choice |
|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core minimal APIs, EF Core |
| Database | PostgreSQL with `pg_trgm` (+ `pgvector` from plan 06) |
| Frontend | Angular (latest stable, standalone components, signals), PWA, mobile-first |
| Charts | ECharts via `ngx-echarts` |
| i18n | Transloco, default `pl`, `en` later |
| Auth | Google OIDC (Apple later: it needs a paid Apple Developer account) |
| Background work | `BackgroundService` + `System.Threading.Channels` |
| Local LLM (optional) | llama.cpp server (OpenAI-compatible API), small quantized model |
| Tests | xUnit + Testcontainers (backend), Vitest (frontend), Playwright with a mobile viewport (e2e) |
| Deployment | Docker Compose on the home server, Cloudflare Tunnel or Tailscale |

## Repository layout (target)

```
src/
  backend/
    ExpenseInsights.sln
    ExpenseInsights.Api/             # HTTP endpoints, auth, composition root
    ExpenseInsights.Core/            # domain model, application services (no infra deps)
    ExpenseInsights.Infrastructure/  # EF Core, Postgres, external services
    ExpenseInsights.Import.Ing/      # ING CSV parser (one project per bank)
    ExpenseInsights.Classification/  # rules, similarity, ML, LLM fallback
    tests/
      ExpenseInsights.*.Tests/
  frontend/                          # Angular PWA
test-data/                           # FAKE data + generator only
docs/
  architecture.md
  plans/                             # numbered implementation plans
deploy/                              # docker-compose, tunnel config, backup scripts
```

Until plan 01 is done, only `docs/` and `test-data/` exist.

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
6. **Numbers are never produced by an LLM.** All amounts, sums, percentages and comparisons
   are computed in code. An LLM may only pick a category from a fixed list, or put
   pre-computed facts into words.
7. **Every query is scoped to the current user.** Use an EF Core global query filter; add a test
   whenever you add a new user-owned entity.
8. **No secrets in the repo.** Use user-secrets locally and environment variables or Docker
   secrets in deployment.
9. **Don't log account numbers or full card numbers.**

## Conventions

- Code, identifiers, comments, commits and docs are in **English**. UI text is in Polish via
  i18n keys (`transactions.list.empty`, etc.).
- Format Polish locale explicitly (`pl-PL`): `1 234,56 zł`, `28.10.2026`.
- Prefer small, boring solutions. Add a dependency only when it clearly earns its place;
  mention it in the PR description.
- Backend: nullable enabled, warnings as errors, file-scoped namespaces, `sealed` by default.
- Frontend: standalone components, signals for state, `OnPush`, no NgModules. Design for a
  360 px width first; touch targets ≥ 44 px; no hover-only interactions.
- Tests: every parser quirk gets a test. Parser and classification tests run against
  `test-data/fake_ing.csv`; classification accuracy is measured against
  `test-data/fake_ing.labels.csv`.
- Commits: Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
- Update the plan's checklist and status in the same PR that completes a task. If you change an
  architectural decision, update `docs/architecture.md` (decision log) too.

## Commands

Filled in by plan 01. Until then:

```bash
python3 test-data/generate_ing_csv.py --months 6 --seed 42 --out test-data/fake_ing.csv
```
