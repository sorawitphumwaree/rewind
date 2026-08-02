# Resource bounds

Last reviewed: 2026-08-02.

| Resource | Default bound | Failure behavior |
|---|---:|---|
| SDK event queue | 4,096 items | New event is dropped and counted |
| SDK control queue | 64 items | New control message is dropped and counted |
| SDK field length | 65,536 UTF-16 characters | Item is rejected and counted |
| Protocol frame | 1 MiB | SDK rejects before enqueue; Agent isolates the offending client |
| SDK connect attempt | 250 ms | Failure counted; reconnect delayed |
| Agent client sessions | 16 | Additional clients wait for a slot |
| Agent buffer count | 100,000 events | Oldest event evicted |
| Agent buffer bytes | 128 MiB | Oldest event evicted |
| Agent buffer time | 5 minutes | Oldest expired event evicted on append |
| Capture pre-window | 5 minutes | Older evidence is excluded |
| Capture post-window | 1 minute | Capture finalizes after the deadline |
| Merged capture | 15 minutes | Later triggers cannot extend past the ceiling |
| Triggers per capture | 64 | Additional triggers are rejected and counted |
| Completed incidents | 100 | Oldest completed package is removed |
| Incident storage | 5 GiB | Oldest completed package is removed |
| Continuous file | 100 MiB | Writer rotates to the next JSONL file |
| Continuous files | 100 | Oldest file is removed |
| Continuous storage | 5 GiB | Oldest file is removed |

Configuration validation rejects non-positive or internally conflicting bounds.
All collections and persisted stores have an explicit count, byte, time, or
single-active-capture boundary.
