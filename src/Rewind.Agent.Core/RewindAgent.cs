using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Rewind.Abstractions;
using Rewind.Protocol;

namespace Rewind.Agent.Core;

public sealed class RewindAgent : IDisposable
{
    private readonly AgentOptions _options;
    private readonly EventBuffer _buffer;
    private readonly IIncidentWriter _writer;
    private readonly IContinuousLogWriter _continuousWriter;
    private readonly object _ingestionGate = new();
    private readonly object _captureGate = new();
    private Capture? _capture;
    private long _ingestionSequence;
    private long _accepted;
    private long _rejected;
    private long _storageFailures;
    private long _activeClients;
    private long _peakClients;
    private readonly long _quarantinedStagingDirectories;

    public RewindAgent(
        AgentOptions options,
        IIncidentWriter writer,
        IContinuousLogWriter continuousWriter)
    {
        _options = options;
        _buffer = new EventBuffer(options.MaximumEventCount, options.MaximumBufferBytes, options.Retention);
        _writer = writer;
        _continuousWriter = continuousWriter;
        _quarantinedStagingDirectories = _writer.QuarantineIncompleteStaging();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var slots = new SemaphoreSlim(_options.MaximumConcurrentClients, _options.MaximumConcurrentClients);
        var sessions = new ConcurrentDictionary<long, Task>();
        long sessionId = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                var pipe = new NamedPipeServerStream(
                    _options.PipeName,
                    PipeDirection.In,
                    _options.MaximumConcurrentClients,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    pipe.Dispose();
                    slots.Release();
                    throw;
                }

                long id = Interlocked.Increment(ref sessionId);
                Task task = HandleClientAsync(pipe, slots, cancellationToken);
                sessions[id] = task;
                _ = task.ContinueWith(
                    completedTask =>
                    {
                        _ = completedTask.Exception;
                        _ = sessions.TryRemove(id, out Task? _);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        await Task.WhenAll(sessions.Values).ConfigureAwait(false);
    }

    public void Dispose() => _buffer.Dispose();

    public AgentHealthSnapshot GetHealthSnapshot() => new(
        Interlocked.Read(ref _accepted),
        Interlocked.Read(ref _rejected),
        Interlocked.Read(ref _storageFailures),
        Interlocked.Read(ref _activeClients),
        Interlocked.Read(ref _peakClients),
        _quarantinedStagingDirectories,
        Interlocked.Read(ref _ingestionSequence));

    private async Task ReadClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await ReadExactAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length <= 0 || length > ProtocolConstants.MaximumFrameBytes)
            {
                throw new InvalidDataException("Frame length is outside the allowed range.");
            }

            var payload = GC.AllocateUninitializedArray<byte>(length);
            if (!await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            {
                throw new EndOfStreamException("Frame ended before its declared length.");
            }

            lock (_ingestionGate)
            {
                ProcessFrame(payload);
            }
            await FinalizeCaptureIfDueAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        long active = Interlocked.Increment(ref _activeClients);
        UpdatePeak(active);
        try
        {
            await using (pipe.ConfigureAwait(false))
            {
                await ReadClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            Interlocked.Increment(ref _rejected);
        }
        finally
        {
            Interlocked.Decrement(ref _activeClients);
            slots.Release();
        }
    }

    private void ProcessFrame(byte[] bytes)
    {
        JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        int protocolVersion = root.GetProperty("protocolVersion").GetInt32();
        if (protocolVersion != ProtocolConstants.MajorVersion)
        {
            document.Dispose();
            throw new InvalidDataException("Unsupported protocol major version.");
        }

        string type = root.GetProperty("type").GetString() ?? string.Empty;
        switch (type)
        {
            case "Hello":
                document.Dispose();
                break;
            case "Event":
                long sequence = Interlocked.Increment(ref _ingestionSequence);
                DateTimeOffset receivedUtc = DateTimeOffset.UtcNow;
                JsonDocument enriched = AddIngestionMetadata(document, receivedUtc, sequence);
                document.Dispose();
                RewindLevel level = ReadLevel(enriched.RootElement);
                LevelPolicy policy = _options.PolicyFor(level);
                if (policy.PersistContinuously)
                {
                    try
                    {
                        _continuousWriter.Append(enriched.RootElement);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Interlocked.Increment(ref _storageFailures);
                    }
                }

                if (policy.Buffer)
                {
                    _buffer.Add(new BufferedEvent(receivedUtc, sequence, bytes.Length, enriched));
                }
                else
                {
                    enriched.Dispose();
                }

                Interlocked.Increment(ref _accepted);
                if (policy.TriggerIncident)
                {
                    StartCapture(CreateAutomaticTrigger(level, receivedUtc, sequence));
                }

                break;
            case "Trigger":
                JsonElement trigger = root.Clone();
                document.Dispose();
                StartCapture(trigger);
                Interlocked.Increment(ref _accepted);
                break;
            default:
                document.Dispose();
                throw new InvalidDataException("Unsupported message type.");
        }
    }

    private void StartCapture(JsonElement trigger)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool schedule = false;
        lock (_captureGate)
        {
            if (_capture == null)
            {
                _capture = new Capture(Guid.NewGuid(), now, now + _options.PostTrigger);
                schedule = true;
            }
            else if (_options.MergeTriggers)
            {
                DateTimeOffset maximum = _capture.StartedUtc + _options.MaximumCapture;
                DateTimeOffset extended = now + _options.PostTrigger;
                _capture.DeadlineUtc = extended < maximum ? extended : maximum;
                schedule = true;
            }

            if (_capture.Triggers.Count < _options.MaximumTriggersPerCapture)
            {
                _capture.Triggers.Add(trigger);
            }
            else
            {
                Interlocked.Increment(ref _rejected);
            }
        }

        if (schedule)
        {
            _ = ScheduleFinalizationAsync();
        }
    }

    private async Task ScheduleFinalizationAsync()
    {
        while (true)
        {
            TimeSpan remaining;
            lock (_captureGate)
            {
                if (_capture == null)
                {
                    return;
                }

                remaining = _capture.DeadlineUtc - DateTimeOffset.UtcNow;
            }

            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining).ConfigureAwait(false);
            }

            await FinalizeCaptureIfDueAsync(CancellationToken.None).ConfigureAwait(false);
            lock (_captureGate)
            {
                if (_capture == null)
                {
                    return;
                }
            }
        }
    }

    private async Task FinalizeCaptureIfDueAsync(CancellationToken cancellationToken)
    {
        Capture? capture;
        lock (_captureGate)
        {
            if (_capture == null || DateTimeOffset.UtcNow < _capture.DeadlineUtc)
            {
                return;
            }

            capture = _capture;
            _capture = null;
        }

        IReadOnlyList<JsonElement> events = _buffer.Snapshot(
            capture.StartedUtc - _options.PreTrigger,
            capture.DeadlineUtc,
            IncludeInIncident);
        try
        {
            await _writer.WriteAsync(
                capture.Id,
                events,
                capture.Triggers,
                new
                {
                    accepted = Interlocked.Read(ref _accepted),
                    rejected = Interlocked.Read(ref _rejected),
                    storageFailures = Interlocked.Read(ref _storageFailures),
                    activeClients = Interlocked.Read(ref _activeClients),
                    peakClients = Interlocked.Read(ref _peakClients),
                    quarantinedStagingDirectories = _quarantinedStagingDirectories,
                },
                cancellationToken,
                new
                {
                    _options.PipeName,
                    dataDirectory = Path.GetFullPath(_options.DataDirectory),
                    _options.MaximumConcurrentClients,
                    retentionSeconds = _options.Retention.TotalSeconds,
                    _options.MaximumEventCount,
                    _options.MaximumBufferBytes,
                    preTriggerSeconds = _options.PreTrigger.TotalSeconds,
                    postTriggerSeconds = _options.PostTrigger.TotalSeconds,
                    maximumCaptureSeconds = _options.MaximumCapture.TotalSeconds,
                    _options.MergeTriggers,
                    _options.MaximumTriggersPerCapture,
                    _options.MaximumStoredIncidents,
                    _options.MaximumStorageBytes,
                    levels = _options.LevelPolicies.ToDictionary(
                        item => item.Key.ToString(),
                        item => new
                        {
                            item.Value.Buffer,
                            item.Value.PersistContinuously,
                            item.Value.TriggerIncident,
                            item.Value.IncludeInIncident,
                        }),
                    continuousLog = new
                    {
                        _options.ContinuousLog.DirectoryName,
                        _options.ContinuousLog.MaximumFileBytes,
                        _options.ContinuousLog.MaximumTotalBytes,
                        _options.ContinuousLog.MaximumFileCount,
                    },
                },
                capture.StartedUtc,
                capture.DeadlineUtc).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Interlocked.Increment(ref _storageFailures);
        }
    }

    private bool IncludeInIncident(JsonElement value)
        => _options.PolicyFor(ReadLevel(value)).IncludeInIncident;

    private static RewindLevel ReadLevel(JsonElement value)
    {
        string text = value.GetProperty("payload").GetProperty("level").GetString()
            ?? throw new InvalidDataException("Event level is missing.");
        return Enum.TryParse(text, ignoreCase: true, out RewindLevel level)
            ? level
            : throw new InvalidDataException("Event level is invalid.");
    }

    private static JsonElement CreateAutomaticTrigger(
        RewindLevel level,
        DateTimeOffset receivedUtc,
        long ingestionSequence)
        => JsonSerializer.SerializeToElement(new
        {
            type = "AutomaticLevelTrigger",
            level = level.ToString(),
            receivedUtc,
            ingestionSequence,
        });

    private void UpdatePeak(long active)
    {
        while (true)
        {
            long peak = Interlocked.Read(ref _peakClients);
            if (active <= peak || Interlocked.CompareExchange(ref _peakClients, active, peak) == peak)
            {
                return;
            }
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? false : throw new EndOfStreamException();
            }

            offset += read;
        }

        return true;
    }

    private static JsonDocument AddIngestionMetadata(
        JsonDocument source,
        DateTimeOffset receivedUtc,
        long ingestionSequence)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in source.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteString("agentReceivedUtc", receivedUtc);
            writer.WriteNumber("ingestionSequence", ingestionSequence);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray());
    }

    private sealed class Capture
    {
        public Capture(Guid id, DateTimeOffset startedUtc, DateTimeOffset deadlineUtc)
        {
            Id = id;
            StartedUtc = startedUtc;
            DeadlineUtc = deadlineUtc;
        }

        public Guid Id { get; }
        public DateTimeOffset StartedUtc { get; }
        public DateTimeOffset DeadlineUtc { get; set; }
        public List<JsonElement> Triggers { get; } = new();
    }
}
