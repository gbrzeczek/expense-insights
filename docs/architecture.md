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

`ParseResult` is either an `ImportedTransaction` or a row-level `ParseError` (line number and
reason). One bad row never fails the whole import.

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
      └─ Category (per-user overrides of the global taxonomy)

Merchant (global, normalized name, aliases, default category)
```

- **Category taxonomy:** fixed, two levels, about 12 top-level categories, seeded globally.
  Users can rename or hide categories and add subcategories, but not change top-level semantics
  (this keeps ML and insights consistent). Special categories that are excluded from spending:
  `Przelew własny`, `Zwroty`, and income categories.
- **Classification source:** `UserCorrection` > `UserRule` > `GlobalRule` > `Similarity` > `Ml`
  > `Llm` > `Unclassified`. A higher source never gets overwritten by a lower one.

## Classification pipeline

1. **Normalize.** Strip store numbers, cities, country codes and the `JMP S.A.` style legal
   prefixes, giving a merchant key (`JMP S.A. BIEDRONKA 6537 KRAKOW PO` → `biedronka`). For BLIK
   through processors (PayU, Cashbill, PayPro, Przelewy24), use the shop domain from the title.
2. **Rules.** The user's rules, then global merchant rules. Exact key first, then trigram
   similarity (`pg_trgm`) above a threshold.
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
