# Rewind playground

This sample demonstrates one initialization in the executable and event calls from
both the executable and a separate class-library project. No recorder object is
passed to the component.

From the repository root, open two terminals.

Terminal 1:

```powershell
dotnet run --project src/Rewind.Agent.Host -- --config samples/Rewind.Sample.Playground/rewind-agent.playground.json
```

Terminal 2:

```powershell
dotnet run --project samples/Rewind.Sample.Playground
```

Use menu option 4 to emit an Error, which triggers an incident under the supplied
configuration. After the configured post-trigger window, inspect:

- `samples/Rewind.Sample.Playground/data/logs/` for continuous Warning, Error, and Critical events.
- `samples/Rewind.Sample.Playground/data/incidents/` for completed incident packages.

The Agent reads configuration only at startup. Stop and restart it after editing
the JSON file.
