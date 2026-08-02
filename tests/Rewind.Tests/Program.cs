using System.Text.Json;
using System.Buffers.Binary;
using System.IO.Pipes;
using Rewind.Agent.Core;
using Rewind.Abstractions;
using Rewind.Protocol;
using Rewind.Sdk;
using Rewind.Storage;

var failures = new List<string>();
Run("buffer remains count-bounded", BufferRemainsBounded);
await RunAsync("atomic writer publishes manifest last", AtomicWriterPublishesCompletePackage);
await RunAsync("ACC-001 agent absent keeps host responsive", AgentAbsentKeepsHostResponsive);
await RunAsync("event and trigger queues preserve client sequence", EventAndTriggerQueuesPreserveSequence);
await RunAsync("ACC-002 SDK reconnects when Agent starts later", SdkReconnectsWhenAgentStartsLater);
await RunAsync("ACC-003 trigger creates complete pre/post package", TriggerCreatesCompletePackage);
await RunAsync("ACC-006 malformed frame isolates offending client", MalformedFrameIsolatesClient);
await RunAsync("ACC-007 disk failure produces no complete package", DiskFailureProducesNoCompletePackage);
await RunAsync("ACC-010 concurrent clients receive deterministic ingestion order", ConcurrentClientsAreOrdered);
await RunAsync("ACC-004 repeated triggers merge within maximum duration", RepeatedTriggersMerge);
await RunAsync("staging recovery quarantines incomplete packages", StagingRecoveryQuarantinesIncompletePackages);
await RunAsync("storage quota retains bounded completed incidents", StorageQuotaIsBounded);
await RunAsync("flush reports unresolved items and later completion", FlushReportsOutcome);
await RunAsync("configuration loads relative paths and rejects unsafe bounds", ConfigurationIsValidated);
await RunAsync("level policy persists and automatically triggers incidents", LevelPolicyPersistsAndTriggers);
await RunAsync("ACC-005 queue overload remains bounded and reports loss", QueueOverloadIsReported);
await RunAsync("ACC-009 opaque Unicode payload round-trips unchanged", OpaquePayloadRoundTrips);

if (failures.Count > 0)
{
    foreach (string failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine("All executable verification checks passed.");
return 0;

void Run(string name, Action check)
{
    try
    {
        check();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {name}: {exception.Message}");
    }
}

async Task RunAsync(string name, Func<Task> check)
{
    try
    {
        await check();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {name}: {exception.Message}");
    }
}

void BufferRemainsBounded()
{
    using var buffer = new EventBuffer(2, 1024, TimeSpan.FromMinutes(1));
    for (int index = 0; index < 3; index++)
    {
        JsonDocument document = JsonDocument.Parse($"{{\"index\":{index}}}");
        buffer.Add(new BufferedEvent(DateTimeOffset.UtcNow, index, 16, document));
    }

    IReadOnlyList<JsonElement> snapshot = buffer.Snapshot(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    Assert(snapshot.Count == 2, "Expected oldest event eviction.");
    Assert(snapshot[0].GetProperty("index").GetInt32() == 1, "Unexpected retained event order.");
}

async Task AtomicWriterPublishesCompletePackage()
{
    string root = Path.Combine(Path.GetTempPath(), "rewind-tests-" + Guid.NewGuid().ToString("N"));
    try
    {
        using JsonDocument document = JsonDocument.Parse("{\"type\":\"Event\"}");
        Guid id = Guid.NewGuid();
        var writer = new AtomicIncidentWriter(root);
        string completed = await writer.WriteAsync(
            id,
            new[] { document.RootElement.Clone() },
            Array.Empty<JsonElement>(),
            new { accepted = 1 },
            CancellationToken.None);
        Assert(File.Exists(Path.Combine(completed, "manifest.json")), "Complete manifest is missing.");
        Assert(File.Exists(Path.Combine(completed, "events.jsonl")), "Events file is missing.");
        Assert(File.Exists(Path.Combine(completed, "triggers.json")), "Triggers file is missing.");
        Assert(File.Exists(Path.Combine(completed, "configuration.json")), "Configuration file is missing.");
        Assert(File.Exists(Path.Combine(completed, "recorder-health.json")), "Recorder health file is missing.");
        Assert(!Directory.Exists(Path.Combine(root, "incidents", ".staging", id.ToString("D"))), "Staging directory remains.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}

async Task AgentAbsentKeepsHostResponsive()
{
    string pipeName = UniquePipe();
    Assert(RewindRecorder.Initialize(new RewindOptions
    {
        AgentPipeName = pipeName,
        EventQueueCapacity = 8,
        ConnectTimeoutMilliseconds = 25,
    }), "Recorder did not initialize.");
    try
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int index = 0; index < 8; index++)
        {
            RewindRecorder.Information("Tests", "Absent", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        stopwatch.Stop();
        await Task.Delay(100);
        RewindHealthSnapshot health = RewindRecorder.GetHealthSnapshot();
        Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(1), "Caller path waited for an absent Agent.");
        Assert(health.Accepted == 8, "Expected bounded queue admission while Agent was absent.");
    }
    finally
    {
        RewindRecorder.Shutdown();
    }
}

async Task EventAndTriggerQueuesPreserveSequence()
{
    string pipeName = UniquePipe();
    Task<IReadOnlyList<JsonElement>> received = ReceiveFramesAsync(pipeName, 7, TimeSpan.FromSeconds(5));
    Assert(RewindRecorder.Initialize(new RewindOptions { AgentPipeName = pipeName }), "Recorder did not initialize.");
    try
    {
        for (int index = 0; index < 5; index++)
        {
            RewindRecorder.Debug("Tests", "OrderedEvent", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        RewindRecorder.TriggerIncident("OrderedTrigger", "after five events");
        IReadOnlyList<JsonElement> frames = await received;
        long[] sequences = frames.Skip(1).Select(frame => frame.GetProperty("clientSequence").GetInt64()).ToArray();
        Assert(sequences.SequenceEqual(sequences.OrderBy(value => value)), "Cross-queue client sequence was reordered.");
        Assert(frames[^1].GetProperty("type").GetString() == "Trigger", "Trigger overtook accepted events.");
    }
    finally
    {
        RewindRecorder.Shutdown();
    }
}

async Task SdkReconnectsWhenAgentStartsLater()
{
    string pipeName = UniquePipe();
    Assert(RewindRecorder.Initialize(new RewindOptions
    {
        AgentPipeName = pipeName,
        ConnectTimeoutMilliseconds = 25,
    }), "Recorder did not initialize.");
    try
    {
        RewindRecorder.Information("Tests", "BeforeAgent", "queued");
        await Task.Delay(100);
        Task<IReadOnlyList<JsonElement>> received = ReceiveFramesAsync(pipeName, 2, TimeSpan.FromSeconds(5));
        IReadOnlyList<JsonElement> frames = await received;
        Assert(frames[1].GetProperty("type").GetString() == "Event", "Queued event was not sent after reconnect.");
    }
    finally
    {
        RewindRecorder.Shutdown();
    }
}

async Task TriggerCreatesCompletePackage()
{
    string root = NewTemporaryRoot();
    string pipeName = UniquePipe();
    using var cancellation = new CancellationTokenSource();
    using var agent = RewindAgentFactory.Create(new AgentOptions
    {
        PipeName = pipeName,
        DataDirectory = root,
        PreTrigger = TimeSpan.FromSeconds(5),
        PostTrigger = TimeSpan.FromMilliseconds(150),
    });
    Task agentTask = agent.RunAsync(cancellation.Token);
    try
    {
        Assert(RewindRecorder.Initialize(new RewindOptions { AgentPipeName = pipeName }), "Recorder did not initialize.");
        RewindRecorder.Information("Tests", "Before", "pre");
        RewindRecorder.TriggerIncident("Manual", "acceptance");
        RewindRecorder.Information("Tests", "After", "post");
        string package = await WaitForCompletedPackageAsync(root, TimeSpan.FromSeconds(5));
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(package, "manifest.json")));
        Assert(manifest.RootElement.GetProperty("status").GetString() == "complete", "Manifest was not complete.");
        Assert(manifest.RootElement.GetProperty("eventCount").GetInt32() >= 2, "Pre/post events were not captured.");
        Assert(manifest.RootElement.GetProperty("triggerCount").GetInt32() == 1, "Trigger was not captured.");
    }
    finally
    {
        RewindRecorder.Shutdown();
        cancellation.Cancel();
        await IgnoreCancellationAsync(agentTask);
        DeleteTemporaryRoot(root);
    }
}

async Task MalformedFrameIsolatesClient()
{
    string root = NewTemporaryRoot();
    string pipeName = UniquePipe();
    using var cancellation = new CancellationTokenSource();
    using var agent = RewindAgentFactory.Create(new AgentOptions
    {
        PipeName = pipeName,
        DataDirectory = root,
        PostTrigger = TimeSpan.FromMilliseconds(100),
    });
    Task agentTask = agent.RunAsync(cancellation.Token);
    try
    {
        using (var malformed = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
        {
            await malformed.ConnectAsync(2000);
            byte[] invalidLength = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(invalidLength, ProtocolConstants.MaximumFrameBytes + 1);
            await malformed.WriteAsync(invalidLength);
        }

        await Task.Delay(100);
        Assert(RewindRecorder.Initialize(new RewindOptions { AgentPipeName = pipeName }), "Recorder did not initialize.");
        RewindRecorder.Information("Tests", "ValidAfterMalformed", "accepted");
        RewindRecorder.TriggerIncident("AfterMalformed", "client isolation");
        string package = await WaitForCompletedPackageAsync(root, TimeSpan.FromSeconds(5));
        Assert(File.Exists(Path.Combine(package, "manifest.json")), "Valid client did not complete after malformed client.");
    }
    finally
    {
        RewindRecorder.Shutdown();
        cancellation.Cancel();
        await IgnoreCancellationAsync(agentTask);
        DeleteTemporaryRoot(root);
    }
}

async Task DiskFailureProducesNoCompletePackage()
{
    string root = NewTemporaryRoot();
    Directory.CreateDirectory(root);
    string blockingFile = Path.Combine(root, "not-a-directory");
    await File.WriteAllTextAsync(blockingFile, "block");
    var writer = new AtomicIncidentWriter(blockingFile);
    bool failed = false;
    try
    {
        await writer.WriteAsync(
            Guid.NewGuid(),
            Array.Empty<JsonElement>(),
            Array.Empty<JsonElement>(),
            new { accepted = 0 },
            CancellationToken.None);
    }
    catch (IOException)
    {
        failed = true;
    }
    catch (UnauthorizedAccessException)
    {
        failed = true;
    }
    finally
    {
        Assert(failed, "Injected storage failure unexpectedly succeeded.");
        Assert(!Directory.Exists(Path.Combine(blockingFile, "incidents")), "A complete incident path was exposed.");
        DeleteTemporaryRoot(root);
    }
}

async Task ConcurrentClientsAreOrdered()
{
    string root = NewTemporaryRoot();
    string pipeName = UniquePipe();
    using var cancellation = new CancellationTokenSource();
    using var agent = RewindAgentFactory.Create(new AgentOptions
    {
        PipeName = pipeName,
        DataDirectory = root,
        MaximumConcurrentClients = 4,
        PostTrigger = TimeSpan.FromMilliseconds(100),
    });
    Task agentTask = agent.RunAsync(cancellation.Token);
    try
    {
        Task[] clients = Enumerable.Range(0, 4)
            .Select(index => SendRawEventAsync(pipeName, index))
            .ToArray();
        await Task.WhenAll(clients);
        await SendRawTriggerAsync(pipeName, "MultiClient");
        string package = await WaitForCompletedPackageAsync(root, TimeSpan.FromSeconds(5));
        JsonElement[] events = (await File.ReadAllLinesAsync(Path.Combine(package, "events.jsonl")))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Assert(events.Length == 4, "Not every concurrent client event was captured.");
        long[] ingestion = events.Select(item => item.GetProperty("ingestionSequence").GetInt64()).ToArray();
        Assert(ingestion.SequenceEqual(ingestion.OrderBy(value => value)), "Package ingestion order is not deterministic.");
    }
    finally
    {
        cancellation.Cancel();
        await IgnoreCancellationAsync(agentTask);
        DeleteTemporaryRoot(root);
    }
}

async Task RepeatedTriggersMerge()
{
    string root = NewTemporaryRoot();
    string pipeName = UniquePipe();
    using var cancellation = new CancellationTokenSource();
    using var agent = RewindAgentFactory.Create(new AgentOptions
    {
        PipeName = pipeName,
        DataDirectory = root,
        PostTrigger = TimeSpan.FromMilliseconds(150),
        MaximumCapture = TimeSpan.FromSeconds(1),
        MergeTriggers = true,
    });
    Task agentTask = agent.RunAsync(cancellation.Token);
    try
    {
        await SendRawTriggerAsync(pipeName, "First");
        await Task.Delay(75);
        await SendRawTriggerAsync(pipeName, "Second");
        string package = await WaitForCompletedPackageAsync(root, TimeSpan.FromSeconds(5));
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(package, "manifest.json")));
        Assert(manifest.RootElement.GetProperty("triggerCount").GetInt32() == 2, "Merged capture did not retain both triggers.");
    }
    finally
    {
        cancellation.Cancel();
        await IgnoreCancellationAsync(agentTask);
        DeleteTemporaryRoot(root);
    }
}

async Task StagingRecoveryQuarantinesIncompletePackages()
{
    string root = NewTemporaryRoot();
    string staging = Path.Combine(root, "incidents", ".staging", "incomplete");
    Directory.CreateDirectory(staging);
    await File.WriteAllTextAsync(Path.Combine(staging, "events.jsonl"), "partial");
    using var agent = RewindAgentFactory.Create(new AgentOptions { DataDirectory = root });
    string quarantine = Path.Combine(root, "incidents", ".quarantine");
    Assert(!Directory.Exists(staging), "Incomplete staging directory was left active.");
    Assert(Directory.EnumerateDirectories(quarantine).Count() == 1, "Incomplete staging directory was not quarantined.");
    DeleteTemporaryRoot(root);
}

async Task StorageQuotaIsBounded()
{
    string root = NewTemporaryRoot();
    var writer = new AtomicIncidentWriter(root, maximumIncidentCount: 2, maximumStorageBytes: 1024 * 1024);
    for (int index = 0; index < 3; index++)
    {
        await writer.WriteAsync(
            Guid.NewGuid(),
            Array.Empty<JsonElement>(),
            Array.Empty<JsonElement>(),
            new { index },
            CancellationToken.None);
        await Task.Delay(10);
    }

    string incidents = Path.Combine(root, "incidents");
    int completed = Directory.EnumerateDirectories(incidents)
        .Count(path => Path.GetFileName(path) is not ".staging" and not ".quarantine");
    Assert(completed == 2, "Completed incident count exceeded its configured quota.");
    DeleteTemporaryRoot(root);
}

async Task FlushReportsOutcome()
{
    string pipeName = UniquePipe();
    Assert(RewindRecorder.Initialize(new RewindOptions
    {
        AgentPipeName = pipeName,
        ConnectTimeoutMilliseconds = 25,
    }), "Recorder did not initialize.");
    try
    {
        RewindRecorder.Information("Tests", "Flush", "pending");
        FlushResult absent = await RewindRecorder.FlushAsync(TimeSpan.FromMilliseconds(50));
        Assert(!absent.Completed && absent.UnresolvedCount == 1, "Absent-Agent flush did not expose its unresolved item.");
        Task<IReadOnlyList<JsonElement>> received = ReceiveFramesAsync(pipeName, 2, TimeSpan.FromSeconds(5));
        _ = await received;
        FlushResult connected = await RewindRecorder.FlushAsync(TimeSpan.FromSeconds(1));
        Assert(connected.Completed && connected.UnresolvedCount == 0, "Connected flush did not resolve the queued item.");
    }
    finally
    {
        await RewindRecorder.ShutdownAsync(TimeSpan.FromSeconds(1));
    }
}

async Task ConfigurationIsValidated()
{
    string root = NewTemporaryRoot();
    Directory.CreateDirectory(root);
    string path = Path.Combine(root, "rewind-agent.json");
    await File.WriteAllTextAsync(
        path,
        """
        {
          "agent": { "pipeName": "Configured", "dataDirectory": "./capture", "maximumConcurrentClients": 2 },
          "buffer": { "retentionSeconds": 20, "maximumEventCount": 10, "maximumBytes": 4096 },
          "capture": { "preTriggerSeconds": 10, "postTriggerSeconds": 1, "maximumCaptureSeconds": 5 }
        }
        """);
    AgentOptions loaded = AgentConfiguration.Load(path);
    Assert(loaded.PipeName == "Configured", "Configured pipe name was not loaded.");
    Assert(loaded.DataDirectory == Path.Combine(root, "capture"), "Relative data path was not anchored to the config file.");

    await File.WriteAllTextAsync(
        path,
        """
        {
          "buffer": { "retentionSeconds": 5 },
          "capture": { "preTriggerSeconds": 10 }
        }
        """);
    bool rejected = false;
    try
    {
        _ = AgentConfiguration.Load(path);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }

    Assert(rejected, "Conflicting capture/retention bounds were accepted.");

    await File.WriteAllTextAsync(path, """{ "agent": { "pipeNmae": "misspelled" } }""");
    rejected = false;
    try
    {
        _ = AgentConfiguration.Load(path);
    }
    catch (JsonException)
    {
        rejected = true;
    }

    Assert(rejected, "Unknown configuration property was silently accepted.");
    DeleteTemporaryRoot(root);
}

async Task LevelPolicyPersistsAndTriggers()
{
    string root = NewTemporaryRoot();
    string pipeName = UniquePipe();
    using var cancellation = new CancellationTokenSource();
    using var agent = RewindAgentFactory.Create(new AgentOptions
    {
        PipeName = pipeName,
        DataDirectory = root,
        PostTrigger = TimeSpan.FromMilliseconds(100),
        MaximumCapture = TimeSpan.FromSeconds(1),
    });
    Task agentTask = agent.RunAsync(cancellation.Token);
    try
    {
        await SendRawEventAsync(pipeName, 1, RewindLevel.Information);
        await SendRawEventAsync(pipeName, 2, RewindLevel.Warning);
        await SendRawEventAsync(pipeName, 3, RewindLevel.Error);

        string package = await WaitForCompletedPackageAsync(root, TimeSpan.FromSeconds(5));
        string[] incidentEvents = await File.ReadAllLinesAsync(Path.Combine(package, "events.jsonl"));
        Assert(incidentEvents.Length == 3, "Incident policy did not include all configured levels.");

        string logs = Path.Combine(root, "logs");
        string[] continuousEvents = Directory.EnumerateFiles(logs, "*.jsonl")
            .SelectMany(File.ReadAllLines)
            .ToArray();
        Assert(continuousEvents.Length == 2, "Continuous policy did not retain exactly Warning and Error.");
        Assert(
            continuousEvents.Any(line => line.Contains("\"level\":\"Warning\"", StringComparison.Ordinal)),
            "Warning was not continuously persisted.");
        Assert(
            continuousEvents.Any(line => line.Contains("\"level\":\"Error\"", StringComparison.Ordinal)),
            "Error was not continuously persisted.");
    }
    finally
    {
        cancellation.Cancel();
        await IgnoreCancellationAsync(agentTask);
        DeleteTemporaryRoot(root);
    }
}

async Task QueueOverloadIsReported()
{
    Assert(RewindRecorder.Initialize(new RewindOptions
    {
        AgentPipeName = UniquePipe(),
        EventQueueCapacity = 1,
        ConnectTimeoutMilliseconds = 25,
    }), "Recorder did not initialize.");
    try
    {
        RewindRecorder.Information("Tests", "First", "accepted");
        RewindRecorder.Information("Tests", "Second", "must be dropped");
        RewindHealthSnapshot health = RewindRecorder.GetHealthSnapshot();
        Assert(health.Accepted == 1, "Queue accepted more events than its configured capacity.");
        Assert(health.DroppedQueueFull == 1, "Queue-full loss was not counted.");
        Assert(health.Pending == 1, "Pending count exceeded or missed the bounded item.");
    }
    finally
    {
        RewindRecorder.Shutdown();
    }
}

async Task OpaquePayloadRoundTrips()
{
    const string message = "ไทย \"quoted\" \\\\ newline\\n emoji 😀 null:\\u0000";
    string root = NewTemporaryRoot();
    string pipeName = UniquePipe();
    using var cancellation = new CancellationTokenSource();
    using var agent = RewindAgentFactory.Create(new AgentOptions
    {
        PipeName = pipeName,
        DataDirectory = root,
        PostTrigger = TimeSpan.FromMilliseconds(100),
    });
    Task agentTask = agent.RunAsync(cancellation.Token);
    try
    {
        Assert(RewindRecorder.Initialize(new RewindOptions { AgentPipeName = pipeName }), "Recorder did not initialize.");
        RewindRecorder.Information("Tests", "Opaque", message);
        RewindRecorder.TriggerIncident("OpaqueRoundTrip", "test");
        string package = await WaitForCompletedPackageAsync(root, TimeSpan.FromSeconds(5));
        string line = (await File.ReadAllLinesAsync(Path.Combine(package, "events.jsonl"))).Single();
        using JsonDocument document = JsonDocument.Parse(line);
        string actual = document.RootElement.GetProperty("payload").GetProperty("message").GetString()!;
        Assert(actual == message, "Opaque payload changed during transport or persistence.");
    }
    finally
    {
        RewindRecorder.Shutdown();
        cancellation.Cancel();
        await IgnoreCancellationAsync(agentTask);
        DeleteTemporaryRoot(root);
    }
}

static async Task<IReadOnlyList<JsonElement>> ReceiveFramesAsync(string pipeName, int count, TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    await using var pipe = new NamedPipeServerStream(
        pipeName,
        PipeDirection.In,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);
    await pipe.WaitForConnectionAsync(cancellation.Token);
    var frames = new List<JsonElement>(count);
    for (int index = 0; index < count; index++)
    {
        byte[] lengthBytes = await ReadExactAsync(pipe, sizeof(int), cancellation.Token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        byte[] payload = await ReadExactAsync(pipe, length, cancellation.Token);
        using JsonDocument document = JsonDocument.Parse(payload);
        frames.Add(document.RootElement.Clone());
    }

    return frames;
}

static async Task SendRawEventAsync(
    string pipeName,
    int index,
    RewindLevel level = RewindLevel.Information)
{
    Guid clientId = Guid.NewGuid();
    var value = new RewindEvent(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        1,
        level,
        "ConcurrentClient",
        "Event",
        index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        new Dictionary<string, string>(),
        Environment.ProcessId,
        Environment.CurrentManagedThreadId);
    byte[] payload = WireJson.SerializeEvent(
        new WireMessage
        {
            Type = WireMessageType.Event,
            ClientInstanceId = clientId,
            MessageId = Guid.NewGuid(),
            ClientSequence = 1,
        },
        value);
    await SendFramesAsync(pipeName, payload);
}

static async Task SendRawTriggerAsync(string pipeName, string name)
{
    byte[] payload = WireJson.SerializeTrigger(
        new WireMessage
        {
            Type = WireMessageType.Trigger,
            ClientInstanceId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            ClientSequence = 1,
        },
        name,
        "test");
    await SendFramesAsync(pipeName, payload);
}

static async Task SendFramesAsync(string pipeName, params byte[][] payloads)
{
    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(2000);
    foreach (byte[] payload in payloads)
    {
        byte[] length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await pipe.WriteAsync(length);
        await pipe.WriteAsync(payload);
    }

    await pipe.FlushAsync();
}

static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
{
    var buffer = new byte[length];
    int offset = 0;
    while (offset < length)
    {
        int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException();
        }

        offset += read;
    }

    return buffer;
}

static async Task<string> WaitForCompletedPackageAsync(string root, TimeSpan timeout)
{
    DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
    string incidents = Path.Combine(root, "incidents");
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (Directory.Exists(incidents))
        {
            string? package = Directory.EnumerateDirectories(incidents)
                .FirstOrDefault(path => Path.GetFileName(path) != ".staging"
                    && File.Exists(Path.Combine(path, "manifest.json")));
            if (package != null)
            {
                return package;
            }
        }

        await Task.Delay(25);
    }

    throw new TimeoutException("A completed incident package was not produced.");
}

static async Task IgnoreCancellationAsync(Task task)
{
    try
    {
        await task;
    }
    catch (OperationCanceledException)
    {
    }
}

static string UniquePipe() => "Rewind.Tests." + Guid.NewGuid().ToString("N");
static string NewTemporaryRoot() => Path.Combine(Path.GetTempPath(), "rewind-tests-" + Guid.NewGuid().ToString("N"));
static void DeleteTemporaryRoot(string root)
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, true);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
