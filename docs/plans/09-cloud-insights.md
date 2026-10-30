# 09 · Cloud insights

**Status:** Not started. Depends on plan 06 (aggregates, `InsightStatistics`, the insight UI).

## Goal

Find more interesting patterns than the fixed rules of plan 06 can ("restaurants up, groceries
down: eating out instead of cooking?") by letting a cloud LLM explore the user's **aggregated**
spending. The model acts as the analyst: it decides what to look at and writes the text. Code
does every calculation (hard rule 6, as revised in D6/D29). Opt-in per user, off by default,
on a small prepaid budget.

## What is sent (and what never is)

Sent, per opted-in user:

- Monthly totals per category (spending, income, excluded), the plan 06 baseline (median) and
  spread (scaled MAD), transaction counts, for up to 24 months.
- The category list with its descriptions.

Never sent: merchant names, transaction titles, counterparties, account numbers, exact dates
(months only), the user's name or email, other users' data. A user's data is sent only if that
user opted in themselves; the owner can't opt in for invited people.

## Design

- **Query tools.** The model gets a fixed set of functions, answered by our code from the
  aggregates: `total(category, month)`, `compare(category, month, baseline | same month last
  year)`, `split(category, month, by = weekday | amount band)`, `trend(category, months)`,
  `top_changes(month, n)`. Every result has an id (`r1`, `r2`, …) and pre-formatted values
  (`pl-PL`: `1 234,56 zł`, `8%`, plural-ready counts such as `12 transakcji`).
- **Free text, numbers by placeholder.** The model answers in a JSON schema: up to 4 insights,
  each with a title, a sentence or two, the result ids it is based on, and the category to link
  to. Numbers appear only as placeholders (`{r3.delta_pct}`), which code replaces with the
  formatted values. Validation drops an insight if the text contains a digit the model typed,
  references an unknown result, or names a category that isn't on the list. Qualitative claims
  are allowed; they are labelled as the model's interpretation in the UI.
- **Where it shows.** A separate "Spostrzeżenia AI" ("AI observations") section under the plan
  06 insight cards, never mixed with them, each card linking to the transactions behind its
  results. The deterministic insights stay the base and work without this plan.
- **When it runs.** On demand ("Pokaż spostrzeżenia AI") and at most once a month
  automatically after an import; results are cached per user and month.
- **Provider-agnostic client.** One OpenAI-compatible client (chat completions + tool calling).
  Base URL, model and API key come from configuration (`deploy/.env`, never the repo, hard
  rule 8), so switching provider is a config change. No reasoning ("thinking") models, or with
  the thinking budget capped: thinking tokens are billed as output.
- **Budget, twice capped.** A prepaid provider balance is the hard stop. In the app: at most N
  runs per user per month (default 2), a token limit per run, a global monthly token cap, and a
  log entry when a cap is hit. Token usage per run is stored so the cost is visible.

## Providers (prices as of October 2026, from third-party price trackers; verify first)

Assumed usage for ~4 users: 2 runs per user a month, ~20k input + 3k output tokens per run
(the context is re-sent with each tool call) → ~160k input + 24k output tokens a month.

| Provider and model | Per 1M tokens (in / out) | ~Per month | $5 lasts | Billing | Data |
|---|---|---|---|---|---|
| DeepSeek V4.1 Flash (direct) | $0.30 / $1.20 peak, half off-peak | ~$0.08 | ~5 years | Prepaid balance (to confirm) | Processed in China |
| DeepSeek V4 Pro (direct) | $1.32 / $3.96 peak | ~$0.31 | ~16 months | Same | Same |
| Mistral Small 4 (direct) | $0.15 / $0.60 | ~$0.04 | Years | To confirm | EU (GDPR); check training terms |
| OpenRouter (any model above) | Model price + ~5.5% top-up fee | Model + fee | Possibly limited by a 365-day credit expiry | Prepaid, $5 minimum | Per-provider data settings |
| Gemini Flash (paid) | $0.75 / $3.75 promo to 31.12.2026, then $1.50 / $7.50 | ~$0.21 → ~$0.42 | – | Billed after use | Not used for training |
| Gemini Flash (free tier) | $0 | $0 | – | None, rate-limited | Prompts may be used by Google |

Cache hits on repeated context are much cheaper on DeepSeek (~50×), so real costs are likely
lower.

**Recommendation:** DeepSeek V4.1 Flash, topped up with $5, if category totals going to China
are acceptable. Otherwise Mistral Small 4, through OpenRouter if Mistral's own billing isn't
prepaid (top up $5 a year in case credits expire). Not Gemini: the free tier may use the prompts
and the paid tier has no prepaid cap.

## Tasks

- [ ] Choose the provider (open question below); check its current prices, billing and data
      terms on the official pages and update the table.
- [ ] Aggregate snapshot builder (reuses plan 06's queries and `InsightStatistics`).
- [ ] Query tools with result ids and pre-formatted values; unit tests per tool.
- [ ] OpenAI-compatible client with tool calling, configuration, timeouts and retries.
- [ ] Response schema, placeholder rendering and validation (digit check, unknown ids,
      unknown categories), with tests including adversarial model output.
- [ ] Budget caps and per-run token accounting.
- [ ] Opt-in setting per user with a plain explanation of what is sent; opting out deletes the
      cached results.
- [ ] "Spostrzeżenia AI" section, mobile first, linking to the transactions behind each result.
- [ ] `deploy/README.md`: creating the API key, topping up, the env vars.

## Acceptance criteria

- No insight is shown whose numbers didn't come from a tool result (tests with a fake provider
  returning typed digits, unknown ids and unknown categories).
- What is sent matches *What is sent*: a test inspects the outgoing request for merchant names,
  titles, dates finer than a month and other users' data.
- A month of normal use for a few users costs cents, and a cap stops runs with a log entry.
- With the provider down, out of credit or opted out, the plan 06 insights work unchanged.

## Open questions

- Provider: DeepSeek V4.1 Flash (cheapest that is good, data in China) or Mistral Small 4 (EU)?
  Default: DeepSeek V4.1 Flash.
- Should invited users get this at all, or only the owner? Default: everyone, opt-in.
