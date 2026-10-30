# 04 · Rule-based classification & corrections

**Status:** In review

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
| *Wynagrodzenie* | salary | income |
| *Przelewy od osób* | P2P in | income |
| *Inne przychody* | other income, e.g. from the user's own business account | income |
| *Odsetki* | savings interest (net of the interest tax row) | income |
| *Zwroty* | refunds | offsets the original category |
| *Przelew własny* | between the user's accounts in the export | excluded |

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

### Transfers to and from the user's own name (decision D20)

A transfer between two accounts in the export is an own transfer: it is excluded, and the importer
links both legs. An account in the user's name that isn't in the export is different: a business
account, for example, which the ING export doesn't include. Money from it is **income**
(`Inne przychody`), not an own transfer. So the user's name as the counterparty is only a guess, and
both directions go to "Do sprawdzenia": incoming → `Inne przychody`, outgoing → `Przelew własny`.
Confirming it and "apply to all" creates a rule for that account number, so it's asked once.

The income side is split into `Wynagrodzenie`, `Przelewy od osób` and `Inne przychody`, which is
also how the fake data was already labelled. Names are i18n keys (`categories.*` in `pl.json`).

## Tasks

### Merchant normalization
- [x] `MerchantNormalizer`: lowercases and strips diacritics; removes legal prefixes
      (`JMP S.A.`, `SP. Z O.O.`, `S.A.`), store numbers (`Z5521`, `6537`, `0123`), card
      suffixes (`K.1`), trailing city and country tokens (a city list plus the truncated
      `PO`/`POL`/`P` forms), and `*` prefixes (`GOOGLE *YouTube`). Output: a merchant key.
- [x] For `BlikOnline`, the key comes from `MerchantHint` (the domain) instead of the processor.
- [x] Table-driven tests using every counterparty in the fake data.

### Rules engine
- [x] `MerchantRule(userId?, matchType: Exact|Contains|Trigram, pattern, categoryId, priority)`.
      A null user means a global rule.
- [x] Global seed rules for the top ~150 Polish merchants and chains (a hand-maintained JSON
      file in the repo).
- [x] Rules for transfers: counterparty account is one of the user's own accounts (from the
      import preamble), or the pair is linked by the importer → `Przelew własny`; the own
      name with an account not in the export is a guess for review (D20); positive card
      amount → `Zwroty`; outgoing P2P → `Przelewy do osób`; incoming P2P → income.
- [x] `MatchType.CounterpartyAccount` rules (person or recipient by account number).
- [x] A reimbursement flag on incoming transactions (`OffsetsCategoryId`), respected by the
      aggregations in plan 06.
- [x] Pipeline runs after import (background, via a channel) and on demand. It writes
      `Classification` and `ClassificationHistory`; never overwrites a `UserCorrection`.

### Correction UX (mobile first)
- [x] Transaction list: grouped by day, infinite scroll, a filter chip row (Nieskategoryzowane
      / Do sprawdzenia / Wszystkie), and search.
- [x] Tap → bottom sheet: merchant, amount, raw details (collapsible), and the **top 3 suggested
      categories as chips** plus "Więcej…".
- [x] After a correction, a snackbar offers "Zastosuj do wszystkich: Biedronka (23)" to apply it
      to all transactions from the merchant. This creates a user rule and reclassifies.
- [x] **First-import onboarding: review by merchant, not by transaction.** A real first
      import is ~2 600 rows over 2+ years. Group the unclassified rows by merchant key, sorted
      by count × total amount ("BIEDRONKA · 412 transakcji · 23 140 zł"). One tap labels the
      whole group and creates a rule. Show a progress bar ("78% wydatków skategoryzowanych";
      i.e. 78% of spending categorized), measured by amount rather than by count.
- [x] "Do sprawdzenia" ("needs review") queue: low-confidence items, reviewable one by one
      with large buttons (swipe gestures are optional).
- [x] Category management: rename, hide, add subcategory.

### Evaluation harness
- [x] `ExpenseInsights.Classification.Eval` console app: runs the pipeline on
      `fake_ing.csv` and compares with `fake_ing.labels.csv`. Reports accuracy, coverage
      (share not left as `Unclassified`), and a confusion matrix per tier.
- [x] Can also run on a **local, real, git-ignored** labelled export (the owner's corrections
      exported to CSV), so we measure real accuracy without committing real data.

## Acceptance criteria

- Fake data: ≥ 90% coverage by rules, ≥ 97% precision on what the rules classify. The
  deliberately ambiguous merchants should land in "Do sprawdzenia".
- Correcting one transaction on a 390 px screen takes ≤ 2 taps when the right category is in
  the chips.
- "Apply to all" reclassifies matching past transactions and future imports.

## As built

- **Merchant key.** `MerchantNormalizer` (Classification) folds case and diacritics, cuts the
  address (`ul. …`, postcodes), legal forms, card suffixes, store numbers and terminal codes (words
  with digits, except a leading brand like `4F`), store words (`stacja`, `nr`, `gal`) and place
  names. A domain becomes its name (`BOLT.EU/R/4598` → `bolt`), and so does the processor hint
  (`pyszne.pl` → `pyszne`), so card and BLIK payments to one shop share a key. Stored on the
  transaction (`MerchantKey`, indexed) for grouping and rules.
- **Rules engine.** `RuleEngine.Evaluate` is pure and returns every match best first, one per
  category: the first is the classification, the rest feed the suggestion chips. Tiers in order:
  own accounts in the export (1.0) and positive card/BLIK payments (refunds); the user's rules
  (priority, then confidence); the account holder's own name (0.5, D20); BLIK P2P and phone
  transfers (0.9); global rules. Global rules: exact 0.95, word-start "contains" 0.9 (the longest
  pattern wins, so `uber eats` beats `uber`), title keywords 0.85, and only when nothing matched,
  trigram similarity × 0.85 (`mediaexpet` → Elektronika, for review). Money in from a known
  merchant is a refund. Rules have a direction: income keywords only match money in.
- **Global rules ship as JSON** (`ExpenseInsights.Classification/global-rules.json`, embedded;
  ~400 patterns for Polish chains, plus title keywords like `czynsz` and `wynagrodzenie`), not as
  database rows (D21). User rules are `MerchantRule` rows; a rule by account stores the account's
  SHA-256 hash, never the number, and shows the person's name as its label.
- **Review.** A guess below 0.7 confidence is "Do sprawdzenia". The deliberately ambiguous fake
  merchants (`9179 KRAKOW ZAKOPIAN`, `P.H.U. ANNA NOWAK`) match nothing, so they stay
  **unclassified** rather than getting a made-up guess: they show under "Nieskategoryzowane" and
  in the by-merchant onboarding list, not in the review queue. See the open question.
- **Pipeline.** `TransactionClassifier` (Infrastructure) sets the key and category of the user's
  transactions in batches of 500 and writes `ClassificationHistory` on every change. Only a
  `UserCorrection` is never recomputed; rule-based categories follow the rules (deleting a rule
  reclassifies what it had classified). The import endpoint queues the user on
  `ClassificationRequests` (a channel); `ClassificationWorker` (API) classifies in the background,
  and at startup also picks up transactions never classified. The transaction's `xmin` is a
  concurrency token, so a background write never overwrites a correction made meanwhile.
- **Corrections.** "Apply to all" generalizes by merchant key, or by account for P2P transfers and
  the account-holder guesses; it upserts the rule and reclassifies the matches, leaving other
  one-off corrections alone. `TransactionSplit` exists in the model only (UI in the backlog).
- **API.** `GET /api/transactions` (filter, search incl. amounts, category, offset paging),
  `GET /api/transactions/{id}` (masked account, raw row, 3 suggestions, the apply-to-all offer),
  `PUT …/category`, `POST …/apply-to-all`, `PUT …/offsets`; `GET /api/merchants/to-review`
  (groups by count × amount, progress by amount per currency), `POST /api/merchants/category`;
  `GET/POST /api/categories`, `PUT /api/categories/{id}`; `GET /api/rules`,
  `DELETE /api/rules/{id}`; `POST /api/classification/run`; `GET /api/classification/labels`.
- **Frontend.** `/transactions` (day groups, chips, search, infinite scroll, bottom sheet, snackbar
  with "Zastosuj do wszystkich"), `/transactions/review`, `/transactions/merchants`,
  `/categories` (rename, hide, add subcategory, the user's rules). The e2e test checks the
  two-tap correction at 390 px.
- **Evaluation.** `dotnet run --project ExpenseInsights.Classification.Eval` reports coverage,
  precision, per-tier counts and the confusion. On `fake_ing.csv`: **97.0% coverage, 100%
  precision** (the 10 unclassified rows are the two ambiguous merchants and one small online
  shop). `FakeDataAccuracyTests` enforce the targets on all three fake exports. For real data:
  export `GET /api/classification/labels` (your own corrections and rules, by bank id, as
  top-level keys) and run `-- --csv <export> --labels <labels>`, outside the repo.

## Open questions

- Should a merchant the rules know nothing about land in "Do sprawdzenia" as `Inne` (as this
  plan's acceptance criteria assumed), or stay unclassified (as built)? Unclassified keeps the
  review queue for real guesses and makes coverage honest; the by-merchant list is where the
  unknown ones get labelled.
