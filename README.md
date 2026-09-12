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

## Runtime 0.2.10 scope

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

**Metadata Runtime 0.2.10 — relation-aware sequel pass.**

Metadata 0.2.10 builds on the 0.2.9 season-range fix and addresses franchises whose
provider subjects use named arcs instead of numeric seasons. Providers may expose an
optional related-subject graph; Bangumi uses the public v0 subject-relations endpoint.
When ordinary scoring remains unresolved for an exact base title and Recognition has
Season 2+, the resolver follows only a unique series-level sequel edge at each step.
A unique chain can therefore map local seasons to named arcs without hard-coded franchise
tables. Ambiguous sequel branches, provider failures, movies, and non-series relations
remain unresolved. The existing 0.82 / 0.06 safety gates and long-running-series local
partition behavior are preserved.

## License

A project license has not yet been declared.
