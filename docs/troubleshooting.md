# Troubleshooting

## Windows blocks the executable or PowerShell scripts

Private-alpha artifacts are unsigned. First verify the ZIP checksum against
`SHA256SUMS.txt`. Then unblock only the extracted Rewind files:

```powershell
Get-ChildItem "C:\Program Files\Rewind" -File | Unblock-File
```

Do not disable Smart App Control or machine-wide security controls for Rewind.

## SDK reports transport failures

Check that:

- the Agent is running;
- SDK `AgentPipeName` matches Agent `pipeName`;
- both processes run under the same Windows identity;
- only one Agent owns that pipe name.

The SDK reconnects automatically. The application does not need to reinitialize
the recorder when the Agent restarts.

## Events are missing

Inspect SDK health:

- `DroppedQueueFull` means the bounded queue was full;
- `DroppedInvalid` means a field or frame violated limits;
- `TransportFailures` means connection/send attempts failed;
- `Pending` means items remain in the SDK queue.

Inspect `recorder-health.json` in completed incidents for Agent-side losses and
storage failures.

Also verify the level's `buffer`, `persistContinuously`, and
`includeInIncident` settings.

## Error does not trigger an incident

Verify:

```json
"error": {
  "buffer": true,
  "persistContinuously": true,
  "triggerIncident": true,
  "includeInIncident": true
}
```

Restart the Agent after editing configuration, then wait through
`postTriggerSeconds`.

## No continuous log file appears

Continuous files are created only for levels with `persistContinuously: true`.
Verify data-directory permissions and storage quotas.

## Incident remains under `.staging`

The package did not complete. It must not be treated as durable evidence. On the
next Agent startup, incomplete staging is moved to `.quarantine`.

Check Agent output, filesystem permissions, available disk space, and the
configured quotas.

## Agent configuration fails

Run the Agent interactively:

```powershell
.\Rewind.Agent.Host.exe --config C:\ProgramData\Rewind\rewind-agent.json
```

Common causes:

- misspelled/unknown property;
- malformed JSON;
- relative or unsafe continuous-log directory;
- `preTriggerSeconds` greater than buffer retention;
- `maximumCaptureSeconds` less than `postTriggerSeconds`;
- non-positive count or byte limit;
- inaccessible data directory.

## A plugin does not share initialization

The plugin probably loaded another copy of `Rewind.Sdk.dll` or uses a separate
load context. Deploy one SDK version from the executable and make the plugin
resolve that assembly instance.
