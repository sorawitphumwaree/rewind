# Platform support

Last verified: 2026-08-02.

- The Agent, samples, soak runner, and verification executable target .NET 10.
- The Agent supports interactive console and Windows Service lifetimes.
- The Agent transport is Windows Named Pipes.
- Supported operating systems are Microsoft-supported Windows 11 versions and
  Windows 10 Enterprise/LTSC editions supported by .NET 10.
- `Rewind.Abstractions`, `Rewind.Protocol`, and `Rewind.Sdk` compile for both
  `net462` and `netstandard2.0`.
- The current CI and local Release build compile both SDK targets.

Runtime verification inside a representative .NET Framework 4.6.2 machine
application and the 24-hour Windows 10 industrial-PC qualification remain release
gates.

Sources:

- https://dotnet.microsoft.com/en-us/platform/support/policy
- https://learn.microsoft.com/en-us/dotnet/core/install/windows
- https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
