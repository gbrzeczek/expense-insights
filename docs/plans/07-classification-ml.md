# 07 · Similarity & embedding classification

**Status:** In review

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

- [x] `IClassifier` tier interface: `Classify(batch) → (category, confidence, explanation)`.
- [x] A: kNN TF-IDF classifier over the user's labelled transactions, with the explanation.
- [x] Category anchors: a short Polish description and 3–5 example merchants per category, in
      the embedded rules JSON (reviewed in diffs like D21). *Changed:* the examples are kinds of
      merchant (`apteka`, `stacja paliw`), not brands; brands made it worse (see *Results*).
- [x] C: ONNX embedding service (loaded lazily, batch only, unloaded after idle to keep RAM low);
      `pgvector` column ~~+ HNSW index~~; backfill job; anchors re-embedded when the JSON changes.
      *Changed:* no HNSW index (an exact per-user scan, see *As built*); the backfill runs inside
      classification, for the labelled merchants that have no embedding yet.
- [x] Confidence calibration and the combining rule above.
- [x] Harness report: rules only, + A, + C, + A + C on fake data ~~and on the owner's local
      labelled export~~. Coverage and precision at several thresholds, latency, peak RAM. The
      real export is the owner's to run locally (`--csv`, `--labels`); see *Results*.
- [x] **Leftover report:** after rules + A + C, how many transactions a month still land in
      review, and what they look like. ~~Plan 08 benchmarks its candidates on these.~~ They
      decided that there is no plan 08 (D34).
- [x] Write the results up in this file. This doubles as portfolio material.
- [x] Wire the tiers that measurably help into the pipeline after the rules. Both do (D33).

## Acceptance criteria

- On the owner's real data (local only), rules + A + C reach ≥ 95% coverage at ≥ 95% precision
  after one month of corrections.
- Classifying a 300-row import takes under 10 s on the i3; the embedding model's peak RAM stays
  within the api container's limit (raise it if needed and record why).
- The decision on which tiers stay is recorded in `docs/architecture.md`.

## As built

- **Tier interface.** `IClassifier.ClassifyAsync(batch)` returns a `TierResult` per transaction
  (category, source, calibrated confidence, reason code and value) or `null` for no opinion
  (`ExpenseInsights.Classification/Tiers`). `TierPipeline` runs after the rules, only for what
  they leave unclassified or below the review threshold: A first; if A is confident (≥ 0.7), done;
  otherwise C on what's still open; agreeing results take the higher confidence, disagreeing ones
  the more confident (a review guess below 0.7). A tier guess under 0.4 is no guess: the row stays
  unclassified, and a rule's own guess stays unless a tier does better. A confident rule always
  wins (D5).
- **A, memory.** `MemoryClassifier`: the user's labelled transactions (`UserCorrection` and
  `UserRule` sources) grouped by merchant key, char 3–4-gram TF-IDF (sublinear TF, smoothed IDF,
  unseen n-grams count in the norm), cosine, k = 5 above 0.3, same direction of money only. Each
  neighbour votes with its similarity; confidence = vote share × the nearest neighbour's
  similarity. `Kind` is the tie-breaker: a neighbour of the other kind (card/BLIK payment vs
  transfer) counts 0.6, so a card payment at "P.H.U. Anna Nowak" isn't confidently a transfer to
  the person Anna Nowak. Reason `similarTo` with the neighbour's key: "Podobne do: „…”". A
  correction queues a background reclassification, so it takes effect at once.
- **C, embeddings.** `E5Embedder`: `intfloat/multilingual-e5-small`, the int8 ONNX export from
  the model's own repository (`model_qint8_avx512_vnni.onnx`, 118 MB, MIT licence), pinned by
  revision and SHA-256 in `E5Model`. Tokenized by `UnigramTokenizer`, a ~150-line SentencePiece
  Unigram reader of the model's `tokenizer.json` (identical ids to the reference on 1 280 fake
  strings, so no tokenizer dependency); mean pooling, unit vectors; text `query: <merchant key>`.
  ONNX Runtime with 2 threads. Loaded on the first batch, unloaded 2 minutes after the last
  (`Classification:Embeddings:IdleTimeout`), followed by a full GC and, on Linux, `malloc_trim`. Each distinct merchant key is embedded once per batch.
- **Anchors.** `anchor` per spending category in `global-rules.json`: the Polish name plus 4–8
  kinds of merchant, each embedded on its own (~85 texts), kept in memory for the process
  (`AnchorEmbeddings`; a new JSON ships with a deploy, so it's embedded afresh). Not for
  `Przelewy do osób`, `Inne` or income. The anchors only apply to money going out.
- **Neighbours.** `MerchantEmbedding` (user, merchant key, model, `vector(384)`), one per labelled
  merchant key, with a query filter and an isolation test (hard rule 7) and erased with the
  account (D19). The vector is a shadow property, so Core doesn't depend on pgvector. The first
  search of a run embeds the labelled keys that have no embedding from the current model (and
  drops other models' rows); open transactions are embedded on the fly, not stored. Search:
  `ORDER BY vector <=> query LIMIT 10` over the user's labelled keys; no HNSW index, because
  a user has a few hundred labelled merchants (an exact scan is fast) and an HNSW scan filtered
  by user could return fewer than k of them. Confidence: the margin over the second-best category,
  linear, 0.7 at a margin of 0.03, capped at 0.95. Reason `similarTo` for a labelled neighbour,
  `categoryAnchor` ("Nazwa pasuje do: „apteka”") for an anchor.
- **Unknown merchants** (plan 04's open question): a merchant nothing knows still stays
  unclassified, unless an anchor gives a guess of at least 0.4, which goes to "Do sprawdzenia" as a
  suggestion. On a first import of `fake_ing.csv` that is `9179 KRAKOW ZAKOPIAN` (a wrong
  "Zakupy online" guess at 0.51, in review); after the first correction tier A takes over.
- **Sources.** `ClassificationSource.Similarity` and `Embedding`. Like the rule sources they are
  recomputed on every run; a `UserCorrection` never is. "Do sprawdzenia" now means any computed
  source below 0.7 (`Confidence.IsComputed`), in the list filter, the review queue and the
  by-merchant list.
- **Deployment.** The production image downloads the two model files in a build stage, checks
  their SHA-256 (a test keeps the Dockerfile and `E5Model` in step) and sets
  `Classification__Embeddings__ModelDirectory=/app/models` with downloads off. Elsewhere (dev, the
  Eval tool) the files are downloaded on first use into `~/.cache/expense-insights/models`. The
  api container's limit is now 1 GB (D33). The API tests turn tier C off
  (`Classification:Embeddings:Enabled=false`); its tests use a stand-in model, plus an explicit
  test with the real one.
- **Dependencies.** `Microsoft.ML.OnnxRuntime` (runs the model; Microsoft, MIT) and
  `Pgvector.EntityFrameworkCore` (the `vector` type and `<=>` in LINQ; MIT). No tokenizer package.

## Results

Measured with `dotnet run --project ExpenseInsights.Classification.Eval -c Release -- --leftover`
(Linux container, 2 CPUs; the Mac gives the same table within a row or two, since int8 kernels
differ by CPU). The first month's labels (50 rows) are the user's corrections; the other 285
rows of `fake_ing.csv` are evaluated. "No review" is what the user doesn't have to look at.

**Rules as shipped.** The fake data is mostly chains the rule list knows, so there is little left:

| | coverage | precision | no review | its precision | left a month |
|---|---|---|---|---|---|
| rules | 96.8% | 100% | 96.8% | 100% | 1.8 |
| rules + A | 99.6% | 98.9% | 98.6% | 100% | 0.8 |
| rules + C | 98.6% | 100% | 98.6% | 100% | 0.8 |
| rules + A + C | 99.6% | 98.9% | 98.6% | 100% | 0.8 |

**Merchants unknown to the rules** (title keywords only, as for a bank or city whose shops aren't
in the list):

| | coverage | precision | no review | its precision | left a month |
|---|---|---|---|---|---|
| rules | 17.5% | 100% | 17.5% | 100% | 47.7 |
| rules + A | 89.1% | 98.8% | 88.1% | 100% | 6.9 |
| rules + C | 93.0% | 98.5% | 93.0% | 98.5% | 4.1 |
| rules + A + C | 94.0% | 97.4% | 93.0% | 98.5% | 4.1 |

**Category anchors on local shops nobody labelled** (`test-data/fake_local_merchants.csv`, 56
invented names such as `APTEKA POD ORLEM`, `SALON FRYZJERSKI EWA`):

| | coverage | precision | no review | its precision |
|---|---|---|---|---|
| rules as shipped | 50.0% | 92.9% | 50.0% | 92.9% |
| rules as shipped + C | 87.5% | 93.9% | 83.9% | 95.7% |
| C alone | 82.1% | 95.7% | 69.6–71.4% | 97.5–100% |

What the numbers say:

- **A is the workhorse.** With one month of labels it takes merchants the rules don't know from
  17.5% to 88% without review, at 100% precision. It costs milliseconds.
- **C earns its place on descriptive names, not on brands.** e5-small doesn't know Polish chains:
  it compares spelling, so `lidl` sat next to `tidal` and `biedronka` next to `kwiaciarnia`. The
  first anchors (brand examples such as Carrefour, Hebe) made it worse; anchors of merchant kinds
  fixed that, and brands now get tiny margins and stay unclassified rather than wrong. For local
  shops it is right on every pick with a margin above 0.03. The one confident miss on fake data is
  `koleo` → `kino` (the rules know Koleo, so it only shows without them).
- **Calibration.** A: confidence 0.9+ was 100% right (201 rows). C: 0.9+ was 100% right on the
  local merchants (36 rows); on the fake data's brands the `koleo` rows bring it to 71%. Thresholds
  0.3–0.8 change little; 0.9 drops ~6 points of coverage for no precision gain, so the review
  threshold stays 0.7.
- **Found on the way:** the global rules misfile two local kinds: `gabinet` (any *gabinet* →
  Zdrowie, also a beauty salon) and `zoo` (matches `zoologiczny`). Left for a rules fix.
- **Latency and memory.** The tiers took 0.2–1.1 s for 285 rows including loading the model
  (~1 s) on 2 container CPUs; A alone ~15 ms. The process with the model loaded peaked at 326 MB
  RSS, 263 MB more than without it, and fell back to 143 MB on unloading (`malloc_trim` hands
  ONNX Runtime's freed buffers back to the OS). The production image (chiseled, 2 CPUs, 1 GB
  limit) went from 87 MiB idle to 357 MiB while classifying an import of `fake_ing.csv`, and to
  177 MiB after the idle unload. Hence 1 GB for the api container. On the i3-6100T the model should still classify a 300-row import well inside 10 s,
  but that, and the real-data accuracy, are left to measure on the server and the owner's export.

**Leftover report** (after rules + A + C): with the shipped rules, 0.8 rows a month: the
`P.H.U. ANNA NOWAK` card payments (a review guess of "Przelewy do osób" from the person of the same
name, 0.6) and one small online shop. Without merchant rules, 4.1 a month, all brands the model
can't place: Bolt, Reserved, IKEA, Bershka, Media Expert, Decathlon, plus low-margin local names
(`lodziarnia`, `krawiec`, `biletomat ztm`, `escape room`). They are left to the user: one
correction per merchant ("Zastosuj do wszystkich", or the by-merchant list) and tier A covers
the rest. So there is no local LLM tier (plan 08 dropped, D34).

**Acceptance on real data** (≥ 95% coverage at ≥ 95% precision after a month of corrections, and
under 10 s for 300 rows on the i3) is still to be checked by the owner: export the labels
(`GET /api/classification/labels`) and run the Eval tool with `--csv` and `--labels`, outside the
repo.
