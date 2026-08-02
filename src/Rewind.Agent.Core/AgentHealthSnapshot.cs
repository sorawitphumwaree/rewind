namespace Rewind.Agent.Core;

public sealed record AgentHealthSnapshot(
    long Accepted,
    long Rejected,
    long StorageFailures,
    long ActiveClients,
    long PeakClients,
    long QuarantinedStagingDirectories,
    long IngestionSequence);
