# Rewind

Rewind is a local-first incident flight recorder for Windows machine software.
It has two deployable parts:

- `Rewind.Sdk`: a process-wide static API targeting .NET Framework 4.6.2 and
  .NET Standard 2.0.
- `Rewind.Agent.Host`: a headless .NET 10 process that runs interactively or as
  a Windows Service.

The SDK uses bounded in-process queues and sends events to the Agent over a local
Windows Named Pipe. Application calls never perform Agent or disk I/O. Delivery
is best-effort and health counters expose known losses.

The Agent can continuously persist selected levels and retain a bounded rolling
buffer. A configured level or an explicit SDK call can trigger an atomic incident
package containing selected pre-trigger and post-trigger events.

## Use a prebuilt release

Install the SDK from NuGet:

```powershell
dotnet add package Rewind.Sdk --version 0.1.0-alpha.1
```

Download `rewind-agent-win-x64.zip` from the matching GitHub Release. The Agent
bundle contains a self-contained Windows x64 executable, example configuration,
JSON Schema, operating guides, and Windows Service scripts.

Start with [the complete user guide](docs/user-guide.md).

## Build and verify

```powershell
dotnet restore Rewind.slnx
dotnet build Rewind.slnx --configuration Release --no-restore
dotnet run --project tests/Rewind.Tests --configuration Release --no-build
```

## Try the playground

Open two terminals at the repository root.

Terminal 1:

```powershell
dotnet run --project src/Rewind.Agent.Host -- --config samples/Rewind.Sample.Playground/rewind-agent.playground.json
```

Terminal 2:

```powershell
dotnet run --project samples/Rewind.Sample.Playground
```

The executable initializes Rewind once. A separate component DLL then emits
events through the same static recorder without receiving a recorder object.
See `samples/Rewind.Sample.Playground/README.md`.

## Agent configuration

Run with a JSON configuration:

```powershell
Rewind.Agent.Host.exe --config C:\ProgramData\Rewind\rewind-agent.json
```

The Agent reads configuration once at startup. Restart the console process or
Windows Service after changing the file. There is no live reload.

Without a configuration file, the interactive host accepts:

```text
--pipe <name> --data <directory> --pre <seconds> --post <seconds>
```

## Windows Service

Publish the Agent, then run an elevated PowerShell:

```powershell
.\scripts\install-service.ps1 `
  -AgentExecutable C:\Rewind\Rewind.Agent.Host.exe `
  -ConfigurationFile C:\ProgramData\Rewind\rewind-agent.json `
  -Credential (Get-Credential)

Start-Service RewindAgent
Restart-Service RewindAgent
```

Use `scripts/uninstall-service.ps1` to remove the service. The installation
scripts never run automatically. The private alpha requires the service to run
under the same Windows identity as the instrumented applications because the
Named Pipe uses current-user isolation.

## Supported baseline

- Agent, samples, and executable verification: .NET 10 LTS on supported Windows
  10 Enterprise/LTSC and Windows 11 versions.
- SDK, abstractions, and protocol: `net462` and `netstandard2.0`.
- Transport: same-machine Windows Named Pipes only.

The current build compiles every SDK target. Representative Windows 10 industrial
hardware runtime qualification remains required before a public release.

## License and release status

Licensed under the Apache License, Version 2.0. “Rewind” remains the chosen name
after preliminary conflict screening; package-name availability is not trademark
clearance.

No public package, tag, or release has been created. Public executables require a
trusted Authenticode signing process.

See `docs/platform-support.md`, `docs/known-limitations.md`, and `SUPPORT.md`.

## Documentation

- [Complete user guide](docs/user-guide.md)
- [SDK integration](docs/sdk-integration.md)
- [Agent configuration](docs/configuration.md)
- [Windows Service operation](docs/windows-service.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Build from source](docs/build-from-source.md)
- [Distribution and releases](docs/distribution.md)
- [Incident format](docs/incident-format.md)
- [Known limitations](docs/known-limitations.md)
