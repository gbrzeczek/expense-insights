# 04 · Rule-based classification & corrections

**Status:** Not started

## Goal

Classify the large majority of transactions with no ML, make correcting them on a phone
effortless, and turn every correction into a rule. Build the evaluation harness that later
tiers (plans 07 and 08) are measured with.

## Category taxonomy (seed, Polish)

| Top level | Examples | Counts as spending |
|---|---|---|
| Spożywcze | Biedronka, Lidl, Żabka | yes |
| Restauracje i jedzenie na wynos | McDonald's, Pyszne.pl | yes |
| Transport | Orlen, Bolt, Koleo, MPK | yes |
| Zdrowie | Apteka, ZnanyLekarz | yes |
| Drogeria i uroda | Rossmann | yes |
| Dom i ogród | Castorama, IKEA, Pepco | yes |
| Odzież i obuwie | Reserved, Bershka | yes |
| Elektronika | Media Expert, x-kom | yes |
| Sport, hobby i rozrywka | Decathlon, kino | yes |
| Subskrypcje | Netflix, Spotify | yes |
| Rachunki i opłaty | czynsz, prąd, internet, telefon | yes |
| Zakupy online (inne) | Allegro (unknown contents) | yes |
| Przelewy do osób | P2P out | configurable |
| Inne | | yes |
| *Wynagrodzenie / Przychody* | salary, P2P in | income |
| *Zwroty* | refunds | offsets the original category |
| *Przelew własny* | own accounts, savings | excluded |

Final names are subject to the UX review. The fake-data generator's category names get aligned
to this list.

## Tasks

### Merchant normalization
- [ ] `MerchantNormalizer`: lowercases and strips diacritics; removes legal prefixes
      (`JMP S.A.`, `SP. Z O.O.`, `S.A.`), store numbers (`Z5521`, `6537`, `0123`), card
      suffixes (`K.1`), trailing city and country tokens (a city list plus the truncated
      `PO`/`POL`/`P` forms), and `*` prefixes (`GOOGLE *YouTube`). Output: a merchant key.
- [ ] For `BlikOnline`, the key comes from `MerchantHint` (the domain) instead of the processor.
- [ ] Table-driven tests using every counterparty in the fake data.

### Rules engine
- [ ] `MerchantRule(userId?, matchType: Exact|Contains|Trigram, pattern, categoryId, priority)`.
      A null user means a global rule.
- [ ] Global seed rules for the top ~150 Polish merchants and chains (a hand-maintained JSON
      file in the repo).
- [ ] Rules for transfers: own name in counterparty → `Przelew własny`; positive card
      amount → `Zwroty`; `Kind = BlikP2P` with an incoming amount → income.
- [ ] Pipeline runs after import (background, via a channel) and on demand. It writes
      `Classification` and `ClassificationHistory`; never overwrites a `UserCorrection`.

### Correction UX (mobile first)
- [ ] Transaction list: grouped by day, infinite scroll, a filter chip row (Nieskategoryzowane
      / Do sprawdzenia / Wszystkie), and search.
- [ ] Tap → bottom sheet: merchant, amount, raw details (collapsible), and the **top 3 suggested
      categories as chips** plus "Więcej…".
- [ ] After a correction, a snackbar offers "Zastosuj do wszystkich: Biedronka (23)" to apply it
      to all transactions from the merchant. This creates a user rule and reclassifies.
- [ ] "Do sprawdzenia" ("needs review") queue: low-confidence items, reviewable one by one
      with large buttons (swipe gestures are optional).
- [ ] Category management: rename, hide, add subcategory.

### Evaluation harness
- [ ] `ExpenseInsights.Classification.Eval` console app: runs the pipeline on
      `fake_ing.csv` and compares with `fake_ing.labels.csv`. Reports accuracy, coverage
      (share not left as `Unclassified`), and a confusion matrix per tier.
- [ ] Can also run on a **local, real, git-ignored** labelled export (the owner's corrections
      exported to CSV), so we measure real accuracy without committing real data.

## Acceptance criteria

- Fake data: ≥ 90% coverage by rules, ≥ 97% precision on what the rules classify. The
  deliberately ambiguous merchants should land in "Do sprawdzenia".
- Correcting one transaction on a 390 px screen takes ≤ 2 taps when the right category is in
  the chips.
- "Apply to all" reclassifies matching past transactions and future imports.
