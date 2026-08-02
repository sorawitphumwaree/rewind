namespace Rewind.Sdk;

public sealed class RewindHealthSnapshot
{
    public RewindHealthSnapshot(
        long accepted,
        long sent,
        long droppedQueueFull,
        long droppedInvalid,
        long transportFailures,
        long pending)
    {
        Accepted = accepted;
        Sent = sent;
        DroppedQueueFull = droppedQueueFull;
        DroppedInvalid = droppedInvalid;
        TransportFailures = transportFailures;
        Pending = pending;
    }

    public long Accepted { get; }
    public long Sent { get; }
    public long DroppedQueueFull { get; }
    public long DroppedInvalid { get; }
    public long TransportFailures { get; }
    public long Pending { get; }
}
