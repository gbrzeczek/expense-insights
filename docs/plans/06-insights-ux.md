# 06 · Summaries & insights UX

**Status:** In review. Visual direction: [`docs/design-system.md`](../design-system.md) (Warm Paper Finance); the screens reuse the components in `src/frontend/src/app/shared/ui/`. See *As built* for the decisions taken while implementing.

## Goal

Show what matters about your spending at a glance on a phone, with depth on demand. Insightful,
not overwhelming.

## Principles

1. **One headline per screen.** E.g. "W październiku wydałeś 4 210 zł — 8% mniej niż zwykle"
   ("In October you spent 4,210 zł, 8% less than usual").
2. **Three or four insight cards, ranked.** Only show insights that clear a significance
   threshold. When nothing notable happened, say so.
3. **Compare against a personal baseline** (median of the previous 3–6 months), not against
   zero or arbitrary budgets.
4. **Drill-down:** summary → category → merchant → transactions. Every number is tappable
   and leads to the transactions behind it.
5. **Everything deterministic.** Insights are computed in code from typed rules (decision D6).
   No ML model decides what is unusual (D26); the statistics below are plain, tested code, and
   the texts are templates (no local LLM, D34). Plan 09 adds opt-in cloud insights in a
   separate section, with every number still computed here.

## Statistics (how "unusual" is decided)

One user has a few hundred monthly data points (about 24 months × ~20 categories), too few to
train a model without learning noise, and every number has to lead to the transactions behind
it. So the thresholds come from robust statistics on the user's own history:

- **Baseline:** the median of the previous 6 months for the category (3 when there are fewer),
  not the mean, so one big month doesn't move it.
- **Spread:** the MAD (median absolute deviation) of the same months, scaled by 1.4826 so it is
  comparable to a standard deviation. A month is unusual when `|month − baseline| ≥ k × spread`
  (start with k = 2.5; tune on fake data) **and** the difference is ≥ 100 zł. Volatile categories
  (fuel, restaurants) need a bigger jump than steady ones (rent, subscriptions). If the spread is
  near zero (a fixed bill), fall back to `|Δ| ≥ 10%`. *As built: k = 3, see below.*
- **Seasonality:** with 13+ months of history, also compare with the same month last year. A
  spike is reported only if it is unusual against both, so every December isn't news. With 2+
  years, use the median of the same month in previous years.
- **Pace:** project the month from the user's typical share of a month's spending by day *d*
  (median over past months), not linearly, because rent and bills land early. Shown only after
  day 7 and only if the projection is unusual by the rule above.
- **Large payment:** above the p95 of the category's transactions over the last 12 months and
  above the category's median × 3, so a category with five transactions doesn't flag its
  largest every month.
- **Ranking:** score = robust z-score (`|Δ| / spread`, capped) × log of the amount at stake.
  Show the top 3–4; an insight shown for the same month and category isn't repeated unless it
  grew by another threshold step.

All of it lives in one `InsightStatistics` class with unit tests on hand-built series
(steady, volatile, seasonal, with and without outliers), so the thresholds are reviewable.

## Screens

- **Przegląd** (overview): month switcher, headline, spend vs. baseline (a sparkline or small
  bar), insight cards, top categories (horizontal bars, not a pie chart), upcoming
  subscriptions.
- **Kategoria** (category detail): trend over 6–12 months, top merchants, transactions.
- **Subskrypcje** (subscriptions): detected recurring payments, monthly and yearly cost, and
  "new" and "price changed" badges.

## Insight catalogue (first version)

| Insight | Rule |
|---|---|
| Category spike/drop | Unusual vs. baseline by the median/MAD rule, ≥ 100 zł, and not explained by season (see *Statistics*) |
| New subscription | Same merchant key, similar amount (±10%), a regular cadence (median gap 7, 30 or 365 days ± 20%), 2+ occurrences |
| Subscription price change | A detected subscription's latest amount is outside ±2% of its previous amount |
| Unusually large payment | > p95 of the category's last 12 months and > 3 × its median |
| Frequent small spending | E.g. Żabka: this month's count and sum unusual vs. the merchant's baseline (median/MAD) |
| Pace | Projection from the typical day-of-month spending profile, unusual vs. baseline (only after day 7) |
| Uncategorized reminder | "12 transakcji czeka na kategorię" (12 transactions waiting for a category) |

## Tasks

- [x] Collect references → write a short visual direction (type scale, colour, chart style) here.
      The direction is the design system (Warm Paper Finance, D13); charts: see *As built*.
- [x] Aggregation queries: monthly totals per category with spending/income/excluded semantics,
      refunds offset against their original category.
- [x] `InsightStatistics`: baseline (median), spread (scaled MAD), seasonal comparison,
      day-of-month spending profile, ranking score. Unit tests on hand-built series.
- [x] `InsightEngine`: each insight is a class with `Evaluate(context) → Insight?` (type,
      severity, parameters). The text comes from i18n templates with ICU plurals (Polish
      plural forms!).
- [x] Recurring payment detector, with tests on generator subscriptions.
- [x] Screens above, mobile first, plus Playwright screenshots at 390 px in light and dark.
- [x] Empty states: no data, one month only (no baseline yet), everything categorized.

## Open questions

- Tune k and the 100 zł floor on the owner's real data (the tuning in *As built* is on fake data):
  count insights per month over the real history and aim for 0–2 in a normal month.

## Decided

- No ML for insights (D26): too little data per user, and every number must be explainable.
  Robust statistics instead (see *Statistics*).
- The baseline is available from day one: a real first import covers 2+ years, so the "one
  month only" empty state is an edge case, not the default experience.
- P2P transfers count as spending by default and can be broken down; reimbursements offset
  their category (D11, details in plan 04).
- Monthly only, no weekly view (the open question's default).

## As built

**Code.** `ExpenseInsights.Core/Insights/`: `SpendingLedger` (what counts as spending),
`SpendingHistory` (by month, with the imported period), `InsightStatistics`, the rules in
`InsightRules.cs`, `InsightEngine`, `RecurringPayments`. `Infrastructure/Insights/SpendingData`
loads the current user's rows (query filter, hard rule 7). API in
`Api/Insights/InsightEndpoints.cs`. UI in `src/frontend/src/app/features/insights/`.

**Aggregation.** A few thousand rows a user, so the ledger is built in memory on each request (no
SQL aggregation, no stored results). Subcategories roll up to their top-level category. Own
transfers (`Kind` or category) and expired holds don't count; pending holds do. Unclassified money
out is spending without a category ("Bez kategorii"). A refund offsets the category its merchant's
purchases are usually in (or `OffsetsCategoryId` when set); a refund from a merchant with no
purchases offsets `Inne`. Income with `OffsetsCategoryId` offsets that category (D11). Splits
replace the transaction's category. Summaries are in the user's main currency (the one most
transactions are in); rows in another currency are left out. Months are by `TransactionDate`.

**History coverage.** A month counts towards a baseline only when the imports cover all of it: the
earliest `ImportBatch.PeriodStart` (a period starting on the 2nd makes the next month the first
full one) to the latest `PeriodEnd` or transaction date. A month with no spending in a category is a
zero. The baseline needs 3 complete months before the month; with fewer, the overview says so and
shows no insights. The last month is "in progress" until the data reaches its last day: then the
headline compares by day (spent so far vs. the usual share by this day × the baseline) and no drop is
reported.

**Thresholds (tuned on fake data).** `threshold = max(k × spread, 10% × baseline, 100 zł)`: the
10% term is the near-zero-spread fallback, applied smoothly. k = **3** instead of 2.5: on the
25-month fake history (`test-data/fake_ing_history.csv`, random with no real events) k = 2.5 showed
1.7 insights a month, k = 3 shows 1.3, with quiet months. A test keeps the average ≤ 2. Seasonal:
with a complete same month a year (or years) back, a spike must be unusual against both the baseline
and the seasonal reference, with the baseline's spread.

**Large payment.** The plan's p95 fired almost every month for groceries: a category with ~25
payments a month has one above its p95 by definition. The bar is the 12-month maximum (p100) and
3 × the median payment, with ≥ 5 payments of history.

**Frequent small spending.** A merchant whose median payment is ≤ 50 zł with ≥ 3 payments in a
usual month; this month's count (≥ 3 more) and sum (the usual rule) both unusually high.

**Recurring payments.** The merchant's payments split into price segments (each within ±10% of the
one before). A subscription is the current segment (≥ 2 payments, cadence from the median gap
7/30/365 ± 20%), possibly ending in one changed price (up to ±50%, on schedule, after ≥ 2 at the
old price); every earlier segment must have held at least twice. So a varying bill (Tauron) or a
shop with random baskets doesn't become a subscription by chance. Active until 1.5 cadences pass
without a payment. "New" only when it starts at least a cadence after the imports do (otherwise
it may just predate the first import).

**Ranking.** Score = min(robust z, 10) × ln(amount at stake). One insight per subject (a category's
spike and its largest payment are one story; a merchant's subscription or small payments another),
top 3 by score, then the uncategorized reminder (so 3–4 cards). There is no separate severity: the
score is it. Insights are recomputed for the month being looked at and never stored, so "not
repeated unless it grew" reduces to one card per subject per month; a notifications feed would need
a stored "shown" record (not built).

**Rules.** `IInsightRule.Evaluate(context)` returns a list (a rule such as the category rule finds
one per category). The UI words each type through `insights.insight.*` i18n templates; Polish plural
forms use CLDR plural keys (`.one/.few/.many/.other`, like the rest of the app) rather than ICU
message syntax. Every number in a card comes from the API, including percentages (`change`) and
yearly costs.

**API** (all scoped to the current user, each with a route isolation test):
`GET /api/insights/overview?month=yyyy-MM` (headline numbers, usual, projection, the last six
months, categories, ranked insights, upcoming recurring payments), `GET /api/insights/categories/{id}`
(month vs. usual, 12-month trend, top merchants), `GET /api/insights/transactions?month&category&merchant`
(the transactions behind any number, with what each counts for; `category=none` is unclassified),
`GET /api/insights/subscriptions`.

**Screens.** `/overview` (Przegląd, month switcher via `?month=`), `/overview/categories/:id`,
`/overview/transactions`, `/overview/subscriptions`. Charts are small CSS bar charts
(`shared/ui/month-bars.ts`, horizontal `category-breakdown`), not ECharts (D31). Tests: `e2e/insights.spec.ts`
walks summary → category → merchant → transactions at 390 px in light and dark and attaches
full-page screenshots to the report.

