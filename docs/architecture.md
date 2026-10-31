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
 │    ├─ Classification pipeline (rules → similarity → LLM queue)               │
 │    ├─ Insights (deterministic aggregations)                                  │
 │    └─ BackgroundService workers (classification, LLM, retraining)            │
 │                         │                              │                     │
 │                         ▼                              ▼                     │
 │                 PostgreSQL (+pg_trgm, pgvector)   llama.cpp server (opt.)    │
 └──────────────────────────────────────────────────────────────────────────────┘
```

One deployable API (a modular monolith) plus Postgres and llama.cpp (plan 08). No message
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
- **Classification source:** `UserCorrection` > `UserRule` > `GlobalRule` > `Unclassified` so far
  (`Similarity` and `Embedding` come with plan 07, `Llm` with plan 08). A `UserCorrection` is never
  recomputed; the rule-based sources are re-derived when the rules change.

## Classification pipeline

1. **Normalize.** Strip store numbers, cities, country codes and the `JMP S.A.` style legal
   prefixes, giving a merchant key (`JMP S.A. BIEDRONKA 6537 KRAKOW PO` → `biedronka`). For BLIK
   through processors (PayU, Cashbill, PayPro, Przelewy24), use the shop domain from the title.
2. **Rules.** Own accounts and refunds, the user's rules, the account holder's own name (for
   review), transfers to and from people, then global merchant and title-keyword rules; trigram
   similarity only when nothing matched (see plan 04, *As built*).
3. **Memory.** kNN over the user's own labelled transactions (char n-gram TF-IDF). No training;
   every correction is another example (plan 07).
4. **Embeddings.** `multilingual-e5-small` (ONNX) + `pgvector`: nearest labelled transactions
   and category anchors (descriptions + example merchants), for merchants nobody has labelled
   (plan 07).
5. **Local LLM** (required, async). Low-confidence leftovers only. Output constrained to the
   category ids; confidence from the token log-probabilities; nearest labelled examples and the
   category anchors in the prompt. The result is a suggestion (plan 08).
6. **User correction.** Saves a correction, offers "apply to all from this merchant", and
   creates or updates a `MerchantRule`.

Accuracy is measured offline against `test-data/fake_ing.labels.csv` and reported per tier.

## Deployment

Docker Compose (`deploy/docker-compose.prod.yml`): `caddy`, `api`, `db`, and optionally `llm`
(llama.cpp, 2 threads, low CPU priority). The Angular build is served by the API as static files,
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
| D5 | Rules → similarity → LLM, LLM last | Old CPU; merchant strings are repetitive; small models are unreliable |
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
| D27 | Classification after rules: kNN memory over corrections + local embeddings with category anchors; no trained ML.NET model; the local LLM (plan 08) handles what is left | Both tiers work from the first correction with no training step; anchors cover cold start |
| D28 | The local LLM (plan 08) is required: a ≤ 3B quantized model on the current 8 GB, used as a chooser (constrained output, log-probability confidence) and for phrasing; the app works unchanged when its container is down | No transaction text leaves the server; brand knowledge covers what embeddings miss; 16 GB only if a bigger model clearly wins |
| D29 | Cloud insights (plan 09): opt-in per user, category-level monthly aggregates only, the model explores through fixed query tools and writes with placeholders that code fills; OpenAI-compatible client on a prepaid provider plus app-side caps | More interesting insights than fixed rules, without trusting model arithmetic or sending merchants; a few users cost cents a month |
| D30 | Deploys from GitHub Actions after CI on `main`: images built on GitHub and pushed to GHCR by commit; the job joins the tailnet as `tag:ci` and deploys over SSH with a key restricted to one forced command; settings live in the GitHub environment `production` and each deploy writes the server's `.env` | Same pattern as sheetsolver on the same server; builds on fast runners, not the i3; one place to edit settings; no open port and no shell for the deploy key |
