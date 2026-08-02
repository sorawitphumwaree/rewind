# SDK Integration

## Install

Modern .NET:

```powershell
dotnet add package Rewind.Sdk --version 0.1.0-alpha.1
```

.NET Framework Package Manager Console:

```powershell
Install-Package Rewind.Sdk -Version 0.1.0-alpha.1
```

`Rewind.Sdk` targets .NET Framework 4.6.2 and .NET Standard 2.0. NuGet selects
the compatible target and resolves matching `Rewind.Protocol` and
`Rewind.Abstractions` dependencies automatically.

## Process-wide static recorder

`RewindRecorder` is a thread-safe static facade. The executable initializes it
once; application classes and referenced DLLs call it directly.

The static recorder is shared only by callers resolving the same loaded
`Rewind.Sdk` assembly instance. A plugin must not load a private SDK copy into a
separate `AppDomain` or `AssemblyLoadContext`. Keep one NuGet version across the
application and plugin dependency graph.

Repeated initialization returns `AlreadyInitialized` and leaves the original
configuration active. Calls before initialization safely do nothing.

## SDK options

| Option | Default | Purpose |
|---|---:|---|
| `AgentPipeName` | `Rewind.Agent` | Local Named Pipe; must match the Agent |
| `EventQueueCapacity` | `4096` | Maximum queued events |
| `ControlQueueCapacity` | `64` | Maximum queued triggers/control items |
| `ConnectTimeoutMilliseconds` | `250` | Bounded Agent connection attempt |
| `MaximumContextEntries` | `64` | Process-wide shared-context limit |

SDK options are immutable after initialization. Restart the application to change
them.

## Event fields

Every event has:

- level;
- source;
- event name;
- opaque message string;
- current shared-context snapshot;
- process and thread identity;
- event/client sequence and timestamp metadata.

The SDK does not serialize arbitrary application objects or redact content. The
application must serialize data and remove passwords, keys, personal data, or
other sensitive content before calling Rewind.

## Shared context

Use shared context only for stable process-wide facts:

```csharp
RewindRecorder.SetContext("MachineId", "HF-INIT-03");
RewindRecorder.SetContext("SoftwareVersion", "2.4.7");
RewindRecorder.SetContext("Application", "MachineController");
```

Do not use shared context for concurrent unit, batch, recipe, or operation values.
Put those values directly in the event message.

```csharp
bool removed = RewindRecorder.RemoveContext("Application");
RewindRecorder.ClearContext();
```

## Delivery semantics

SDK admission is bounded and best-effort:

- caller threads do not wait for Agent acknowledgement;
- caller threads perform no Agent or disk I/O;
- queue-full and invalid events are dropped and counted;
- the sender reconnects after the Agent returns;
- delivery is at-most-once;
- no replay guarantee exists after a connection failure.

`FlushAsync` reports whether accepted items left the SDK queue. It does not prove
Agent acceptance or durable storage.

## Health and shutdown

```csharp
RewindHealthSnapshot health = RewindRecorder.GetHealthSnapshot();

Console.WriteLine(
    $"accepted={health.Accepted}, sent={health.Sent}, " +
    $"droppedQueueFull={health.DroppedQueueFull}, " +
    $"droppedInvalid={health.DroppedInvalid}, " +
    $"transportFailures={health.TransportFailures}, " +
    $"pending={health.Pending}");
```

At normal application shutdown:

```csharp
ShutdownResult result =
    await RewindRecorder.ShutdownAsync(TimeSpan.FromSeconds(2));

if (!result.Completed)
{
    // Report through the application's existing diagnostics.
    Console.Error.WriteLine(
        $"Rewind shutdown incomplete. Unresolved={result.UnresolvedCount}");
}
```

Do not report Rewind failures back into Rewind.

## Recommended first integration

1. Initialize in `Main`.
2. Add stable machine/application context.
3. Instrument important state transitions and external-device operations.
4. Use Information for meaningful normal operations.
5. Use Warning for recoverable abnormal behavior.
6. Use Error/Critical only when the configured incident policy should capture
   evidence.
7. Expose SDK health through the application's existing diagnostic surface.
8. Test with the Agent absent, running, and restarted.
