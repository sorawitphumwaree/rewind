using System.Text.Json;

namespace Rewind.Agent.Core;

public interface IIncidentWriter
{
    long QuarantineIncompleteStaging();

    Task<string> WriteAsync(
        Guid incidentId,
        IReadOnlyList<JsonElement> events,
        IReadOnlyList<JsonElement> triggers,
        object health,
        CancellationToken cancellationToken,
        object? configuration = null,
        DateTimeOffset? captureStartedUtc = null,
        DateTimeOffset? captureEndedUtc = null);
}

public interface IContinuousLogWriter
{
    void Append(JsonElement value);
}
