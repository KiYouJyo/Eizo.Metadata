# Contributing

## Recognition rules

Recognition code must remain deterministic, synchronous and network-free. It may inspect
the supplied logical path string but must not require the path to exist on disk.

Do not add provider-specific IDs, HTTP clients, API DTOs or WinUI types to
`Eizo.Metadata.Recognition`.

## Changes

Every parsing rule or bug fix should include at least one positive test and, when the
rule can collide with another token class, at least one negative/collision test.

Prefer small composable recognizers over one large regular expression.

## Test data

Do not commit private paths, account names, WebDAV endpoints, credentials or filenames
that expose personal information. Sanitize real-world examples before adding them to
the public regression corpus.

## Local validation

```powershell
dotnet restore Eizo.Metadata.slnx
./eng/verify-boundaries.ps1
dotnet build Eizo.Metadata.slnx -c Release
dotnet test Eizo.Metadata.slnx -c Release
```
