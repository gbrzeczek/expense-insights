# 02 · ING import

**Status:** Not started

## Goal

Reliably turn an ING Bank Śląski CSV export into normalized `ImportedTransaction`s, with
re-import being safe: overlapping exports and pending → booked transitions never create
duplicates. ING is the only bank, but the code sits behind `IStatementParser`
(see `docs/architecture.md`).

## Known format (from a real export, Oct 2026)

17 columns, newest first:

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

**To verify against a raw file** (the sample was pasted, so the original bytes are unknown):
the delimiter (`;` vs tab), the encoding (Windows-1250 vs UTF-8, BOM?), any metadata lines
before the header, and the line endings. The parser must sniff all of these, and the fake data
generator gets matching flags.

## Tasks

- [ ] `IStatementParser`, `ParseResult`, `ImportedTransaction` in `Core`.
- [ ] `IngCsvParser` in `Import.Ing`: encoding/delimiter detection, preamble skipping, header
      signature check, position-based mapping, all quirks above. Streaming, no full-file load.
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

- Does ING offer exports for multiple accounts in one file? (The `Konto` column suggests yes.)
  If so, auto-create an `Account` per distinct value.
- Credit card exports: same format? Out of scope until a sample exists.
