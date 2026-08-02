using System;

namespace Rewind.Protocol;

public enum WireMessageType
{
    Hello,
    Event,
    Trigger,
    Goodbye,
}

public sealed class WireMessage
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.MajorVersion;
    public WireMessageType Type { get; set; }
    public Guid ClientInstanceId { get; set; }
    public Guid MessageId { get; set; }
    public long ClientSequence { get; set; }
    public string Payload { get; set; } = string.Empty;
}
