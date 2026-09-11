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

## Runtime 0.2.3 scope

### Eizo.Metadata.Recognition

Recognition 0.1.6 is frozen as the current parser baseline. It remains completely
offline and provider-neutral. It extracts title candidates, media kind, year, season,
episode, specials and confidence/evidence from local or WebDAV logical paths.

### Eizo.Metadata.Core

Provider-neutral metadata orchestration:

- converts `RecognitionResult` into ranked provider search requests;
- defines canonical subject, title, artwork and episode models;
- runs multiple providers independently and isolates provider failures;
- scores candidates using title similarity, year, media kind and provider rank;
- only auto-resolves when both confidence and winner margin pass configured gates;
- provides memory and file-backed TTL caches;
- provides a caching provider decorator so network providers remain stateless.

### Eizo.Metadata.Providers

Initial online providers:

- **Bangumi** public `api.bgm.tv/v0` search, subject and episode APIs;
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

**Metadata Runtime 0.2.3 — field-report search normalization.**

Recognition remains stable while Metadata 0.2.3 targets the dominant failure mode from
Eizo's large real-library report: recognized titles that fail provider search because
library ordinals, season prefixes, trailing years, release separators or Unicode
punctuation leak into the query. Normalization now happens at the resolver boundary so
both FromRecognition callers and hosts that construct MetadataSearchRequest directly
receive the same conservative search variants without lowering resolution thresholds.

## License

A project license has not yet been declared.
