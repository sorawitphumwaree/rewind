using Rewind.Abstractions;

namespace Rewind.Agent.Core;

public sealed class AgentOptions
{
    public string PipeName { get; init; } = "Rewind.Agent";
    public string DataDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "incidents");
    public int MaximumEventCount { get; init; } = 100_000;
    public long MaximumBufferBytes { get; init; } = 128 * 1024 * 1024;
    public TimeSpan Retention { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PreTrigger { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PostTrigger { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaximumCapture { get; init; } = TimeSpan.FromMinutes(15);
    public bool MergeTriggers { get; init; } = true;
    public int MaximumConcurrentClients { get; init; } = 16;
    public int MaximumStoredIncidents { get; init; } = 100;
    public long MaximumStorageBytes { get; init; } = 5L * 1024 * 1024 * 1024;
    public int MaximumTriggersPerCapture { get; init; } = 64;
    public IReadOnlyDictionary<RewindLevel, LevelPolicy> LevelPolicies { get; init; }
        = LevelPolicy.Defaults;
    public ContinuousLogOptions ContinuousLog { get; init; } = new();

    public LevelPolicy PolicyFor(RewindLevel level)
        => LevelPolicies.TryGetValue(level, out LevelPolicy? policy)
            ? policy
            : LevelPolicy.Defaults[level];
}

public sealed class LevelPolicy
{
    public static IReadOnlyDictionary<RewindLevel, LevelPolicy> Defaults { get; }
        = new Dictionary<RewindLevel, LevelPolicy>
        {
            [RewindLevel.Trace] = new(true, false, false, true),
            [RewindLevel.Debug] = new(true, false, false, true),
            [RewindLevel.Information] = new(true, false, false, true),
            [RewindLevel.Warning] = new(true, true, false, true),
            [RewindLevel.Error] = new(true, true, true, true),
            [RewindLevel.Critical] = new(true, true, true, true),
        };

    public LevelPolicy(
        bool buffer,
        bool persistContinuously,
        bool triggerIncident,
        bool includeInIncident)
    {
        Buffer = buffer;
        PersistContinuously = persistContinuously;
        TriggerIncident = triggerIncident;
        IncludeInIncident = includeInIncident;
    }

    public bool Buffer { get; }
    public bool PersistContinuously { get; }
    public bool TriggerIncident { get; }
    public bool IncludeInIncident { get; }
}

public sealed class ContinuousLogOptions
{
    public string DirectoryName { get; init; } = "logs";
    public long MaximumFileBytes { get; init; } = 100L * 1024 * 1024;
    public long MaximumTotalBytes { get; init; } = 5L * 1024 * 1024 * 1024;
    public int MaximumFileCount { get; init; } = 100;
}
