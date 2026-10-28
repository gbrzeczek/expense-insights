# 07 · Similarity & ML classification

**Status:** Not started

## Goal

Classify what the rules miss by learning from the user's own corrections. Compare approaches
with the evaluation harness from plan 04, and keep only what measurably helps.

## Why this is feasible for Polish data

The descriptions are short merchant strings (`ZABKA Z5521 K.1 KRAKOW POL`), not prose. Character
n-grams handle Polish inflection, truncation and missing diacritics well, and the vocabulary is
small and repetitive. Language-specific NLP is not needed. The hard part is cold start (little
labelled data per user), which the rules from plan 04 cover.

## Approaches (all measured with the plan 04 harness)

| # | Approach | Notes |
|---|---|---|
| A | **kNN over char n-gram TF-IDF** (pure .NET or `pg_trgm`) | The baseline. No training step; works from the first correction |
| B | **ML.NET** `FeaturizeText` (char n-grams) + `SdcaMaximumEntropy` | Retrained in the background after N new corrections, one model per user (with a global fallback model trained on global rules and fake data) |
| C | **Embeddings**: `multilingual-e5-small` (ONNX Runtime, ~120 MB) + `pgvector` kNN | Better at "semantic" matches (e.g. an unknown florist → Dom i ogród). ~1 GB RAM at load; run in batches |

Features beyond text: `Kind`, the amount bucket, sign, and day of week.

Output: a category plus calibrated confidence. Below a threshold → "Do sprawdzenia" (and the
LLM queue in plan 08).

## Tasks

- [ ] `IClassifier` tier interface: `Classify(batch) → (category, confidence, explanation)`.
- [ ] A: kNN TF-IDF classifier with an explanation ("podobne do: BIEDRONKA 1234 …" — "similar to: BIEDRONKA 1234 …").
- [ ] B: ML.NET training job (`BackgroundService`), model versioning per user, stored in the DB
      or on disk.
- [ ] C: ONNX embedding service; `pgvector` column + HNSW index; batch backfill job.
- [ ] Harness report comparing A/B/C on fake data and on the owner's local labelled export
      (accuracy at different confidence thresholds, latency, RAM).
- [ ] Write the results up in this file. This doubles as portfolio material.
- [ ] Wire the winner(s) into the pipeline after the rules.

## Acceptance criteria

- On the owner's real data (local only), rules + the ML tier reach ≥ 95% coverage at ≥ 95%
  precision after one month of corrections.
- Classifying a 300-row import takes under 10 s on the i3.
- The decision on which approaches stay is recorded in `docs/architecture.md`.
