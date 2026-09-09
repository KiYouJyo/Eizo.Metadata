# Eizo.Metadata

Metadata and offline media-recognition infrastructure for **Eizo**.

The repository is intentionally built in layers. The first deliverable is
`Eizo.Metadata.Recognition`: a deterministic, offline parser that turns noisy media
paths and filenames into structured recognition candidates. Online metadata providers
will be added only after the recognition contract is stable.

## Current scope

### Eizo.Metadata.Recognition

Responsibilities:

- parse file and directory naming signals;
- identify media-kind hints;
- extract title candidates, season and episode information;
- recognize specials, ranges, years and common release tags;
- normalize noisy naming without destroying meaningful title text;
- report confidence and evidence for every decision.

Non-responsibilities:

- no HTTP or provider API calls;
- no TMDB, AniList, Bangumi or other provider-specific models;
- no image download or metadata cache;
- no WinUI or playback dependency;
- no mutation of the user's media library.

## Repository layout

```text
src/
  Eizo.Metadata.Recognition/        Offline recognition contracts and implementation

tests/
  Eizo.Metadata.Recognition.Tests/  Unit, regression and corpus tests

docs/
  architecture.md                   Module boundary and dependency rules
  recognition-plan.md               Recognition development stages and acceptance gates
  testing.md                        Test corpus and quality strategy
```

## Dependency rule

```text
Eizo
  |
  v
Eizo.Metadata.Recognition
  ^
  |
future Eizo.Metadata provider/orchestration projects
```

Recognition must remain provider-neutral and fully usable without network access.
Future provider modules may consume Recognition results; Recognition must never depend
on provider modules.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet restore Eizo.Metadata.slnx
dotnet build Eizo.Metadata.slnx -c Release
dotnet test Eizo.Metadata.slnx -c Release
dotnet pack Eizo.Metadata.slnx -c Release -o artifacts/packages
```

## Status

**Recognition Stage 5 — calibrated confidence and explainability.**

Implemented so far:

- provider-neutral Recognition contracts and default `RecognitionEngine`;
- Unicode-safe path preprocessing and release-noise tokenization;
- season/episode/range/decimal parsing;
- filename and parent-directory title candidates;
- public season and cour context;
- OVA / OAD / ONA / SP / Specials / NCOP / NCED classification;
- special numbering without treating specials as ordinary episodes;
- Japanese/English movie marker classification;
- Japanese `第N話` / `第N回`, `最終話`, `前編`, `後編`;
- appended Japanese episode-name cleanup;
- collision protection for title text such as `SPY x FAMILY`, `Special Ops`,
  `Movie Night` and `OVA Project`;
- deterministic evidence output with no network or disk dependency;
- Windows/Linux CI, package validation and **923 passing tests**;
- a **500-case sanitized Stage 4 domain corpus**;
- calibrated `Confidence` plus `RecognitionConfidenceLevel`;
- explicit `IsAmbiguous` output for conflicting evidence;
- ranked, deduplicated public title candidates for Metadata Provider search;
- confidence evidence for title/episode/domain components, consensus and conflicts.

Corpus hardening and performance validation are the next stage.

See [docs/recognition-plan.md](docs/recognition-plan.md) for the staged development
plan and acceptance criteria.

## License

A project license has not yet been declared.
