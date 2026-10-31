# Implementation plans

Each plan is a self-contained unit of work with scope, tasks, acceptance criteria and open
questions. Work through them roughly in order. Update the checklist and status as tasks land.

| # | Plan | Status | Depends on |
|---|---|---|---|
| 01 | [Foundation](01-foundation.md): solution, Angular PWA shell, Compose, CI | Done | – |
| 02 | [ING import](02-ing-import.md): parser, normalization, dedup, pending → booked | Done | 01 |
| 03 | [Auth & users](03-auth.md): Google login, per-user data isolation | In review | 01 |
| 04 | [Rule-based classification & corrections](04-classification-rules.md) | In review | 02, 03 |
| 05 | [Deployment & ops](05-deployment.md): home server, Tailscale, backups | In progress (server steps left) | 03 |
| 06 | [Summaries & insights UX](06-insights-ux.md) | In review | 04 |
| 07 | [Similarity & embedding classification](07-classification-ml.md): kNN memory, embeddings, category anchors | In review | 04 |
| 08 | [Local LLM fallback](08-local-llm.md): chooser + Polish phrasing, ≤ 3B on 8 GB | Not started | 07 |
| 09 | [Cloud insights](09-cloud-insights.md): opt-in, aggregates only, prepaid | Not started | 06 |

**MVP = plans 01–05**: import ING, classify by rules, correct on the phone, and see a basic
monthly breakdown, running at home.

## Backlog (no plan yet)

- Receipts: e-receipt PDF and photo upload with OCR, attached to a transaction. No Biedronka
  scraping (see decision D8).
- Split transactions in the UI. The model supports them from plan 04.
- More banks (each gets its own plan and an `Import.<Bank>` project).
- English UI.
- Sign in with Apple.
- Budgets and alerts.
- Shared household view (two users, one combined summary).
