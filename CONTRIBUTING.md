# Contributing

Open an issue before substantial design changes. Keep changes in small vertical
slices and reference requirement or acceptance IDs from the project specification.

Before submitting a change:

```powershell
dotnet build Rewind.slnx --configuration Release
dotnet run --project tests/Rewind.Tests --configuration Release
```

Protocol, concurrency, storage, security, and public API changes require explicit
compatibility review. Use short-lived branches, atomic Conventional Commits, and
Semantic Versioning for release-facing changes.
