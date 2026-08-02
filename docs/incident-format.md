# Incident format version 1

A completed incident is a directory named by incident UUID. Consumers must only
recognize a package when `manifest.json` exists and has `status: "complete"`.

Required files:

- `manifest.json` — completion marker and record counts.
- `events.jsonl` — one UTF-8 JSON event envelope per line, in Agent ingestion order.
- `triggers.json` — UTF-8 JSON array of trigger envelopes.
- `configuration.json` — sanitized immutable effective configuration.
- `recorder-health.json` — bounded counters captured during finalization.

Each persisted event adds `agentReceivedUtc` and `ingestionSequence`. Existing
wire-envelope fields and the opaque payload are preserved. Unknown additive fields
must be ignored for schema version 1.

Staged packages live below `incidents/.staging/` and are never complete. On Agent
restart they move to `incidents/.quarantine/` for diagnosis.
