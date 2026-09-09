# Architecture

## Repository boundary

`Eizo.Metadata` will eventually contain both offline Recognition and online metadata
provider/orchestration projects, but these layers have intentionally different
responsibilities.

Recognition converts noisy path text into structured candidates. Metadata providers
map those candidates to canonical external records.

```text
Media path / filename
        |
        v
Eizo.Metadata.Recognition
        |
        | RecognitionResult
        v
future metadata orchestration
        |
        +--> TMDB provider
        +--> AniList provider
        +--> Bangumi provider
        +--> other providers
```

The dependency direction is one-way: future provider/orchestration projects may
reference Recognition. Recognition must never reference them.

## Recognition pipeline

The planned internal pipeline is:

```text
RecognitionRequest
      |
      v
Path decomposition
      |
      v
Unicode-safe normalization
      |
      v
Tokenization / tag classification
      |
      +--> season & episode extractor
      +--> title candidate extractor
      +--> year extractor
      +--> special/movie extractor
      +--> release-noise classifier
      |
      v
Candidate conflict resolution
      |
      v
Confidence + evidence
      |
      v
RecognitionResult
```

Each stage should preserve enough evidence to explain why a result was produced.

## Hard rules

1. No network access in Recognition.
2. No file-system existence requirement.
3. No WinUI, playback or provider dependency.
4. Unicode/CJK text must be preserved unless a normalization step is explicitly
   reversible or evidence-backed.
5. Recognition must return an uncertain result rather than fabricate certainty.
6. A filename rule must not silently depend on one provider's naming conventions.

## Public contract policy

The public contract should stay small. Internal token classes, regexes, scoring rules
and parser stages remain internal unless Eizo needs them directly.

Breaking contract changes are acceptable before the first stable package release, but
they should be deliberate and documented.
