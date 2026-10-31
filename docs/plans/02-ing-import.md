# 02 · ING import

**Status:** Done

## Goal

Reliably turn an ING Bank Śląski CSV export into normalized `ImportedTransaction`s, with
re-import being safe: overlapping exports and pending → booked transitions never create
duplicates. ING is the only bank, but the code sits behind `IStatementParser`
(see `docs/architecture.md`).

## Known format (from a real export, Oct 2026)

Confirmed from the raw bytes of `Lista_transakcji_nr_<doc>_<ddmmyy>.csv`:

- **Encoding:** Windows-1250, no BOM. **Delimiter:** `;`. **Line endings:** LF.
- **Quoting is mixed.** Text fields are double-quoted *with padding spaces inside the quotes*
  (`" JMP S.A. BIEDRONKA 6537  KRAKOW  PO "`, `" Płatność kartą …"`). Dates, amounts and
  apostrophe-wrapped values (`'5624…'`, `'2026…'`) are bare. Empty fields are bare (`;;`).
  Quoting of the bank, details, account and currency columns is unconfirmed; the parser must
  accept either.
- **Field counts:** header and data lines have **21 fields**, i.e. 17 named columns plus 4
  trailing empty ones.
- **Overall layout** (line numbers from a real 2+ year export; values below are fake):

  ```
   1  "Lista transakcji";;;;;"ING Bank Śląski S.A. ul. Sokolska 34, 40-086 Katowice www.ing.pl";;;…
   2  "Dokument nr 0123456789_281026";
   3  "Wygenerowany dnia: 2026-10-28, 12:36";;;…
   4
   5  "Dane Użytkownika:";
   6  "<SURNAME NAMES , street, postcode city>";         ← PII: discard
   7
   8  "Wybrane rachunki:";
   9  "KONTO Mobi pow.26 (PLN)";;"12 10…";                ← account name (currency);;number
  10  "Otwarte Konto Oszczędnościowe (PLN)";;"74 1…";     ← one line per account
  11
  12  "Zastosowane kryteria wyboru";;;;;"Podsumowanie";;
  13
  14  "Zakres dat:";"2024-07-01 - 2026-10-28";"Typy transakcji:";"wszystkie"…
  15
  16  ;;;;;"Suma uznań (412):";398120,40;PLN;;…           ← credits: count and sum
  17
  18  ;;;;;"Suma obciążeń (2187):";402555,17;PLN;;…       ← debits: count and sum
  19
  20  "Data transakcji";"Data księgowania";…              ← header
  21… data rows, newest first (credits + debits = number of data rows)
      <blank>
      <blank>
      "Dokument ma charakter informacyjny, nie stanowi dowodu księgowego";   ← footer
  ```

  The parser locates the header by its **signature**, not by line number, and stops at the
  first blank line after the data.

  **What the preamble gives us:**
  - **Accounts:** a name → account number map. The data rows' `Konto` column carries only the
    name (without ` (PLN)`), so this map is how rows link to real account numbers. It also lets
    us recognise **own transfers**: when the counterparty account is one of the user's own.
  - **Date range:** stored on `ImportBatch`. It shows which period an import covers; that is
    needed for gap detection between imports and for expiring pending rows reliably.
  - **Suma uznań / obciążeń:** counts and sums give an integrity check. After parsing,
    compare them and warn on a mismatch. The counts matched the row count in the real file,
    so pending rows appear to be included in them; whether the *sums* include them is to
    verify, so report a mismatch as a warning only until it's confirmed.
  - **Discard:** the user's name and address. Not stored, not logged, not in error messages.
    Keep only the document number and generation timestamp.

Data rows have 17 named columns, newest first:

```
0 Data transakcji          6 Szczegóły                  12 Kwota płatności w walucie
1 Data księgowania         7 Nr transakcji              13 Waluta
2 Dane kontrahenta         8 Kwota transakcji (w. rach.) 14 Konto
3 Tytuł                    9 Waluta                     15 Saldo po transakcji
4 Nr rachunku             10 Kwota blokady/zwolnienie   16 Waluta
5 Nazwa banku             11 Waluta
```

Quirks to handle (each one gets a test):

| Quirk | Example | Handling |
|---|---|---|
| Duplicate header names (`Waluta` ×4) | | Map by **position** after validating the header signature |
| Apostrophe-wrapped values | `'202630164005767692'`, `'1915031/19730 '` | Strip `'` and trim |
| Leading/trailing/multiple spaces | ` Bershka.com               Arteixo ` | Trim; collapse spaces in the cleaned name; keep raw |
| Decimal comma, no thousands separator | `-298,80`, `22055,82` | Parse with invariant rules for `,` → `decimal` |
| **Pending card payment (blokada)** | No booking date, no `Nr transakcji`, empty col 6/8/9, amount in **col 10**, title has a double space | `Status = Pending`, `ExternalId = null` |
| Purchase date in title | `Płatność kartą 25.10.2026 Nr karty …` | Parse into `PurchaseDate` |
| BLIK through a payment processor | `Cashbill eComm …` + title ending `doci.pl` | `Kind = BlikOnline`, `MerchantHint = doci.pl` |
| Incoming BLIK P2P | `Szczegóły = P.BLIK`, a person's name, positive amount | `Kind = BlikP2P` |
| Phone transfer | Title `Telephone transfer +48… To X From Y` (English!) | `Kind = PhoneTransfer` |
| Truncated country/city | `KRAKOW  PO`, `warszawa  P` | Handled by merchant normalization (plan 04) |
| Foreign currency | cols 12–13 filled | `OriginalAmount` / `OriginalCurrency` |
| Booking date before transaction date | Savings interest: transaction `2024-07-01`, booked `2024-06-30` | Allowed; no validation of `BookingDate >= TransactionDate` |
| Own transfer on both accounts | −2000 on the current account, +2000 on savings | Both rows kept; linked as a pair, both `Przelew własny` |
| Transaction id structure | `YYYY` + day-of-year (booking) + 2-digit channel + 9 digits | Use only as an opaque dedup key |
| More `Szczegóły` codes | `ST.ZLEC` (standing order), `EXPRESS` (express transfer), `PZ` (transfer order, *zlecenie przelewu*) | `Kind = Transfer` (see *Decided*) |
| **Booked row without a number** | Booking date, amount in col 8, a balance, but empty `Nr transakcji`; otherwise an ordinary-looking row (in the real file: 6 rows, 3 credits and 3 debits of equal sums) | Kept as booked; `ExternalId` = `ing-row:` + a hash of its content (see *Decided*) |
| **Rows out of balance order** | A row listed a few places from where its `Saldo` belongs (pairs of equal and opposite balance differences) | The balance check follows the chain instead of the file order (see *Decided*) |

The parser should still tolerate UTF-8 and CRLF (the file may be re-saved by Excel or a phone),
but cp1250 + `;` + LF is the main path. Both variants get fixtures.

## Tasks

- [x] `IStatementParser`, `ParseResult`, `ImportedTransaction` in `Core`.
- [x] `IngCsvParser` in `Import.Ing`: encoding detection (cp1250 default, UTF-8 with or without
      BOM), preamble skipping up to the header signature, a footer tolerated, position-based
      mapping, all quirks above. Streaming, no full-file load.
- [x] A test asserting that preamble PII (the name and address line) appears nowhere in the
      persisted entities, `RawRow` or logs.
- [x] **Accounts from the preamble:** parse the `Wybrane rachunki` lines, then upsert an
      `Account` per entry (name, masked number, currency). A row whose `Konto` doesn't match
      any listed account is a row error.
- [x] **Integrity check:** compare parsed credit/debit counts and sums with the `Suma uznań` /
      `Suma obciążeń` lines; report the result on `ImportBatch`.
- [x] Store the `Zakres dat` range on `ImportBatch`. Warn when a new import leaves a gap after the
      previous one ("Brak danych z okresu 03.09–10.09"; i.e. no data for that period).
- [x] Own-transfer pairing: a debit on account A whose counterparty account is account B, plus
      the matching credit on B (same amount, same date ± 1 day) → link the two and mark both as
      own transfers.
- [x] `Kind` detection from the `Szczegóły` code plus title patterns. Unknown codes →
      `Other`, logged once per import.
- [x] `MerchantHint` extraction for BLIK through a processor: last token of the title if it's
      a domain or URL. The processor list lives in config, not code.
- [x] **Balance check, per account** (`Saldo` is the account's own running balance): with
      newest-first rows, `Saldo[i] = Saldo[i+1] + Amount[i]`.
      Mismatches are reported as import warnings, never silently fixed.
- [x] **Dedup:**
  - Booked: unique `(AccountId, ExternalId)`.
  - Whole file: SHA-256 of the file. Re-uploading the same file is a no-op with a friendly
        message.
- [x] **Pending → booked reconciliation:** when a booked card row arrives, look for a pending
      transaction on the same account with the same amount, a `PurchaseDate`/`TransactionDate`
      within 0–5 days, and similar counterparty (trigram). If there's a match, promote it in
      place: keep the id, category and user edits. Pending rows that never get booked
      (after 14 days, missing from newer exports) are flagged as `Expired`, not deleted.
- [x] **Fixes from the first real import (Oct 2026):** `ST.ZLEC`, `EXPRESS` and `PZ` were unknown
      codes; six booked rows without a number were row errors, which made the `Suma` counts and
      sums miss 3 credits and 3 debits (equal sums) and broke the balance chain around them; and
      rows listed out of balance order gave pairs of balance warnings. The generator reproduces all
      three (rent as a standing order, express transfers, a quarterly transfer order, one cluster of
      corrections without a number, some same-day rows swapped).
- [x] `ImportBatch` entity: file hash, row counts (new / updated / duplicate / error), warnings.
- [x] API: `POST /api/imports` (multipart, 5 MB limit) → batch summary; `GET /api/imports`.
- [x] Frontend: Import screen. Mobile file picker, upload progress, result summary card
      ("Dodano 42, zaktualizowano 3, pominięto 120 duplikatów"), row errors expandable.
- [x] Extend `test-data/generate_ing_csv.py` with an "overlapping exports" mode: two files with
      overlapping date ranges, where pending rows in the first become booked in the second.

## Acceptance criteria

All met; the speed targets were measured on an Apple-silicon Mac (Postgres in Docker), not yet on
the i3: 2 600 rows in 0.6 s and 10 000 rows in 2.0 s (`ImportBenchmarkTests`, explicit).

- `fake_ing.csv` imports with 0 errors, and both the per-account balance check and the
  `Suma` totals check pass.
- 2 600 rows (the size of a real 28-month export) import in under 2 s on the i3.
- Importing the same file twice → 0 new rows.
- The overlapping-exports fixture → no duplicates; every pending row in file A is either
  promoted by file B or still pending.
- Parser unit tests cover every quirk in the table.
- 10 000 rows import in under 5 s on the i3 (benchmark test, skipped in CI).

## Open questions

- Exact quoting of the `Nazwa banku` / `Szczegóły` / `Konto` / currency columns; whether the
  `Suma` sums include pending rows. These are verified by running the parser on a real file
  locally (never committed).
- `Szczegóły` codes and titles for savings interest and tax: the generator guesses
  `ODSETKI` / `PODATEK`.
- The booked rows without a number look like ordinary transactions in the real export, so why
  they have none is unknown (the generator shapes them as corrections that cancel out). The
  importer only needs them to stay identical between exports.
- Credit card exports: same format? Out of scope until a sample exists.

## Decided

- **Users before auth.** `User`, `IUserOwned`, `ICurrentUser` and the global query filter arrive here
  because transactions need an owner; the initial migration seeds one owner
  (`00000000-…-0001`) and every request runs as that user until plan 03 adds sign-in.
- **Statement metadata is a parse result.** `ParseAsync` yields a `ParsedStatement` (accounts,
  period, declared totals, document number) before the rows, plus `ParseWarning`s; file-level
  problems throw `StatementFormatException` with a code.
- **Own account numbers** are stored as a mask (`•••• 3863`) plus a SHA-256 hash of the digits; the
  hash identifies the account on re-import and recognises own transfers. Counterparty account
  numbers stay on the transaction (needed for rules per recipient in plan 04).
- **Errors and warnings are codes with parameters** (`invalidAmount`, `periodGap`, …), worded in
  the UI through i18n; they never contain the account holder's personal data.
- **Pending matching** uses an in-memory trigram similarity that mirrors `pg_trgm` (threshold 0.3,
  tested against Postgres), since the candidates are a handful of holds per account.
- **Expiry:** a stored hold is expired when a newer export covers its date, doesn't contain it,
  and ends more than 14 days after it. A pending row in a new file that matches an already booked
  row (same account, amount, purchase date, similar counterparty) counts as a duplicate, so
  importing an older export after a newer one creates no holds.
- **Kinds:** `TR.KART` → Card, `TR.BLIK` → BlikOnline, `P.BLIK` → BlikP2P, `PRZELEW` → Transfer;
  the title wins for card payments, BLIK payments and phone transfers (pending rows have no code).
  `OwnTransfer` is set on import, when the counterparty account is one of the user's accounts.
  `ODSETKI`/`PODATEK` map to `Other`; the `Przelew własny` category comes with plan 04. `ST.ZLEC`,
  `EXPRESS` and `PZ` are transfers to an account (`Transfer`): a standing order to a person is
  classified like any transfer (the user's rule for the recipient), never as a card payment.
- **Booked rows without a number** are kept, not row errors: they are real movements in the
  balance chain and the `Suma` totals. Their `ExternalId` is `ing-row:` + the first 16 bytes of a
  SHA-256 of the dates, counterparty, title, counterparty account, code, amount, currency, account
  and balance after (cleaned), plus `#2`, `#3` … for identical rows in one file. That is stable
  between overlapping exports (the line number isn't part of it), so dedup works unchanged.
  `missingTransactionId` is no longer raised; the code stays for older batches.
- **Balance check follows the chain.** Walking oldest first, the next row is the nearest one
  (within 20 rows of that account) whose balance before it equals the running balance; only when
  none continues the chain is the next row in file order a mismatch. A row listed out of order is
  then no warning, a missing row is one, and nothing is ever reordered or fixed in the data.
- **The `Suma` totals include every data row**, pending ones too: the real file's counts and sums
  matched the rows exactly once the rows without a number were kept.
- **Upload** goes through `HttpClient` with the XHR backend (`withXhr()`), because the fetch
  backend can't report upload progress.

- Multiple accounts per file, all of the same bank (revised D10). The real export contained
  the current and savings accounts together, and having both legs of own transfers makes
  excluding them reliable.
