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
| Przelewy do osób | P2P out, not yet broken down | yes (see below) |
| Inne | | yes |
| *Wynagrodzenie / Przychody* | salary, P2P in | income |
| *Odsetki* | savings interest (net of the interest tax row) | income |
| *Zwroty* | refunds | offsets the original category |
| *Przelew własny* | own accounts, savings | excluded |

### Transfers to and from people (decision D11)

People-to-people transfers count as **spending by default**, in `Przelewy do osób`, which acts
as a holding category. They can be broken down further:

- **Recategorize a single transfer.** E.g. 93 zł to a friend for dinner → `Restauracje`.
- **Rule per recipient.** The key is the counterparty account number (stable), with the name as
  a fallback. E.g. a landlord paid by transfer → `Rachunki i opłaty / Czynsz`, and every future
  transfer follows.
- **Split** one transfer across categories (model from this plan, UI in the backlog).
- **Incoming transfers from people** default to income, but can be marked as a **reimbursement**
  of a category. E.g. a friend pays back 22 zł for shopping → it offsets `Spożywcze` instead of
  inflating income. This matters for shared bills, otherwise both spending and income are
  overstated.
- The insights screen nudges, without nagging: "5 przelewów do osób (480 zł) bez kategorii"
  ("5 transfers to people, 480 zł, without a category").

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
- [ ] Rules for transfers: counterparty account is one of the user's own accounts (from the
      import preamble), or the pair is linked by the importer → `Przelew własny` (the own
      name in the counterparty is only a fallback); positive card
      amount → `Zwroty`; outgoing P2P → `Przelewy do osób`; incoming P2P → income.
- [ ] `MatchType.CounterpartyAccount` rules (person or recipient by account number).
- [ ] A reimbursement flag on incoming transactions (`OffsetsCategoryId`), respected by the
      aggregations in plan 06.
- [ ] Pipeline runs after import (background, via a channel) and on demand. It writes
      `Classification` and `ClassificationHistory`; never overwrites a `UserCorrection`.

### Correction UX (mobile first)
- [ ] Transaction list: grouped by day, infinite scroll, a filter chip row (Nieskategoryzowane
      / Do sprawdzenia / Wszystkie), and search.
- [ ] Tap → bottom sheet: merchant, amount, raw details (collapsible), and the **top 3 suggested
      categories as chips** plus "Więcej…".
- [ ] After a correction, a snackbar offers "Zastosuj do wszystkich: Biedronka (23)" to apply it
      to all transactions from the merchant. This creates a user rule and reclassifies.
- [ ] **First-import onboarding: review by merchant, not by transaction.** A real first
      import is ~2 600 rows over 2+ years. Group the unclassified rows by merchant key, sorted
      by count × total amount ("BIEDRONKA · 412 transakcji · 23 140 zł"). One tap labels the
      whole group and creates a rule. Show a progress bar ("78% wydatków skategoryzowanych";
      i.e. 78% of spending categorized), measured by amount rather than by count.
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
