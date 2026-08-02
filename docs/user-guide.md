# Rewind User Guide

This guide takes a Windows application from download to a working Rewind
installation. Rewind has two parts:

1. The SDK runs inside the application and sends events through a local Windows
   Named Pipe.
2. The Agent runs as a separate process, applies capture policy, and writes
   continuous logs and incident packages.

The application remains independent of the Agent. If the Agent is stopped,
restarting, or unavailable, SDK calls remain bounded and events may be dropped
according to the configured SDK queue limits.

## 1. Install the SDK and download the Agent

Install the SDK from nuget.org:

```powershell
dotnet add package Rewind.Sdk --version 0.1.0-alpha.1
```

For a .NET Framework project using Visual Studio Package Manager Console:

```powershell
Install-Package Rewind.Sdk -Version 0.1.0-alpha.1
```

The single `Rewind.Sdk` installation automatically resolves the matching
`Rewind.Protocol` and `Rewind.Abstractions` packages. Do not install mismatched
versions manually.

From the matching GitHub Release, download:

- `rewind-agent-win-x64.zip`
- `SHA256SUMS.txt`

The Agent package is self-contained for Windows x64. The target PC does not need
a separate .NET runtime for the Agent.

Public-alpha Agent artifacts are checksum-verified but unsigned. Verify the downloaded
ZIP against `SHA256SUMS.txt` before extracting it. If Windows marks the verified
files as downloaded and blocks them, unblock only the extracted Rewind files:

```powershell
Get-ChildItem "C:\Program Files\Rewind" -File | Unblock-File
```

## 2. Prepare the Agent

Extract the Agent ZIP to a stable location, for example:

```text
C:\Program Files\Rewind\
```

Create a writable data directory and copy the example configuration:

```text
C:\ProgramData\Rewind\
  rewind-agent.json
  data\
```

The account running the Agent needs:

- read permission for `rewind-agent.json`;
- read/write/create/delete permission below the configured `dataDirectory`;
- permission to execute `Rewind.Agent.Host.exe`.

Edit `rewind-agent.json` before starting the Agent. The most important settings
are:

- `agent.pipeName`: must match the SDK's `AgentPipeName`;
- `agent.dataDirectory`: where logs and incidents are written;
- `levels`: per-level buffering, continuous persistence, triggering, and incident
  inclusion;
- `capture`: pre-trigger and post-trigger windows;
- `incidentStorage` and `continuousLog`: storage quotas.

See [configuration.md](configuration.md) for every setting.

## 3. Run the Agent interactively

Use interactive mode for initial testing:

```powershell
cd "C:\Program Files\Rewind"
.\Rewind.Agent.Host.exe --config "C:\ProgramData\Rewind\rewind-agent.json"
```

The console reports the effective pipe name and data directory. Keep it open
while testing. Press Ctrl+C to stop the Agent gracefully.

After changing configuration, stop and restart the Agent. Configuration is never
reloaded while the process is running.

## 4. Configure the SDK in the application

Initialize Rewind exactly once in the executable entry point:

```csharp
using Rewind.Sdk;

InitializationResult result = RewindRecorder.Initialize(new RewindOptions
{
    AgentPipeName = "Rewind.Agent",
    EventQueueCapacity = 4096,
    ControlQueueCapacity = 64,
    ConnectTimeoutMilliseconds = 250,
    MaximumContextEntries = 64
});

RewindRecorder.SetContext("MachineId", "MACHINE-01");
RewindRecorder.SetContext("SoftwareVersion", "2.4.7");
```

The pipe name must exactly match `agent.pipeName` in the Agent configuration.
There is no TCP endpoint or port in the current release.

Any code in the same process can then emit events without receiving a recorder
object:

```csharp
RewindRecorder.Trace("PLC", "Polling", "Address=DB12");
RewindRecorder.Debug("Motion", "MoveStarted", "Axis=X;Target=120.5");
RewindRecorder.Information("Machine", "CycleCompleted", "Cycle=1842");
RewindRecorder.Warning("Vision", "LowConfidence", "Score=0.71");
RewindRecorder.Error("Motion", "MoveFailed", "Axis=X;Reason=Timeout");
RewindRecorder.Critical("Controller", "Disconnected", "Controller link lost");
```

Manually trigger an incident when application logic knows a failure occurred:

```csharp
RewindRecorder.TriggerIncident(
    "InitializationFailed",
    "Machine could not enter Ready state");
```

At controlled application shutdown:

```csharp
ShutdownResult shutdown =
    await RewindRecorder.ShutdownAsync(TimeSpan.FromSeconds(2));
```

See [sdk-integration.md](sdk-integration.md) for health, shared context, DLL/plugin
behavior, and failure semantics.

## 5. Verify the complete path

With the Agent and application running:

1. Emit Information, Warning, and Error events.
2. Trigger an incident manually or emit a level configured with
   `triggerIncident: true`.
3. Wait for `postTriggerSeconds`.
4. Inspect the configured data directory.

Expected layout:

```text
data\
  logs\
    20260802-0000.jsonl
  incidents\
    <incident-id>\
      manifest.json
      events.jsonl
      triggers.json
      configuration.json
      recorder-health.json
```

Only an incident directory containing `manifest.json` with
`"status": "complete"` is complete evidence.

## 6. Install automatic startup

After interactive testing succeeds, install the same executable as a Windows
Service. Use an elevated PowerShell and run the service under the same Windows
identity as the instrumented application:

```powershell
cd "C:\Program Files\Rewind"

.\install-service.ps1 `
  -AgentExecutable "C:\Program Files\Rewind\Rewind.Agent.Host.exe" `
  -ConfigurationFile "C:\ProgramData\Rewind\rewind-agent.json" `
  -Credential (Get-Credential)

Start-Service RewindAgent
Get-Service RewindAgent
```

The service startup type is `Automatic`, so Windows starts it at boot.

After changing configuration:

```powershell
Restart-Service RewindAgent
```

See [windows-service.md](windows-service.md) for permissions, status, logs,
upgrade, and removal.

## 7. Operate and diagnose

Application-side health:

```csharp
RewindHealthSnapshot health = RewindRecorder.GetHealthSnapshot();
```

Watch:

- `DroppedQueueFull`
- `DroppedInvalid`
- `TransportFailures`
- `Pending`

Non-zero counters do not stop the machine application, but they show that evidence
may be incomplete.

For a durable incident, inspect `manifest.json`, `recorder-health.json`, and the
event/trigger counts before using the package for diagnosis.

## 8. Upgrade

1. Stop the Agent or Windows Service.
2. Back up completed incidents and the active configuration.
3. Replace the Agent package.
4. Update `Rewind.Sdk` through NuGet during the application's normal deployment.
5. Review `upgrade-notes.md`.
6. Start the Agent and verify the pipe and data directory.
7. Run a controlled incident test.

Never combine SDK assemblies from different versions.
