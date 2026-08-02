using Rewind.Agent.Core;

namespace Rewind.Storage;

public static class RewindAgentFactory
{
    public static RewindAgent Create(AgentOptions options)
        => new(
            options,
            new AtomicIncidentWriter(
                options.DataDirectory,
                options.MaximumStoredIncidents,
                options.MaximumStorageBytes),
            new ContinuousLogWriter(
                options.DataDirectory,
                options.ContinuousLog.DirectoryName,
                options.ContinuousLog.MaximumFileBytes,
                options.ContinuousLog.MaximumTotalBytes,
                options.ContinuousLog.MaximumFileCount));
}
