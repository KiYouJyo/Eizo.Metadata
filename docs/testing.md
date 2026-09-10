# Recognition testing strategy

Recognition quality depends more on regression coverage than on individual parser
cleverness. Tests are therefore treated as a first-class dataset.

## Test layers

### 1. Unit rules

Small tests for one syntax at a time:

- `S01E03`, `1x03`, `EP03`;
- Japanese `第3話` / `第03話`;
- bare episode numbers in well-bounded contexts;
- ranges such as `01-02`;
- year, resolution and codec collision cases;
- bracketed release tags.

Every positive rule should have collision tests against numbers that are not episodes,
especially years, resolutions, bit depth, codec versions and release-group names.

### 2. Golden corpus

Versioned, sanitized JSONL corpora store input paths and expected structured outputs.
They are readable without network access and run in normal CI.

Current Stage 6 datasets:

- `stage6-golden.jsonl`: 2,000 positive cases; current structured-output coverage 2,000/2,000;
- `stage6-negative.jsonl`: 400 non-episode/domain cases; current structural false-positive
  baseline 0/400.

Positive extraction coverage and structural false-positive rate are deliberately tracked
separately so increasing recall cannot silently hide a precision regression.

### 3. Regression cases

Every user-reported false positive or false negative becomes a minimal sanitized
regression case before the fix is merged.

### 4. Robustness

Property/fuzz-style tests cover unusual Unicode, repeated separators, very long names,
malformed brackets, combining marks, full-width forms, emoji and mixed-script paths.
Stage 6 adds 1,000 deterministic seeded fuzz paths plus adversarial long-input cases.
Recognition should not throw on these valid Unicode inputs, and repeat recognition must
remain deterministic.

### 5. Performance

Recognition is expected to be cheap enough for library scans. Stage 6 includes a
dependency-free benchmark harness that measures 1K, 10K and 100K representative paths,
recording elapsed time, throughput, allocation and a checksum.

The first GitHub `ubuntu-latest` / .NET 10 baseline is documented in
[recognition-performance.md](recognition-performance.md). CI uploads the raw Markdown and
JSON benchmark results for each run. Performance gates should be based on repeated
measured baselines rather than guessed microsecond targets.

## Privacy

Private paths, account names, server names and WebDAV URLs must stay out of the public
corpus. Real examples should be minimized and sanitized before commit.
