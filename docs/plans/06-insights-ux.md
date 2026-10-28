# 06 · Summaries & insights UX

**Status:** Not started. **Waiting for UX references from the owner.**

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
   The LLM (plan 08) may only rephrase them.

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
| Category spike/drop | Month vs. baseline, \|Δ\| ≥ 25% and ≥ 100 zł |
| New subscription | Same merchant key, similar amount (±10%), roughly monthly cadence, 2+ occurrences |
| Subscription price change | Detected subscription amount changed |
| Unusually large payment | > p95 of the user's transactions in that category |
| Frequent small spending | E.g. Żabka: count and sum this month vs. baseline |
| Pace | Mid-month projection vs. baseline (shown only after day 7) |
| Uncategorized reminder | "12 transakcji czeka na kategorię" (12 transactions waiting for a category) |

## Tasks

- [ ] Collect references → write a short visual direction (type scale, colour, chart style) here.
- [ ] Aggregation queries: monthly totals per category with spending/income/excluded semantics,
      refunds offset against their original category.
- [ ] `InsightEngine`: each insight is a class with `Evaluate(context) → Insight?` (type,
      severity, parameters). The text comes from i18n templates with ICU plurals (Polish
      plural forms!).
- [ ] Recurring payment detector, with tests on generator subscriptions.
- [ ] Screens above, mobile first, plus Playwright screenshots at 390 px in light and dark.
- [ ] Empty states: no data, one month only (no baseline yet), everything categorized.

## Open questions

- Weekly view, or monthly only? Default: monthly only.

## Decided

- The baseline is available from day one: a real first import covers 2+ years, so the "one
  month only" empty state is an edge case, not the default experience.
- P2P transfers count as spending by default and can be broken down; reimbursements offset
  their category (D11, details in plan 04).
