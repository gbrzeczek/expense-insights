# 08 · Local LLM fallback

**Status:** Not started. Needs the RAM upgrade to 16 GB.

## Goal

Handle the long tail that rules and ML can't classify confidently, and optionally turn
computed insights into natural Polish sentences. Fully local and optional: the app must work
identically with the LLM container turned off.

## Hardware reality (i3-6100T: 2C/4T, AVX2 + FMA, DDR4 or DDR3L)

CPU inference is limited by memory bandwidth. Rough expectations, to be measured with
`llama-bench`:

| Model size (Q4_K_M) | RAM | Generation speed |
|---|---|---|
| 1.5–2B | ~1.3 GB | ~10–15 tok/s |
| 3–4B | ~2.5 GB | ~5–8 tok/s |
| 7–8B | ~4.5 GB | ~2–3 tok/s |

Classification output is a few tokens (constrained to the category enum), so the cost is
dominated by prompt processing. Keep prompts short. A few dozen leftover transactions per
month is easily feasible as an async queue.

## Design

- `llm` service in Compose: llama.cpp `llama-server` (OpenAI-compatible), `--threads 2`,
  `nice`, `mem_limit`. The model file is mounted from `models/` (git-ignored).
- `LlmClassifier : IClassifier` uses **JSON schema / GBNF-constrained output**:
  `{ "category": <enum>, "confidence": "low|medium|high" }`.
- Prompt = the category list with short descriptions + the 5 nearest labelled transactions
  (from plan 07, "RAG-lite") + the cleaned transaction. Never send account numbers.
- Queue: a `Channel` worker, one request at a time, retry with backoff, and a circuit breaker
  when the container is down.
- The LLM result is a **suggestion**: it's shown as the first chip in "Do sprawdzenia" and only
  auto-applied at `high` confidence if the user enables that.
- Optional insight phrasing: input = a typed `Insight` with its computed parameters; output = one
  Polish sentence. It's validated by checking that every number in the output appears in the
  input; otherwise the template text is used.

## Tasks

- [ ] Benchmark candidates on the server: Bielik small variants (a Polish-native model from
      SpeakLeash), Qwen-class 1.5–4B, Gemma-class small. Measure tok/s, RAM, and accuracy with
      the harness.
- [ ] Compose `llm` service with a profile (`docker compose --profile llm up`).
- [ ] `LlmClassifier` plus a queue worker plus a health-based circuit breaker.
- [ ] Settings toggle per user: "Użyj lokalnego modelu AI" ("use the local AI model").
- [ ] Optional cloud fallback (anonymized descriptions only, explicit opt-in, off by default).
      Decide after the benchmarks; it may not be needed.
- [ ] Record the results and the chosen model in this file and in the decision log.

## Acceptance criteria

- The LLM tier improves harness accuracy on the leftovers by a measurable margin. Otherwise
  it's not enabled by default.
- The web UI stays responsive (p95 API latency < 300 ms) while the LLM queue is busy.
- With the `llm` container stopped, nothing breaks and items stay in "Do sprawdzenia".
