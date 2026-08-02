using System.Text.Json;

namespace Rewind.Agent.Core;

public sealed class BufferedEvent : IDisposable
{
    public BufferedEvent(DateTimeOffset receivedUtc, long ingestionSequence, int byteCount, JsonDocument document)
    {
        ReceivedUtc = receivedUtc;
        IngestionSequence = ingestionSequence;
        ByteCount = byteCount;
        Document = document;
    }

    public DateTimeOffset ReceivedUtc { get; }
    public long IngestionSequence { get; }
    public int ByteCount { get; }
    public JsonDocument Document { get; }
    public void Dispose() => Document.Dispose();
}

public sealed class EventBuffer : IDisposable
{
    private readonly LinkedList<BufferedEvent> _events = new();
    private readonly int _maximumCount;
    private readonly long _maximumBytes;
    private readonly TimeSpan _retention;
    private long _bytes;

    public EventBuffer(int maximumCount, long maximumBytes, TimeSpan retention)
    {
        _maximumCount = maximumCount;
        _maximumBytes = maximumBytes;
        _retention = retention;
    }

    public void Add(BufferedEvent value)
    {
        lock (_events)
        {
            _events.AddLast(value);
            _bytes += value.ByteCount;
            DateTimeOffset oldest = value.ReceivedUtc - _retention;
            while (_events.First != null
                && (_events.Count > _maximumCount || _bytes > _maximumBytes || _events.First.Value.ReceivedUtc < oldest))
            {
                BufferedEvent removed = _events.First.Value;
                _events.RemoveFirst();
                _bytes -= removed.ByteCount;
                removed.Dispose();
            }
        }
    }

    public IReadOnlyList<JsonElement> Snapshot(
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        Func<JsonElement, bool>? predicate = null)
    {
        lock (_events)
        {
            return _events
                .Where(item => item.ReceivedUtc >= fromUtc && item.ReceivedUtc <= throughUtc)
                .Where(item => predicate == null || predicate(item.Document.RootElement))
                .Select(item => item.Document.RootElement.Clone())
                .ToArray();
        }
    }

    public void Dispose()
    {
        lock (_events)
        {
            foreach (BufferedEvent item in _events)
            {
                item.Dispose();
            }

            _events.Clear();
            _bytes = 0;
        }
    }
}
