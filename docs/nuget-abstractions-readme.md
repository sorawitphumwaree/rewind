# Rewind.Abstractions

Shared event and severity contracts used by `Rewind.Sdk` and
`Rewind.Protocol`.

Application developers normally install only `Rewind.Sdk`; NuGet resolves this
package automatically. Reference `Rewind.Abstractions` directly only when a
library intentionally exposes Rewind contracts in its public API.

The package targets .NET Framework 4.6.2 and .NET Standard 2.0.
