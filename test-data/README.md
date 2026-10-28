# Test data

**Fake data only. Never put real bank exports here.**

- `generate_ing_csv.py`: generates a fake ING Bank Śląski export that reproduces the real
  format's quirks: pending holds, apostrophe-wrapped ids, BLIK payments through payment
  processors, foreign currency, and a consistent running balance. See the docstring.
- `fake_ing.csv`: 6 months, seed 42, two accounts (current + savings), in the real export format.
  That means Windows-1250, `;`, LF line endings, the full preamble (fake name, address and
  account numbers, plus date range and totals), mixed quoting, 21 fields per line, and the
  footer. Its line structure and field counts match a real export. Read it with
  `iconv -f cp1250 -t utf-8 fake_ing.csv | less`.
- `fake_ing.labels.csv`: the correct merchant and category for each data row of `fake_ing.csv`
  (same order), for measuring classifier accuracy.

- `fake_ing_overlap_a.csv`, `fake_ing_overlap_b.csv` (+ labels): two exports of one fake history.
  B starts 14 days before A ends, and the six pending holds at the end of A are booked in B with
  their transaction ids. Used by the dedup and pending → booked import tests.

```bash
python3 generate_ing_csv.py --months 6 --seed 42 --out fake_ing.csv
python3 generate_ing_csv.py --months 2 --seed 10 --overlap --out fake_ing_overlap.csv
python3 generate_ing_csv.py --encoding utf-8 --delimiter '\t' --no-preamble --out fake_ing_utf8.csv
```

Still guessed: quoting of a few columns, and the codes and titles for savings interest and tax
(see `docs/plans/02-ing-import.md`).
