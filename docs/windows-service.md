# Windows Service Operation

The Agent executable supports both interactive console and Windows Service
lifetimes. Test the configuration interactively before installing the service.

## Identity requirement

The private alpha uses current-user Named Pipe isolation. The Windows Service and
instrumented application must run under the same Windows account.

Use a dedicated least-privilege local or domain account when appropriate. Grant
that account:

- read/execute permission on the Agent installation directory;
- read permission on the configuration file;
- modify permission on the configured data directory;
- the Windows **Log on as a service** right when required by local policy.

Do not run the service as LocalSystem when the application runs as a normal user;
their Named Pipe identities will not match.

## Install with automatic startup

Open PowerShell as Administrator:

```powershell
cd "C:\Program Files\Rewind"

$credential = Get-Credential

.\install-service.ps1 `
  -AgentExecutable "C:\Program Files\Rewind\Rewind.Agent.Host.exe" `
  -ConfigurationFile "C:\ProgramData\Rewind\rewind-agent.json" `
  -Credential $credential
```

The script creates `RewindAgent` with startup type `Automatic`.

If PowerShell blocks the downloaded script, verify the release checksum first,
then unblock the two service scripts:

```powershell
Unblock-File "C:\Program Files\Rewind\install-service.ps1"
Unblock-File "C:\Program Files\Rewind\uninstall-service.ps1"
```

Start and inspect it:

```powershell
Start-Service RewindAgent
Get-Service RewindAgent
```

Verify that the service account can create files under the configured data
directory. Then start the instrumented application and inspect its SDK health.

## Apply configuration

Configuration is startup-only:

```powershell
Restart-Service RewindAgent
```

If restart fails, run the Agent interactively with the same configuration to see
the validation error:

```powershell
.\Rewind.Agent.Host.exe --config "C:\ProgramData\Rewind\rewind-agent.json"
```

## Stop and start

```powershell
Stop-Service RewindAgent
Start-Service RewindAgent
Restart-Service RewindAgent
```

Stopping the Agent never stops the instrumented application. SDK events may remain
queued or be dropped while the Agent is unavailable.

## Upgrade

1. `Stop-Service RewindAgent`
2. Back up the configuration and completed incidents.
3. Replace `Rewind.Agent.Host.exe` and its accompanying documentation/scripts.
4. Review upgrade notes and configuration schema changes.
5. `Start-Service RewindAgent`
6. Verify a controlled event and incident.

## Remove

Open PowerShell as Administrator:

```powershell
cd "C:\Program Files\Rewind"
.\uninstall-service.ps1
```

The uninstall script stops and deletes the service. It does not delete
configuration, logs, or incident packages.

## Common service failures

### Error 1069 or logon failure

Verify the stored credential and the account's **Log on as a service** right.

### Service starts but the SDK cannot connect

Verify:

- service and application run under the same Windows identity;
- Agent and SDK pipe names match exactly;
- only one Agent instance uses that pipe name;
- the Agent process remains running;
- SDK `TransportFailures` and `Pending` counters.

### Service cannot write data

Grant the service identity modify permission on the configured data directory.
Do not solve this by granting broad write permission to the Agent installation
directory or the entire system drive.
