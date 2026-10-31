# 08 · Local LLM fallback

**Status:** Dropped (D34, 2026-10-31). Kept for the record.

After plan 07, the rules, memory (tier A) and embeddings (tier C) leave 0.8 rows a month on the
fake data with the shipped rules (4.1 without merchant rules): ambiguous texts and brands the
user has to decide anyway, and one correction teaches tier A the merchant. A ≤ 3B model would
cost ~2–3 GB of RAM, a container and a queue for a handful of guesses, so what's left goes to
"Do sprawdzenia" and the by-merchant list instead. Insight texts stay template-based (plan 06).
Revisit only if real data leaves far more than plan 07 measured.

The original plan follows.

## Goal

Classify the long tail that rules, memory and embeddings (plan 07) can't classify confidently,
and turn computed insights into natural Polish sentences. Fully local, so no transaction text
leaves the server. Required, but not a single point of failure: with the `llm` container down,
the app works as before and leftovers wait in "Do sprawdzenia" until it is back.

## Hardware reality (i3-6100T: 2C/4T, AVX2 + FMA, DDR4 or DDR3L)

CPU inference is limited by memory bandwidth. Rough expectations, to be measured with
`llama-bench`:

| Model size (Q4_K_M) | RAM | Generation speed |
|---|---|---|
| 1.5–2B | ~1.3 GB | ~10–15 tok/s |
| 3–4B | ~2.5 GB | ~5–8 tok/s |
| 7–8B | ~4.5 GB | ~2–3 tok/s (needs 16 GB) |

Classification output is one category, so the cost is dominated by prompt processing (likely a
few seconds per transaction). Keep prompts short. A few dozen leftovers a month is easily
feasible as an async queue.

## Design

- `llm` service in the production Compose stack (not a profile): llama.cpp `llama-server`
  (OpenAI-compatible), `--threads 2`, `nice`, `mem_limit` sized to the chosen model. The model
  file is mounted from `models/` (git-ignored); `deploy/README.md` says how to download it.
- **The model is a chooser, not a writer** (the same idea as hosted choosers such as Jev).
  `LlmClassifier : IClassifier` restricts the output to the category ids with a GBNF grammar /
  JSON schema and reads the **token log-probabilities** of each category, giving a probability
  per category. Confidence = that probability, calibrated on the fake labels like plan 07's
  tiers. The generated text itself is never trusted.
- Prompt = the category list with the anchor descriptions from plan 07 + the 5 nearest labelled
  transactions (plan 07, "RAG-lite") + the cleaned transaction. Never send account numbers. Its
  advantage over the embeddings: knowledge of brands (`ROSSMANN` → drugstore) without labels.
- Queue: a `Channel` worker, one request at a time, retry with backoff, and a circuit breaker
  when the container is down (health check on `/health` of `llama-server`).
- The result is a **suggestion**: the first chip in "Do sprawdzenia", auto-applied only above a
  high calibrated confidence and only if the user enables that.
- **Insight phrasing:** input = a typed `Insight` with its computed parameters; output = one
  Polish sentence with placeholders (`{delta}`, `{category}`) that code fills in, the same
  mechanism as plan 09. A sentence containing a digit the model typed is rejected and the i18n
  template text is used instead (hard rule 6).

## Tasks

- [ ] Benchmark candidates on the server with the plan 04/07 harness on plan 07's leftovers:
      tok/s, seconds per transaction, peak RAM, accuracy at the calibrated thresholds.
  - Small LLMs (≤ 3B, Q4): Bielik small variants (Polish-native, from SpeakLeash), Qwen-class
    1.5–3B, Gemma-class small.
  - Zero-shot encoders as a lighter alternative, run in ONNX Runtime inside the API (no extra
    container): a multilingual mDeBERTa-v3 NLI model (one pass per category) and a multilingual
    GLiClass model (one pass for all categories). Check Polish support and how they cope with
    terse merchant strings.
- [ ] Choose the winner; record it, with the numbers, in this file and in the decision log.
- [ ] `llm` service in `deploy/docker-compose.prod.yml` (or the encoder inside the API), with a
      memory limit; model download steps in `deploy/README.md`.
- [ ] `LlmClassifier` with constrained output and log-probability scoring, a queue worker and a
      health-based circuit breaker.
- [ ] Insight phrasing with placeholders and the digit check; template text as the fallback.
- [ ] Settings toggle per user for auto-applying high-confidence suggestions.

## Acceptance criteria

- The tier improves harness accuracy on plan 07's leftovers by a measurable margin.
- The web UI stays responsive (p95 API latency < 300 ms) while the LLM queue is busy.
- With the `llm` container stopped, nothing breaks, items stay in "Do sprawdzenia" and the
  queue catches up when it is back.
- The whole stack, including the model, stays within the server's memory budget (AGENTS.md).
