# Architecture

## Overview

```
 Phone / desktop browser (Angular PWA, pl-PL)
              │  HTTPS via Tailscale → Caddy (TLS)
              ▼
 ┌──────────────────────── home server (Docker Compose) ────────────────────────┐
 │  ExpenseInsights.Api (ASP.NET Core)                                          │
 │    ├─ Auth (Google OIDC → cookie session)                                    │
 │    ├─ Import  ── IStatementParser ── Import.Ing                              │
 │    ├─ Classification pipeline (rules → memory → embeddings → the user)       │
 │    ├─ Insights (deterministic aggregations)                                  │
 │    └─ BackgroundService worker (classification)                              │
 │                         │                                                    │
 │                         ▼                                                    │
 │                 PostgreSQL (+pg_trgm, pgvector)                              │
 └──────────────────────────────────────────────────────────────────────────────┘
```

One deployable API (a modular monolith) plus Postgres. No local LLM (D34). No message
broker or microservices: the load is a few users importing a few hundred rows a month.

## Bank import abstraction

Only ING is implemented. The seam for other banks:

```csharp
public interface IStatementParser
{
    string BankId { get; }                       // "ing-pl"
    // Cheap sniffing on the first bytes/lines: header signature, delimiter, encoding.
    ParserMatch Detect(ReadOnlySpan<byte> head);
    IAsyncEnumerable<ParseResult> ParseAsync(Stream csv, CancellationToken ct);
}
```

`ParseResult` is one of: `ParsedStatement` (always first: the statement's accounts, period,
declared totals and document number), `ParsedTransaction` (an `ImportedTransaction`), a row-level
`ParseError`, or a `ParseWarning`. Errors and warnings are an `ImportIssue`: a code plus
parameters (line, value, amounts, dates) that the UI words through i18n. One bad row never fails
the whole import; a file that can't be read at all throws `StatementFormatException` with a code.

`StatementImporter` (Infrastructure) is the bank-agnostic pipeline: SHA-256 file dedup, parser
detection, account upsert, totals and balance checks, period-gap warning, dedup by
`(AccountId, ExternalId)`, pending → booked promotion, expiry, and own-transfer pairing.

`ImportedTransaction` is the **bank-agnostic** model everything downstream uses:

| Field | Notes |
|---|---|
| `BankId`, `AccountRef` | `AccountRef` = the bank's account label/number (masked) |
| `ExternalId?` | Bank transaction id; **null for pending rows** |
| `Status` | `Pending` \| `Booked` |
| `TransactionDate`, `BookingDate?`, `PurchaseDate?` | `PurchaseDate` is parsed from the title for card payments |
| `Amount`, `Currency` | Account currency, signed |
| `OriginalAmount?`, `OriginalCurrency?` | Foreign-currency payments |
| `Kind` | `Card`, `BlikOnline`, `BlikP2P`, `Transfer`, `PhoneTransfer`, `OwnTransfer`, `Other` |
| `CounterpartyRaw`, `TitleRaw` | As in the file |
| `CounterpartyName`, `MerchantHint?` | Cleaned name; for BLIK via a payment processor, the shop domain from the title |
| `CounterpartyAccount?`, `CounterpartyBank?` | |
| `BalanceAfter?` | Used for import sanity checks |
| `RawRow` | Original columns, stored as JSONB |

Adding a bank means a new `Import.<Bank>` project, fake-data fixtures, and DI registration.
Nothing else changes.

## Core data model

```
User ─┬─ Account ── Transaction ──┬── Classification (current category, source, confidence)
      │                           ├── ClassificationHistory (audit, training data)
      │                           └── Split[] (optional: one payment → several categories)
      ├─ ImportBatch (file hash, counts, errors)
      ├─ MerchantRule (pattern → category, per user; created from corrections)
      ├─ Category (the user's own subcategories) + CategorySetting (rename/hide of a global one)
      └─ ClassificationHistory, TransactionSplit

Category (global taxonomy, seeded)    global-rules.json (global merchant rules, shipped with the app)
```

- **Category taxonomy:** fixed, two levels, about 12 top-level categories, seeded globally.
  Users can rename or hide categories and add subcategories, but not change top-level semantics
  (this keeps ML and insights consistent). Special categories that are excluded from spending:
  `Przelew własny`, `Zwroty`, and income categories.
- **Classification source:** `UserCorrection` > `UserRule` > `GlobalRule` > `Similarity` (tier A) >
  `Embedding` (tier C) > `Unclassified`. A `UserCorrection` is never
  recomputed; every other source is re-derived when the rules or the user's labels change.

## Classification pipeline

1. **Normalize.** Strip store numbers, cities, country codes and the `JMP S.A.` style legal
   prefixes, giving a merchant key (`JMP S.A. BIEDRONKA 6537 KRAKOW PO` → `biedronka`). For BLIK
   through processors (PayU, Cashbill, PayPro, Przelewy24), use the shop domain from the title.
2. **Rules.** Own accounts and refunds, the user's rules, the account holder's own name (for
   review), transfers to and from people, then global merchant and title-keyword rules; trigram
   similarity only when nothing matched (see plan 04, *As built*).
3. **Memory (tier A).** kNN over the merchant keys the user labelled (their corrections and what
   their rules classified), char 3–4-gram TF-IDF, k = 5, same direction of money only, a neighbour
   of another kind (card payment vs transfer) at 0.6. No training; every correction is another
   example and reclassifies the user's open transactions in the background. Explains itself:
   "Podobne do: „…”" (plan 07).
4. **Embeddings (tier C).** `multilingual-e5-small` (int8 ONNX, ONNX Runtime) for what A isn't sure
   of: the merchant key against the category anchors (each category's name and the kinds of
   merchant it covers, in `global-rules.json`) and against the user's labelled merchants, whose
   embeddings are stored in `pgvector`. Confidence from the margin over the second-best category.
   Loaded on the first batch, unloaded after 2 minutes idle. It doesn't know brands, so it helps
   with descriptive local names (`APTEKA POD ORLEM`, `SALON FRYZJERSKI`), not chains (plan 07).
5. **User correction.** What's left after the tiers (ambiguous texts and brands the model can't
   place, a few rows a month on fake data) waits in "Do sprawdzenia" or the by-merchant list; no
   local LLM (D34). Each correction saves the category, offers "apply to all from this
   merchant", and creates or updates a `MerchantRule`; tier A learns from it at once.

Accuracy is measured offline against `test-data/fake_ing.labels.csv` and reported per tier.

## Deployment

Docker Compose (`deploy/docker-compose.prod.yml`): `caddy`, `api` and `db`. The Angular build is served by the API as static files,
so there is one origin and no CORS. The app is reachable only inside the tailnet: Caddy listens on
the server's Tailscale address and terminates TLS for `expenses.gbrzeczek.dev`, a Cloudflare
DNS-only record pointing at that address, with a Let's Encrypt certificate from the DNS-01
challenge. No open ports, nothing on the LAN. Nightly `pg_dump`, `age`-encrypted, kept locally and
copied to Cloudflare R2. Merges to `main` deploy themselves: GitHub Actions builds the images, pushes
them to GHCR and deploys over SSH through Tailscale, with settings from a GitHub environment.
Setup, backups, restore and monitoring: `deploy/README.md`.

## Decision log

| # | Decision | Why |
|---|---|---|
| D1 | .NET + Angular | Portfolio value on the Polish job market; strong typing end to end |
| D2 | Modular monolith, single API | Tiny load, 8–16 GB home server |
| D3 | PostgreSQL over SQLite | `pg_trgm` and `pgvector`; better portfolio signal |
| D4 | ING only, behind `IStatementParser` | Ship one bank well; keep the seam for more |
| D5 | ~~Rules → similarity → LLM, LLM last~~ → **rules → memory → embeddings → the user** (revised, D34) | Old CPU; merchant strings are repetitive; small models are unreliable |
| D6 | ~~LLM never computes numbers~~ → **numbers come from code; an LLM may choose what to compute (fixed query tools) and write free text, with numbers inserted by code** (revised, D29) | Models slip on arithmetic and sound just as sure; code-inserted numbers are cheap and always right |
| D7 | Google login first, Apple later | Sign in with Apple requires a paid developer account |
| D8 | No Biedronka scraping | No public API, ToS risk, would require holding users' credentials; receipt upload is possible later |
| D9 | PWA, not native | One codebase, mobile install without app stores |
| D10 | ~~One account per file~~ → **multiple accounts per file, one bank** (revised) | The real ING export lists current and savings accounts together; both legs of own transfers make excluding them reliable |
| D11 | P2P transfers are spending by default; can be recategorized, ruled by recipient account, split, or marked as reimbursements | Shared bills would otherwise overstate both spending and income |
| D12 | Discard the preamble's user name and address on import | It's PII we don't need |
| D13 | Warm Paper Finance design system (`docs/design-system.md`), self-hosted Inter + Source Serif 4, no component library for now | Calm, editorial, trustworthy look; fonts bundled so the PWA works offline and makes no third-party requests |
| D14 | `User` + `ICurrentUser` + query filter from plan 02, with a seeded owner until sign-in (plan 03) | Transactions need an owner from the first import; hard rule 7 applies from day one |
| D15 | The user's own account numbers are stored only as a mask and a SHA-256 hash | Enough to identify accounts and recognise own transfers without keeping the numbers |
| D16 | Google OIDC → our own cookie session (HttpOnly, SameSite=Lax), allow-list of invited emails; users keyed by GUID with `ExternalLogin(provider, subject)` | No tokens in JavaScript, no CORS (one origin); Apple can be added as another login |
| D17 | Antiforgery for the SPA via Angular's `XSRF-TOKEN` cookie / `X-XSRF-TOKEN` header on every state-changing `/api` request | Built into both frameworks; SameSite=Lax alone doesn't cover every case |
| D18 | Data Protection keys stored in Postgres (`DataProtectionKeys`) | Sessions survive restarts and redeploys; the keys are in the same backups, no extra volume |
| D19 | Account deletion erases all of a user's rows (and their invitation); the owner's account can't be deleted | GDPR-style erasure of other people's bank records; the owner manages invitations |
| D20 | Only accounts in the export make an own transfer; the user's name with another account is a guess for review (incoming → `Inne przychody`, outgoing → `Przelew własny`), settled by a rule per account | A business account isn't in the ING export, and money from it is income, not a move between own accounts |
| D21 | Global merchant rules ship as an embedded JSON file; only user rules are rows | A new rule list ships with a deploy, needs no migration, and is reviewed in diffs |
| D22 | Classification runs in a background `BackgroundService` fed by a channel after import; `xmin` guards against overwriting a concurrent correction | The import returns at once; no broker needed for this load (D2) |
| D23 | ~~Cloudflare Tunnel~~ → **Tailscale only**, under a Cloudflare DNS-only name pointing at the server's tailnet address; Caddy bound to that address with a Let's Encrypt certificate via the Cloudflare DNS-01 challenge (revised) | Bank data shouldn't be on the public internet; a real name and certificate keep Google sign-in and PWA install working; no open port is needed for the certificate |
| D24 | Backups: `pg_dump` custom format piped into `age` (public key only on the server), 14 daily + 6 monthly, `rclone copy` (never sync) to Cloudflare R2, run by a host systemd timer | No extra container; a stolen server or bucket doesn't expose data; a wiped disk can't delete the off-site copies |
| D25 | Monitoring by dead man's switch: systemd timers check `/health` through Caddy and ping healthchecks.io, instead of Uptime Kuma | An outside checker can't reach a tailnet-only host; pings stopping also catches the whole server being down; no extra container |
| D26 | Insights use robust statistics (median baseline, MAD spread, seasonal comparison, day-of-month pace profile), not ML | A few hundred monthly data points per user is too little to train on; every number must be explainable and lead to its transactions |
| D27 | Classification after rules: kNN memory over corrections + local embeddings with category anchors; no trained ML.NET model; ~~the local LLM (plan 08) handles what is left~~ the user resolves what is left (revised, D34) | Both tiers work from the first correction with no training step; anchors cover cold start |
| D28 | ~~The local LLM (plan 08) is required: a ≤ 3B quantized model on the current 8 GB, used as a chooser (constrained output, log-probability confidence) and for phrasing; the app works unchanged when its container is down | No transaction text leaves the server; brand knowledge covers what embeddings miss; 16 GB only if a bigger model clearly wins~~ Superseded by D34 |
| D29 | Cloud insights (plan 09): opt-in per user, category-level monthly aggregates only, the model explores through fixed query tools and writes with placeholders that code fills; OpenAI-compatible client on a prepaid provider plus app-side caps | More interesting insights than fixed rules, without trusting model arithmetic or sending merchants; a few users cost cents a month |
| D30 | Deploys from GitHub Actions after CI on `main` (trigger revised, D35): images built on GitHub and pushed to GHCR by commit; the job joins the tailnet as `tag:ci` and deploys over SSH with the server's shared deploy key (~~restricted to one forced command~~, revised: a plain shell, like sheetsolver's); settings live in the GitHub environment `production` and each deploy writes the server's `.env` | Same pattern as sheetsolver on the same server; builds on fast runners, not the i3; one place to edit settings; no open port; one deploy key for the server's apps, accepted because SSH is reachable only from `tag:ci` in the tailnet |
| D31 | Plan 06 charts are small hand-made CSS bar charts (`month-bars`, `category-breakdown`); ECharts (`ngx-echarts`) is deferred until a chart needs it | Two chart shapes with direct labels and tappable bars; a ~1 MB dependency didn't earn its place |
| D32 | Insights are computed on each request from the user's ledger in memory, never stored; k = 3 and the large-payment bar is the 12-month maximum (tuned on fake data, plan 06 *As built*) | A few thousand rows per user; nothing to keep in sync after a correction; every number stays traceable to its transactions |
| D33 | Both plan 07 tiers stay (measured, plan 07 *Results*). Tier C: `multilingual-e5-small` int8 ONNX from the model's repository at a pinned revision and hash, built into the image (downloaded on first use elsewhere), never committed; anchors are kinds of merchant, not brands; embeddings stored per labelled merchant key in `pgvector` with an exact per-user scan, no HNSW index; the api container's limit raised to 1 GB and the image budget to 400 MB (~330 MB: only this platform's ONNX Runtime library is kept) | A covers merchants the user labelled at 100% precision on fake data; C adds descriptive local shops (50% → 84% classified without review on the fake local merchants) for ~270 MB while loaded; brand names matched by spelling (lidl ~ tidal), so brand anchors made it worse; a filtered HNSW scan can miss a user's few hundred rows, an exact one can't |
| D34 | No local LLM: plan 08 is dropped. After the rules, memory (A) and embeddings (C), what's left goes to the user ("Do sprawdzenia", the by-merchant list); insight texts stay template-based (plan 06) | Plan 07 left 0.8 rows a month with the shipped rules (4.1 without merchant rules), all ambiguous texts or brands the user has to decide anyway, and one correction teaches tier A the merchant; a ≤ 3B model would cost ~2–3 GB RAM, a container and a queue for a handful of guesses |
| D35 | The deploy runs inside CI on a push to `main` (a `release` job calling `deploy.yml` after every CI job passes), not on `workflow_run`; values that are personal or name the tailnet (`AUTH_OWNER_EMAIL`, `DEPLOY_HOST`, `R2_ENDPOINT`) are environment secrets, and the repository variable `DEPLOY_ENABLED` turns deploys on | For a public repository: `workflow_run` with `branches: [main]` also fires for a fork's PR from a branch named `main` and would deploy that fork's code with the production secrets; a push needs write access. Variables are printed in the public job log, secrets are masked |
