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

A versioned, sanitized JSONL corpus will store input paths and expected structured
outputs. It should be readable without network access and runnable in normal CI.

Initial target after Stage 4: at least 500 curated cases.
Stage 6 target: at least 2,000 cases with anime and Japanese drama heavily represented.

### 3. Regression cases

Every user-reported false positive or false negative becomes a minimal sanitized
regression case before the fix is merged.

### 4. Robustness

Property/fuzz-style tests should cover unusual Unicode, repeated separators, empty
segments, very long names and malformed brackets. Recognition should not throw on
arbitrary valid .NET strings.

### 5. Performance

Recognition is expected to be cheap enough for library scans. Stage 6 will add a
repeatable benchmark corpus and guard against accidental regex backtracking or
quadratic behavior. Performance gates should be based on measured CI baselines rather
than guessed microsecond targets.

## Privacy

Private paths, account names, server names and WebDAV URLs must stay out of the public
corpus. Real examples should be minimized and sanitized before commit.
