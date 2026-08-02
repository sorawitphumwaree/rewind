# Build from Source

## Prerequisites

- Windows 10/11 x64.
- .NET 10 SDK matching `global.json`.
- PowerShell.
- Git.

The build restores the .NET Framework 4.6.2 reference assemblies through the
project dependency graph; Visual Studio is optional.

## Clone and verify

```powershell
git clone <repository-url>
cd rewind

dotnet restore Rewind.slnx --locked-mode
dotnet build Rewind.slnx --configuration Release --no-restore
dotnet run --project tests/Rewind.Tests --configuration Release --no-build
```

Run the benchmark smoke:

```powershell
dotnet run --project benchmarks/Rewind.Sdk.Benchmarks `
  --configuration Release `
  --no-build `
  -- 1000
```

Run a short soak:

```powershell
dotnet run --project tests/Rewind.Soak `
  --configuration Release `
  --no-build `
  -- 10 100
```

## Run from source

Agent:

```powershell
dotnet run --project src/Rewind.Agent.Host `
  --configuration Release `
  --no-build `
  -- --config samples/Rewind.Sample.Playground/rewind-agent.playground.json
```

Playground in a second terminal:

```powershell
dotnet run --project samples/Rewind.Sample.Playground `
  --configuration Release `
  --no-build
```

## Build distributable artifacts

```powershell
.\scripts\build-artifacts.ps1
.\scripts\generate-sbom.ps1
.\scripts\generate-checksums.ps1
.\scripts\verify-artifacts.ps1
```

Outputs are written below `artifacts\dist\`:

```text
rewind-agent-win-x64.zip
packages\
  Rewind.Abstractions.<version>.nupkg
  Rewind.Abstractions.<version>.snupkg
  Rewind.Protocol.<version>.nupkg
  Rewind.Protocol.<version>.snupkg
  Rewind.Sdk.<version>.nupkg
  Rewind.Sdk.<version>.snupkg
sbom.spdx.json
SHA256SUMS.txt
```

The Agent is published as a self-contained single-file Windows x64 executable.
The three NuGet packages are published to nuget.org; application developers
install only `Rewind.Sdk`, which resolves the other two packages transitively.

## Verify checksums

From `artifacts\dist`:

```powershell
Get-FileHash .\rewind-agent-win-x64.zip -Algorithm SHA256
```

Compare the results with `SHA256SUMS.txt`.

## Development rules

- Use short-lived branches and GitHub Flow.
- Keep commits atomic and use Conventional Commits.
- Keep compiler and analyzer warnings at zero.
- Update tests, schema, documentation, and upgrade notes with behavioral changes.
- Use Semantic Versioning for public API, protocol, configuration, and incident
  schema changes.
- Do not create a tag or release without owner approval.
