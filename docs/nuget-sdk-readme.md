# Rewind.Sdk

Process-wide, bounded, best-effort SDK for the Rewind local incident flight
recorder.

## Install

```powershell
dotnet add package Rewind.Sdk --prerelease
```

For a .NET Framework project using Package Manager Console:

```powershell
Install-Package Rewind.Sdk -Prerelease
```

Installing `Rewind.Sdk` automatically installs the matching
`Rewind.Protocol` and `Rewind.Abstractions` dependencies.

## Initialize once

```csharp
using Rewind.Sdk;

RewindRecorder.Initialize(new RewindOptions
{
    AgentPipeName = "Rewind.Agent"
});
```

Then emit from any code using the same loaded SDK assembly:

```csharp
RewindRecorder.Information("Machine", "CycleCompleted", "Cycle=1842");
RewindRecorder.Warning("Vision", "LowConfidence", "Score=0.71");
RewindRecorder.Error("Motion", "MoveFailed", "Axis=X;Reason=Timeout");
```

The separate Rewind Agent is distributed through the project's GitHub Releases.
Run the Agent interactively first, then install it as a Windows Service when the
configuration is proven.

The SDK targets .NET Framework 4.6.2 and .NET Standard 2.0. Delivery is bounded,
best-effort, and at-most-once.
