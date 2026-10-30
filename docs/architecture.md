# Architecture

## Overview

```
 Phone / desktop browser (Angular PWA, pl-PL)
              │  HTTPS (Cloudflare Tunnel / Tailscale)
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

One deployable API (a modular monolith) plus Postgres, and optionally llama.cpp. No message
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
  (`Similarity`, `Ml` and `Llm` come with plans 07 and 08). A `UserCorrection` is never
  recomputed; the rule-based sources are re-derived when the rules change.

## Classification pipeline

1. **Normalize.** Strip store numbers, cities, country codes and the `JMP S.A.` style legal
   prefixes, giving a merchant key (`JMP S.A. BIEDRONKA 6537 KRAKOW PO` → `biedronka`). For BLIK
   through processors (PayU, Cashbill, PayPro, Przelewy24), use the shop domain from the title.
2. **Rules.** Own accounts and refunds, the user's rules, the account holder's own name (for
   review), transfers to and from people, then global merchant and title-keyword rules; trigram
   similarity only when nothing matched (see plan 04, *As built*).
3. **Similarity.** kNN over the user's own labelled transactions (char n-grams first;
   embeddings in plan 06).
4. **LLM fallback** (optional, async). Only for low-confidence leftovers. The output is
   constrained to the category enum, and the prompt includes the nearest labelled examples.
5. **User correction.** Saves a correction, offers "apply to all from this merchant", and
   creates or updates a `MerchantRule`.

Accuracy is measured offline against `test-data/fake_ing.labels.csv` and reported per tier.

## Deployment

Docker Compose: `api`, `db`, and optionally `llm` (llama.cpp, 2 threads, low CPU priority). The
Angular build is served by the API as static files, so there is one origin and no CORS. Remote
access goes through Cloudflare Tunnel or Tailscale, with no open ports. Nightly `pg_dump`,
encrypted, plus an off-site copy.

## Decision log

| # | Decision | Why |
|---|---|---|
| D1 | .NET + Angular | Portfolio value on the Polish job market; strong typing end to end |
| D2 | Modular monolith, single API | Tiny load, 8–16 GB home server |
| D3 | PostgreSQL over SQLite | `pg_trgm` and `pgvector`; better portfolio signal |
| D4 | ING only, behind `IStatementParser` | Ship one bank well; keep the seam for more |
| D5 | Rules → similarity → LLM, LLM last | Old CPU; merchant strings are repetitive; small models are unreliable |
| D6 | LLM never computes numbers | Small models hallucinate figures |
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
