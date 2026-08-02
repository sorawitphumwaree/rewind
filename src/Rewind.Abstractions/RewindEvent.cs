using System;
using System.Collections.Generic;

namespace Rewind.Abstractions;

public sealed class RewindEvent
{
    public RewindEvent(
        Guid eventId,
        DateTimeOffset timestampUtc,
        long clientSequence,
        RewindLevel level,
        string source,
        string name,
        string message,
        IReadOnlyDictionary<string, string> context,
        int processId,
        int threadId)
    {
        EventId = eventId;
        TimestampUtc = timestampUtc;
        ClientSequence = clientSequence;
        Level = level;
        Source = source;
        Name = name;
        Message = message;
        Context = context;
        ProcessId = processId;
        ThreadId = threadId;
    }

    public Guid EventId { get; }
    public DateTimeOffset TimestampUtc { get; }
    public long ClientSequence { get; }
    public RewindLevel Level { get; }
    public string Source { get; }
    public string Name { get; }
    public string Message { get; }
    public IReadOnlyDictionary<string, string> Context { get; }
    public int ProcessId { get; }
    public int ThreadId { get; }
}
