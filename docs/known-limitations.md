# Known limitations

- SDK delivery is bounded, best-effort, and at-most-once.
- `FlushAsync` reports whether the SDK queue was drained; protocol version 1 has
  no Agent acknowledgement and does not prove durable storage.
- Same-user Named Pipe isolation is implemented. Deployments that run the Agent
  service and machine application under different Windows identities require an
  explicit ACL design that is not included in the current alpha.
- Windows Service hosting and installation scripts exist, but clean-machine
  service installation still requires qualification.
- Configuration is read only at Agent startup. Users restart the process or
  service to apply changes.
- Continuous logs use JSONL size rotation and count/byte quotas. They are not
  compressed or indexed.
- Staging recovery quarantines incomplete packages rather than resuming writes.
- A 24-hour representative-machine soak and minimum-hardware performance report
  have not been completed.
- The selected project name has material public conflicts. The owner accepted
  that risk; availability of exact NuGet IDs is not trademark clearance.
- Public Agent executables are unsigned until trusted Authenticode signing
  becomes available. Verify `SHA256SUMS.txt` before running downloaded files and
  expect Windows reputation or security warnings.
