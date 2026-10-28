# 02 · ING import

**Status:** Not started

## Goal

Reliably turn an ING Bank Śląski CSV export into normalized `ImportedTransaction`s, with
re-import being safe: overlapping exports and pending → booked transitions never create
duplicates. ING is the only bank, but the code sits behind `IStatementParser`
(see `docs/architecture.md`).

## Known format (from a real export, Oct 2026)

Confirmed from the raw bytes of `Lista_transakcji_nr_<doc>_<ddmmyy>.csv`:

- **Encoding:** Windows-1250, no BOM. **Delimiter:** `;`. **Line endings:** LF.
- Values are double-quoted (`"…"`); empty fields are bare (`;;`).
- **A preamble comes before the header.** Lines seen so far:

  ```
  "Lista transakcji";;;;;"ING Bank Śląski S.A. ul. Sokolska 34, 40-086 Katowice www.ing.pl";;;…
  "Dokument nr 0123456789_281026";
  "Wygenerowany dnia: 2026-10-28, 12:36";;;…
  <blank>
  "Dane Użytkownika:";
  "<SURNAME NAME , street, postcode city>";      ← PII: never store or log
  <blank>
  "Wybrane rachunki:";
  "KONTO Mobi …                                  ← rest unknown
  ```

  The parser locates the header by its **signature** (`"Data transakcji";"Data księgowania";…`),
  not by a fixed line number. From the preamble it may keep only the document number and the
  generation timestamp, for `ImportBatch`. **The user's name and address must be discarded:**
  not stored, not logged, not included in error messages.

Still unknown: the preamble lines after `Wybrane rachunki:`, whether there's a footer
(e.g. totals) after the data, and whether data rows always quote every non-empty value.

Data rows have 17 columns, newest first:

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
| Transaction id structure | `YYYY` + day-of-year (booking) + 2-digit channel + 9 digits | Use only as an opaque dedup key |

The parser should still tolerate UTF-8 and CRLF (the file may be re-saved by Excel or a phone),
but cp1250 + `;` + LF is the main path. Both variants get fixtures.

## Tasks

- [ ] `IStatementParser`, `ParseResult`, `ImportedTransaction` in `Core`.
- [ ] `IngCsvParser` in `Import.Ing`: encoding detection (cp1250 default, UTF-8 with or without
      BOM), preamble skipping up to the header signature, a footer tolerated, position-based
      mapping, all quirks above. Streaming, no full-file load.
- [ ] A test asserting that preamble PII (the name and address line) appears nowhere in the
      persisted entities, `RawRow` or logs.
- [ ] **Single account:** if the `Konto` column has more than one distinct value, reject the
      file with a clear message ("Obsługiwany jest plik z jednym rachunkiem", meaning only
      single-account files are supported). Multi-account support is backlog.
- [ ] `Kind` detection from the `Szczegóły` code plus title patterns. Unknown codes →
      `Other`, logged once per import.
- [ ] `MerchantHint` extraction for BLIK through a processor: last token of the title if it's
      a domain or URL. The processor list lives in config, not code.
- [ ] **Balance check:** with newest-first rows, `Saldo[i] = Saldo[i+1] + Amount[i]`.
      Mismatches are reported as import warnings, never silently fixed.
- [ ] **Dedup:**
  - Booked: unique `(AccountId, ExternalId)`.
  - Whole file: SHA-256 of the file. Re-uploading the same file is a no-op with a friendly
        message.
- [ ] **Pending → booked reconciliation:** when a booked card row arrives, look for a pending
      transaction on the same account with the same amount, a `PurchaseDate`/`TransactionDate`
      within 0–5 days, and similar counterparty (trigram). If there's a match, promote it in
      place: keep the id, category and user edits. Pending rows that never get booked
      (after 14 days, missing from newer exports) are flagged as `Expired`, not deleted.
- [ ] `ImportBatch` entity: file hash, row counts (new / updated / duplicate / error), warnings.
- [ ] API: `POST /api/imports` (multipart, 5 MB limit) → batch summary; `GET /api/imports`.
- [ ] Frontend: Import screen. Mobile file picker, upload progress, result summary card
      ("Dodano 42, zaktualizowano 3, pominięto 120 duplikatów"), row errors expandable.
- [ ] Extend `test-data/generate_ing_csv.py` with an "overlapping exports" mode: two files with
      overlapping date ranges, where pending rows in the first become booked in the second.

## Acceptance criteria

- `fake_ing.csv` imports with 0 errors, and the balance check passes.
- Importing the same file twice → 0 new rows.
- The overlapping-exports fixture → no duplicates; every pending row in file A is either
  promoted by file B or still pending.
- Parser unit tests cover every quirk in the table.
- 10 000 rows import in under 5 s on the i3 (benchmark test, skipped in CI).

## Open questions

- The rest of the preamble and a possible footer: needs a redacted look at a real file.
- Credit card exports: same format? Out of scope until a sample exists.

## Decided

- One account per file for now (decision D10). The model already has `Account`, so
  multi-account support later is a parser change only.
