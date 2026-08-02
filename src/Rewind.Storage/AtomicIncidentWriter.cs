using System.Text.Json;
using Rewind.Agent.Core;

namespace Rewind.Storage;

public sealed class AtomicIncidentWriter : IIncidentWriter
{
    private readonly string _root;
    private readonly int _maximumIncidentCount;
    private readonly long _maximumStorageBytes;

    public AtomicIncidentWriter(
        string root,
        int maximumIncidentCount = 100,
        long maximumStorageBytes = 5L * 1024 * 1024 * 1024)
    {
        _root = Path.GetFullPath(root);
        _maximumIncidentCount = maximumIncidentCount > 0
            ? maximumIncidentCount
            : throw new ArgumentOutOfRangeException(nameof(maximumIncidentCount));
        _maximumStorageBytes = maximumStorageBytes > 0
            ? maximumStorageBytes
            : throw new ArgumentOutOfRangeException(nameof(maximumStorageBytes));
    }

    public async Task<string> WriteAsync(
        Guid incidentId,
        IReadOnlyList<JsonElement> events,
        IReadOnlyList<JsonElement> triggers,
        object health,
        CancellationToken cancellationToken,
        object? configuration = null,
        DateTimeOffset? captureStartedUtc = null,
        DateTimeOffset? captureEndedUtc = null)
    {
        string incidents = Path.Combine(_root, "incidents");
        string stagingRoot = Path.Combine(incidents, ".staging");
        string staging = Path.Combine(stagingRoot, incidentId.ToString("D"));
        string completed = Path.Combine(incidents, incidentId.ToString("D"));
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(staging);

        try
        {
            await WriteJsonLinesAsync(Path.Combine(staging, "events.jsonl"), events, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(staging, "triggers.json"), triggers, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(staging, "recorder-health.json"), health, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(
                Path.Combine(staging, "configuration.json"),
                configuration ?? new { },
                cancellationToken).ConfigureAwait(false);
            string[] clients = events
                .Select(item => item.TryGetProperty("clientInstanceId", out JsonElement client)
                    ? client.GetString()
                    : null)
                .Where(value => value != null)
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var manifest = new
            {
                schemaVersion = "1.0.0",
                protocolVersion = 1,
                status = "complete",
                incidentId,
                completedUtc = DateTimeOffset.UtcNow,
                captureStartedUtc,
                captureEndedUtc,
                eventCount = events.Count,
                triggerCount = triggers.Count,
                clients,
                knownLosses = health,
                truncationObserved = false,
                clockDiscontinuities = 0,
            };
            await WriteJsonAsync(Path.Combine(staging, "manifest.json"), manifest, cancellationToken).ConfigureAwait(false);
            Directory.Move(staging, completed);
            EnforceRetention(completed);
            return completed;
        }
        catch
        {
            // Staging intentionally remains incomplete. It is never surfaced as a completed incident.
            throw;
        }
    }

    public long QuarantineIncompleteStaging()
    {
        string stagingRoot = Path.Combine(_root, "incidents", ".staging");
        if (!Directory.Exists(stagingRoot))
        {
            return 0;
        }

        string quarantineRoot = Path.Combine(_root, "incidents", ".quarantine");
        Directory.CreateDirectory(quarantineRoot);
        long count = 0;
        foreach (string staging in Directory.EnumerateDirectories(stagingRoot))
        {
            string name = Path.GetFileName(staging);
            string destination = Path.Combine(
                quarantineRoot,
                DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture)
                    + "-"
                    + name);
            Directory.Move(staging, destination);
            count++;
        }

        return count;
    }

    private void EnforceRetention(string newest)
    {
        string incidents = Path.Combine(_root, "incidents");
        var completed = Directory.EnumerateDirectories(incidents)
            .Where(path => Path.GetFileName(path) is not ".staging" and not ".quarantine")
            .Where(path => File.Exists(Path.Combine(path, "manifest.json")))
            .Select(path => new IncidentDirectory(
                path,
                Directory.GetCreationTimeUtc(path),
                Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(file => new FileInfo(file).Length)))
            .OrderBy(item => item.CreatedUtc)
            .ToList();
        long totalBytes = completed.Sum(item => item.Bytes);
        int retainedCount = completed.Count;
        foreach (IncidentDirectory item in completed)
        {
            if (retainedCount <= _maximumIncidentCount && totalBytes <= _maximumStorageBytes)
            {
                break;
            }

            if (string.Equals(item.Path, newest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.Delete(item.Path, true);
            totalBytes -= item.Bytes;
            retainedCount--;
        }
    }

    private static async Task WriteJsonLinesAsync(
        string path,
        IReadOnlyList<JsonElement> values,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await using var writer = new StreamWriter(stream);
        foreach (JsonElement value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(value.GetRawText()).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    private sealed record IncidentDirectory(string Path, DateTime CreatedUtc, long Bytes);
}
