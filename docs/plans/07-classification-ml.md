# 07 · Similarity & embedding classification

**Status:** Not started

## Goal

Classify what the rules miss, using the user's own corrections as memory and a small local
embedding model for merchants nobody has labelled yet. No model training (D27): both tiers
work from the first correction, and a correction takes effect at once. Measure everything with
the harness from plan 04.

## Why this is feasible for Polish data

The descriptions are short merchant strings (`ZABKA Z5521 K.1 KRAKOW POL`), not prose. Character
n-grams handle Polish inflection, truncation and missing diacritics well, and the vocabulary is
small and repetitive. The hard part is cold start (little labelled data per user), which the
rules from plan 04 cover, plus the category descriptions below.

This is the same shape as hosted "pick one of these options" services such as TypeSafe's Jev
(rules → the company's memory of past choices → a model that chooses from the given list),
done locally so no merchant string leaves the server.

## Tiers (after the rules from plan 04)

| # | Tier | Notes |
|---|---|---|
| A | **Memory: kNN over char n-gram TF-IDF** of the user's labelled transactions (pure .NET, or `pg_trgm`) | No training; every correction is another example. Explains itself: "podobne do: BIEDRONKA 1234 …" ("similar to: …") |
| C | **Embeddings: `multilingual-e5-small`** (ONNX Runtime, ~120 MB on disk) + `pgvector` | Two kinds of neighbour: the user's labelled transactions, and **category anchors** (each category's name plus a short Polish description and example merchants, embedded once). The anchors handle merchants nobody has labelled yet (an unknown florist → `Dom i ogród`) |
| ~~B~~ | ~~ML.NET `FeaturizeText` + `SdcaMaximumEntropy`, retrained per user~~ | **Dropped** (D27). Revisit only if the harness shows a gap A + C don't close |

Confidence: kNN vote share (A) and cosine similarity margin between the best and second-best
category (C), calibrated on the fake labels so "0.9" means roughly 90% correct. Below the
threshold → "Do sprawdzenia" (the review list).

Combining: run A first (cheap). If A is confident, done. Otherwise C; if A and C agree, take it
with the higher confidence; if they disagree, the higher calibrated confidence wins only above
the threshold, otherwise review.

Features beyond text, used as tie-breakers only: `Kind`, the sign, the amount bucket.

## Tasks

- [ ] `IClassifier` tier interface: `Classify(batch) → (category, confidence, explanation)`.
- [ ] A: kNN TF-IDF classifier over the user's labelled transactions, with the explanation.
- [ ] Category anchors: a short Polish description and 3–5 example merchants per category, in
      the embedded rules JSON (reviewed in diffs like D21).
- [ ] C: ONNX embedding service (loaded lazily, batch only, unloaded after idle to keep RAM low);
      `pgvector` column + HNSW index; backfill job; anchors re-embedded when the JSON changes.
- [ ] Confidence calibration and the combining rule above.
- [ ] Harness report: rules only, + A, + C, + A + C on fake data and on the owner's local
      labelled export. Coverage and precision at several thresholds, latency, peak RAM.
- [ ] **Leftover report:** after rules + A + C, how many transactions a month still land in
      review, and what they look like. This decides plan 08.
- [ ] Write the results up in this file. This doubles as portfolio material.
- [ ] Wire the tiers that measurably help into the pipeline after the rules.

## Acceptance criteria

- On the owner's real data (local only), rules + A + C reach ≥ 95% coverage at ≥ 95% precision
  after one month of corrections.
- Classifying a 300-row import takes under 10 s on the i3; the embedding model's peak RAM stays
  within the api container's limit (raise it if needed and record why).
- The decision on which tiers stay is recorded in `docs/architecture.md`.
