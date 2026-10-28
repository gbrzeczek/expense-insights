# Test data

**Fake data only. Never put real bank exports here.**

- `generate_ing_csv.py`: generates a fake ING Bank Śląski export that reproduces the real
  format's quirks: pending holds, apostrophe-wrapped ids, BLIK payments through payment
  processors, foreign currency, and a consistent running balance. See the docstring.
- `fake_ing.csv`: 6 months, seed 42, tab-delimited, UTF-8.
- `fake_ing.labels.csv`: the correct merchant and category for each data row of `fake_ing.csv`
  (same order), for measuring classifier accuracy.

```bash
python3 generate_ing_csv.py --months 6 --seed 42 --out fake_ing.csv
python3 generate_ing_csv.py --delimiter ';' --encoding cp1250 --out fake_ing_cp1250.csv
```

Not yet verified against a raw export file: the delimiter, the encoding, and any preamble lines
(see `docs/plans/02-ing-import.md`).
