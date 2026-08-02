# Agent Configuration

The Agent reads its JSON configuration once at startup. It does not watch or
reload the file. Restart the console process or Windows Service after every
configuration change.

Start from `rewind-agent.example.json`. The JSON Schema is
`rewind-agent.schema.json`.

Unknown properties, malformed JSON, unsafe paths, non-positive limits, and
conflicting durations prevent startup.

## Complete shape

```json
{
  "agent": {
    "pipeName": "Rewind.Agent",
    "dataDirectory": "C:\\ProgramData\\Rewind\\data",
    "maximumConcurrentClients": 16
  },
  "buffer": {
    "retentionSeconds": 300,
    "maximumEventCount": 100000,
    "maximumBytes": 134217728
  },
  "capture": {
    "preTriggerSeconds": 300,
    "postTriggerSeconds": 60,
    "maximumCaptureSeconds": 900,
    "mergeTriggers": true,
    "maximumTriggersPerCapture": 64
  },
  "levels": {
    "trace": { "buffer": true, "persistContinuously": false, "triggerIncident": false, "includeInIncident": true },
    "debug": { "buffer": true, "persistContinuously": false, "triggerIncident": false, "includeInIncident": true },
    "information": { "buffer": true, "persistContinuously": false, "triggerIncident": false, "includeInIncident": true },
    "warning": { "buffer": true, "persistContinuously": true, "triggerIncident": false, "includeInIncident": true },
    "error": { "buffer": true, "persistContinuously": true, "triggerIncident": true, "includeInIncident": true },
    "critical": { "buffer": true, "persistContinuously": true, "triggerIncident": true, "includeInIncident": true }
  },
  "incidentStorage": {
    "maximumIncidentCount": 100,
    "maximumBytes": 5368709120
  },
  "continuousLog": {
    "directoryName": "logs",
    "maximumFileBytes": 104857600,
    "maximumTotalBytes": 5368709120,
    "maximumFileCount": 100
  }
}
```

## Agent

`pipeName` identifies the local Windows Named Pipe. It must match
`RewindOptions.AgentPipeName` in every connected application.

`dataDirectory` is the root for `logs`, `incidents`, staging, and quarantine.
A relative path is resolved relative to the configuration file. Production
installations should use an absolute path.

`maximumConcurrentClients` limits simultaneous SDK connections.

## Rolling buffer

The Agent evicts the oldest event whenever any buffer boundary is exceeded:

- `retentionSeconds`;
- `maximumEventCount`;
- `maximumBytes`.

`preTriggerSeconds` cannot exceed `retentionSeconds`.

## Capture

When an event level or explicit SDK call triggers an incident:

1. the Agent selects eligible buffered events from the pre-trigger window;
2. it continues accepting eligible events through the post-trigger window;
3. it writes a staged package;
4. it writes the complete manifest last;
5. it atomically publishes the completed directory.

If `mergeTriggers` is true, another trigger extends the active capture up to
`maximumCaptureSeconds`. `maximumTriggersPerCapture` bounds trigger metadata.

## Level policy

Each level has four independent switches:

| Setting | Meaning |
|---|---|
| `buffer` | Retain the event temporarily in bounded memory |
| `persistContinuously` | Append it to rotated JSONL logs |
| `triggerIncident` | Start or extend an incident automatically |
| `includeInIncident` | Include buffered events of this level in incidents |

If `buffer` is false, that level cannot provide pre-trigger evidence even when
`includeInIncident` is true.

Common policy:

- Trace/Debug: buffer for incidents; do not persist continuously.
- Information: buffer; persist continuously only if operational history warrants
  the storage cost.
- Warning: buffer and persist continuously; trigger only if warnings represent a
  real incident.
- Error/Critical: buffer, persist, trigger, and include.

## Storage quotas

`incidentStorage` removes the oldest completed incident packages when the count or
byte quota is exceeded. Active staging directories are not treated as completed
packages.

`continuousLog` writes UTF-8 JSONL files below `directoryName`, rotates at
`maximumFileBytes`, and removes the oldest files when either total bytes or file
count is exceeded.

Choose quotas that preserve free space required by the machine application and
Windows.

## Applying changes

Interactive Agent:

```powershell
# Press Ctrl+C, then start it again.
.\Rewind.Agent.Host.exe --config C:\ProgramData\Rewind\rewind-agent.json
```

Windows Service:

```powershell
Restart-Service RewindAgent
```

If the new configuration is invalid, the Agent fails startup instead of silently
running with a partially applied policy.
