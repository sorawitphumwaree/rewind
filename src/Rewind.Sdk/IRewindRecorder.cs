using System;
using System.Threading.Tasks;
using Rewind.Abstractions;

namespace Rewind.Sdk;

public interface IRewindRecorder : IDisposable
{
    void SetContext(string key, string value);
    bool RemoveContext(string key);
    void ClearContext();
    void Write(RewindLevel level, string source, string name, string message);
    void Trigger(string name, string details);
    RewindHealthSnapshot GetHealth();
    Task<FlushResult> FlushAsync(TimeSpan timeout);
}

public enum InitializationStatus
{
    Initialized,
    AlreadyInitialized,
}

public sealed class InitializationResult
{
    public InitializationResult(InitializationStatus status)
    {
        Status = status;
    }

    public InitializationStatus Status { get; }
    public bool Initialized => Status == InitializationStatus.Initialized;
    public static implicit operator bool(InitializationResult value) => value.Initialized;
}
