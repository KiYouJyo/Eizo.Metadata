# Eizo.Metadata.Recognition development plan

## Goal

Build a deterministic offline recognition engine optimized first for Japanese anime
and Japanese TV drama naming conventions, while remaining useful for ordinary series
and movies.

The output is not canonical metadata. It is a structured candidate that later metadata
providers can search and verify.

---

## Stage 0 — Foundation

Status: **implemented by the repository bootstrap**.

Deliverables:

- .NET 10 solution and Recognition class library;
- provider-neutral public request/result/evidence contracts;
- xUnit v3 test project;
- Windows + Linux CI;
- NuGet packaging artifact;
- dependency-boundary verification;
- repository coding, privacy and testing rules.

Exit gate:

- CI is green on both operating systems;
- the Recognition package has no runtime NuGet or project dependencies.

---

## Stage 1 — Path normalization and token model

Status: **implemented and CI-verified**.

The preprocessing layer now decomposes logical paths, normalizes Unicode, preserves raw
source spans, extracts balanced bracket groups and classifies common release/technical
noise. The first sanitized anime/J-drama-style smoke corpus contains 120 cases.

Scope:

- split logical path into directory and filename context without requiring file access;
- strip known media extensions;
- Unicode normalization policy;
- classify separators without destroying CJK punctuation;
- extract balanced bracket groups;
- classify obvious release-noise tokens such as resolution, source, codec, audio,
  language and checksum markers;
- retain original spans so normalization remains explainable.

Representative inputs:

- `[ANi] 葬送のフリーレン - 14 [1080P][Baha][WEB-DL][AAC AVC][CHT].mp4`
- `VIVANT.S01E03.1080p.WEB-DL.mkv`
- `ドラゴン桜 2021 第03話.mp4`

Exit gate:

- normalization never requires network or disk access;
- CJK title text survives normalization;
- year/resolution/codec tokens are distinguishable from episode candidates;
- tests cover malformed brackets and repeated separators.

---

## Stage 2 — Season and episode extraction

Status: **implemented and CI-verified**.

The default `RecognitionEngine` now consumes Stage 1 preprocessing and emits
provider-neutral season/episode candidates. Episode numbers use decimal values so
explicit forms such as `12.5` can be represented without lossy conversion.

Implemented syntax includes:

- `S01E03`, `S1E3`, `1x03`;
- `EP03`, `E03`, `Episode 03`;
- Japanese `第3話`, `第03話`;
- ranges such as `S01E03-E04`, `E01-E02` and `EP01-EP02`;
- anime-style delimited bare numbers such as `Title - 14`;
- pure numeric filenames such as `01.mkv`;
- explicit decimal episode values;
- Season / 第N期 / ordinal Season folder context;
- Cour / 第Nクール / ordinal Cour folder context.

Collision protection covers:

- years such as 1998 / 2026;
- 360 / 480 / 576 / 720 / 1080 / 1440 / 2160 / 4320 resolution values;
- codec and bit-depth tags such as x264 / x265 / H264 / H265 / 8bit / 10bit;
- ambiguous unbounded numbers, which are left unresolved instead of being forced.

The filename season wins when directory context conflicts, while the conflict is kept
as explicit evidence. Regex matching has bounded timeouts, and malformed bracket
preprocessing is linear rather than repeatedly rescanning the same suffix.

Exit gate:

- strong episode patterns are parsed without title knowledge;
- ambiguous bare numbers are not forced;
- supported rules have positive and collision tests;
- deterministic output and malformed-input robustness are covered by tests.

---

## Stage 3 — Title candidate extraction

Status: **implemented and CI-verified**.

The recognizer now generates deterministic title candidates from both filenames and
directory context. Filename candidates remove only evidence-backed episode/technical
noise, while episode-only filenames fall back to the nearest meaningful parent
directory. Numeric and bracketed title content is preserved conservatively.


Scope:

- remove release group and technical noise only when classified with evidence;
- use parent-directory context when the filename is mostly episode information;
- support Japanese, Chinese and Latin-script titles without transliteration;
- preserve punctuation that can belong to a real title;
- emit more than one candidate internally when ambiguity exists;
- choose a primary candidate with evidence rather than destructive cleanup.

Exit gate:

- common fansub and WebDL names yield clean search titles;
- folder-based libraries work when filenames are only `01.mkv`, `02.mkv`, etc.;
- title cleanup cannot consume season/episode evidence;
- representative numeric/bracketed titles have regression coverage;
- Stage 3 brings the suite to 389 passing tests on Windows and Linux.

---

## Stage 4 — Anime and Japanese-drama specialization

Status: **implemented and CI-verified**.

Recognition now exposes provider-neutral domain semantics for anime extras, movie
markers, cour context, and Japanese-drama episode forms. Specials carry their own
subtype/number rather than being silently represented as ordinary episodes.


Anime scope:

- OVA / OAD / ONA / SP / Specials;
- NCOP / NCED and other non-episode extras;
- absolute numbering and season-folder context;
- cour markers where they are explicit;
- movie and theatrical-release markers;
- common release-group bracket layouts.

Japanese-drama scope:

- `第N話`, `第N回`, `最終話`, `前編`, `後編`;
- year-qualified remake titles;
- season labels and specials;
- episode names appended after the number.

Exit gate:

- the sanitized Stage 4 golden corpus contains 500 cases;
- anime and Japanese-drama cases are both represented as first-class categories;
- specials are not silently treated as ordinary numbered episodes;
- public results expose SpecialKind, SpecialNumber, EpisodePart, IsFinalEpisode and CourNumber;
- the full suite reaches 912 passing tests on Windows and Linux.

---

## Stage 5 — Candidate ranking, confidence and explainability

Turn independent parser findings into a stable decision system.

Scope:

- weighted evidence model;
- conflict-resolution rules;
- explicit uncertainty;
- deterministic tie breaking;
- confidence calibration by syntax strength;
- diagnostics that explain title/season/episode decisions.

Example evidence:

```text
title.parent-directory     +0.70
episode.sxxexx             +0.95
year.four-digit            +0.80
noise.resolution           +0.95
special.ova-marker         +0.90
```

Exit gate:

- the same request always produces the same result and evidence order;
- low-confidence inputs remain low confidence;
- diagnostics are useful enough to debug a bad library match without provider logs.

---

## Stage 6 — Corpus hardening and performance

Move from feature completeness to reliability.

Scope:

- grow sanitized corpus to at least 2,000 cases;
- convert every discovered bug into a regression case;
- Unicode and malformed-input robustness tests;
- regex timeout/backtracking review;
- benchmark representative 1k/10k/100k library scans;
- remove or rewrite rules that improve a narrow case while creating broad false
  positives.

Exit gate:

- zero known crashes on corpus/fuzz inputs;
- stable benchmark baseline documented in CI artifacts or repository docs;
- false-positive rate is tracked separately from extraction coverage.

---

## Stage 7 — Eizo integration

Integrate only after the Recognition contract and corpus are stable.

Flow:

```text
Eizo source scan
   -> logical media path
   -> Eizo.Metadata.Recognition
   -> RecognitionResult
   -> library candidate
   -> future metadata provider matching
```

Integration requirements:

- Recognition runs off the UI thread when scanning large libraries;
- result/evidence can be logged without leaking credentials or WebDAV authority data;
- Recognition failure never blocks raw media playback;
- Eizo can retain unmatched media as an unresolved library item.

Exit gate:

- local and WebDAV paths use the same Recognition API;
- scan behavior is deterministic;
- unmatched items remain usable.

---

## After Recognition

Only after Stage 5 is stable should the repository begin the online metadata layer,
for example:

```text
Eizo.Metadata.Core
Eizo.Metadata.Tmdb
Eizo.Metadata.AniList
Eizo.Metadata.Bangumi
```

Those projects may consume `RecognitionResult`. They must not push provider-specific
assumptions back into Recognition.
