# Upgrade notes

## 0.1.0-alpha.1

Initial pre-release contract. There is no supported upgrade from an earlier
release. Back up completed incident directories before replacing the Agent.

Protocol major version 1 accepts additive JSON fields. A major-version mismatch
disconnects only that client. Incident schema version 1 is portable JSON/JSONL.
