# Test data

**Fake data only. Never put real bank exports here.**

- `generate_ing_csv.py`: generates a fake ING Bank Śląski export that reproduces the real
  format's quirks: pending holds, apostrophe-wrapped ids, BLIK payments through payment
  processors, foreign currency, and a consistent running balance. See the docstring.
- `fake_ing.csv`: 6 months, seed 42, in the real export format: Windows-1250, `;`-delimited,
  LF line endings, with the preamble (fake name and address). Read it with
  `iconv -f cp1250 -t utf-8 fake_ing.csv | less`.
- `fake_ing.labels.csv`: the correct merchant and category for each data row of `fake_ing.csv`
  (same order), for measuring classifier accuracy.

```bash
python3 generate_ing_csv.py --months 6 --seed 42 --out fake_ing.csv
python3 generate_ing_csv.py --encoding utf-8 --delimiter '\t' --no-preamble --out fake_ing_utf8.csv
```

Not yet verified: the preamble lines after `Wybrane rachunki:`, any footer, and whether data
rows always quote values (see `docs/plans/02-ing-import.md`).
