## Summary

Describe the Recognition or metadata change.

## Boundary check

- [ ] Recognition remains deterministic and network-free.
- [ ] Recognition does not reference provider-specific APIs or models.
- [ ] New parsing behavior includes tests.
- [ ] Existing regression corpus still passes.
- [ ] User/private media paths are not committed to the repository.

## Validation

- [ ] `dotnet build Eizo.Metadata.slnx -c Release`
- [ ] `dotnet test Eizo.Metadata.slnx -c Release`
