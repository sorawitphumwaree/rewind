namespace Rewind.Sdk;

public sealed class FlushResult
{
    public FlushResult(bool completed, long unresolvedCount)
    {
        Completed = completed;
        UnresolvedCount = unresolvedCount;
    }

    public bool Completed { get; }
    public long UnresolvedCount { get; }
}

public sealed class ShutdownResult
{
    public ShutdownResult(bool completed, long unresolvedCount)
    {
        Completed = completed;
        UnresolvedCount = unresolvedCount;
    }

    public bool Completed { get; }
    public long UnresolvedCount { get; }
}
