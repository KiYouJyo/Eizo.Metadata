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

## Runtime 0.2.17 scope

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

**Metadata Runtime 0.2.17 — final resolver closure pass.**

Metadata 0.2.17 follows the 0.2.16 real-library validation pass, which raised resolved
metadata coverage from 89.60% to 91.68% while leaving Recognition behavior unchanged.
This release keeps the same global 0.82 score and 0.06 winner-lead safety gates and
focuses only on remaining high-confidence resolver failures.

Provider search now emits punctuation-folded, parenthesized bilingual, and conservative
year-pinned variants. This lets local names such as `LoveLive! Sunshine!!`,
`龙樱 (Dragon Sakura)`, and library-sorted `鲁邦三世part4` reach canonical provider
subjects without teaching the offline Recognition parser provider-specific aliases.
Bangumi evaluates up to seven strong query variants while keeping the 50-subject recovery
window introduced in 0.2.16.

Bangumi alias enrichment also performs two narrow identity bridges before Core scoring:
a Latin bridge requires the same first three stable tokens plus a matching long final
anchor, while a CJK bridge requires a substantial in-order title match with provider
words inserted between the local title characters. These rules cover translated arc
wording and descriptive Chinese aliases without converting ordinary franchise
containment into an exact-title match.

Recognition remains frozen and provider-neutral. Low-value extras such as menus,
trailers, NCOP/NCED and bonus material are intentionally allowed to remain unresolved;
they are not a reason to weaken resolver safety or continue adding parser rules.

## License

A project license has not yet been declared.
