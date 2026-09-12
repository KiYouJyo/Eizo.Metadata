# Eizo.Metadata

Metadata and offline media-recognition infrastructure for **Eizo**.

The repository is split into three layers with a strict one-way dependency direction:

```text
Eizo.Metadata.Recognition   deterministic, offline filename/path recognition
            |
            v
Eizo.Metadata.Core          canonical metadata contracts, resolver and cache
            |
            v
Eizo.Metadata.Providers     Bangumi / TMDB HTTP provider implementations
```

## Runtime 0.2.16 scope

### Eizo.Metadata.Recognition

Recognition 0.1.6 is frozen as the current parser baseline. It remains completely
offline and provider-neutral. It extracts title candidates, media kind, year, season,
episode, specials and confidence/evidence from local or WebDAV logical paths.

### Eizo.Metadata.Core

Provider-neutral metadata orchestration:

- converts `RecognitionResult` into ranked provider search requests;
- defines canonical subject, title, artwork and episode models;
- runs multiple providers independently and isolates provider failures;
- scores candidates using ranked title aliases, year, media kind, season/installment semantics and provider rank;
- only auto-resolves when both confidence and winner margin pass configured gates;
- provides memory and file-backed TTL caches;
- provides a caching provider decorator so network providers remain stateless.

### Eizo.Metadata.Providers

Initial online providers:

- **Bangumi** public `api.bgm.tv/v0` search, subject, related-subject and episode APIs;
- **TMDB** v3 movie/TV search, subject details, external IDs and season episodes.

HTTP clients are injected by the host. Provider tests use fake HTTP handlers and never
depend on live network availability.

## Dependency rules

```text
Recognition
    ^
    |
Core
    ^
    |
Providers
```

Hard rules:

1. Recognition has no network, provider, UI or playback dependencies.
2. Core may consume Recognition contracts but has no provider-specific HTTP models.
3. Providers depend on Core, never the other way around.
4. One provider failing must not prevent another provider from resolving metadata.
5. Eizo owns credentials and `HttpClient` lifetime; Metadata does not persist secrets.
6. Cached metadata is disposable enrichment. Local media recognition and playback must
   remain usable when every online provider is offline.

## Repository layout

```text
src/
  Eizo.Metadata.Recognition/
  Eizo.Metadata.Core/
  Eizo.Metadata.Providers/

tests/
  Eizo.Metadata.Recognition.Tests/
  Eizo.Metadata.Core.Tests/
  Eizo.Metadata.Providers.Tests/

benchmarks/
  Eizo.Metadata.Recognition.Benchmarks/
```

## Build

Requires the .NET 10 SDK.

```powershell
dotnet restore Eizo.Metadata.slnx
dotnet build Eizo.Metadata.slnx -c Release
dotnet test Eizo.Metadata.slnx -c Release
```

The external Metadata runtime remains one update unit. The runtime package workflow
automatically includes every `src/Eizo.Metadata.*` module while retaining
`Eizo.Metadata.Recognition.dll` as the Eizo 0.3.6 compatibility anchor.

## Status

**Metadata Runtime 0.2.16 — resolver closure pass.**

Metadata 0.2.16 closes the remaining high-value resolver gaps found by the 6,067-file
real-library report without expanding Recognition grammar or lowering the global
0.82 / 0.06 auto-resolve gates.

The provider-search boundary now removes compact season coverage such as `1-2季`,
emits conservative base-title / Part / bilingual / spaced-subtitle variants, and lets
Bangumi search up to five strong query variants with a wider 50-subject recovery window.
This is intended to recover canonical subjects that were previously absent from the
candidate set for franchise-heavy or library-formatted titles.

Resolver scoring now distinguishes an exact named-season title from derivative works
that merely contain that season title, preventing one-episode compilations or OVAs from
tying the canonical season. Two narrowly bounded promotions can close results within
0.02 of the normal score threshold when either the requested installment matches
structurally or an exceptionally strong title winner has a safe lead. Every promotion
adds explicit evidence and promotes the diagnostic score, so downstream reports remain
internally consistent.

Local-season subject families, sequel-chain safety, ambiguity handling, and the existing
single-subject local-split guard remain unchanged; unsafe cases stay unresolved rather
than being forced.
## License

A project license has not yet been declared.
