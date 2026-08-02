namespace Rewind.Sdk;

public sealed class RewindOptions
{
    public string AgentPipeName { get; set; } = "Rewind.Agent";
    public int EventQueueCapacity { get; set; } = 4096;
    public int ControlQueueCapacity { get; set; } = 64;
    public int ConnectTimeoutMilliseconds { get; set; } = 250;
    public int MaximumContextEntries { get; set; } = 64;
}
