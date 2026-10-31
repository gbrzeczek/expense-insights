# 06 · Summaries & insights UX

**Status:** Not started. UX references received: follow [`docs/design-system.md`](../design-system.md) (Warm Paper Finance); reuse the components in `src/frontend/src/app/shared/ui/`.

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
   No ML model decides what is unusual (D26); the statistics below are plain, tested code. The
   local LLM (plan 08) may only rephrase them, and plan 09 adds opt-in cloud insights in a
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
  near zero (a fixed bill), fall back to `|Δ| ≥ 10%`.
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

- [ ] Collect references → write a short visual direction (type scale, colour, chart style) here.
- [ ] Aggregation queries: monthly totals per category with spending/income/excluded semantics,
      refunds offset against their original category.
- [ ] `InsightStatistics`: baseline (median), spread (scaled MAD), seasonal comparison,
      day-of-month spending profile, ranking score. Unit tests on hand-built series.
- [ ] `InsightEngine`: each insight is a class with `Evaluate(context) → Insight?` (type,
      severity, parameters). The text comes from i18n templates with ICU plurals (Polish
      plural forms!).
- [ ] Recurring payment detector, with tests on generator subscriptions.
- [ ] Screens above, mobile first, plus Playwright screenshots at 390 px in light and dark.
- [ ] Empty states: no data, one month only (no baseline yet), everything categorized.

## Open questions

- Weekly view, or monthly only? Default: monthly only.
- The MAD multiplier k (start at 2.5) and the 100 zł floor: tune on the owner's data so a normal
  month shows 0–2 insights.

## Decided

- No ML for insights (D26): too little data per user, and every number must be explainable.
  Robust statistics instead (see *Statistics*).
- The baseline is available from day one: a real first import covers 2+ years, so the "one
  month only" empty state is an edge case, not the default experience.
- P2P transfers count as spending by default and can be broken down; reimbursements offset
  their category (D11, details in plan 04).
