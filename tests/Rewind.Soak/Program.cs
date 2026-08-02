using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Rewind.Agent.Core;
using Rewind.Sdk;
using Rewind.Storage;

int durationSeconds = args.Length >= 1
    && int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedDuration)
    && parsedDuration > 0
        ? parsedDuration
        : 60;
int ratePerSecond = args.Length >= 2
    && int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRate)
    && parsedRate > 0
        ? parsedRate
        : 100;

string pipeName = "Rewind.Soak." + Guid.NewGuid().ToString("N");
string root = Path.Combine(Path.GetTempPath(), "rewind-soak-" + Guid.NewGuid().ToString("N"));
using var cancellation = new CancellationTokenSource();
using var agent = RewindAgentFactory.Create(new AgentOptions
{
    PipeName = pipeName,
    DataDirectory = root,
    Retention = TimeSpan.FromSeconds(10),
    PreTrigger = TimeSpan.FromSeconds(5),
    PostTrigger = TimeSpan.FromMilliseconds(250),
    MaximumCapture = TimeSpan.FromSeconds(2),
    MaximumEventCount = Math.Max(10_000, ratePerSecond * 20),
    MaximumBufferBytes = 32 * 1024 * 1024,
    MaximumStoredIncidents = 20,
});
Task agentTask = agent.RunAsync(cancellation.Token);
RewindRecorder.Initialize(new RewindOptions
{
    AgentPipeName = pipeName,
    EventQueueCapacity = Math.Max(4096, ratePerSecond * 2),
});

var process = Process.GetCurrentProcess();
long initialWorkingSet = process.WorkingSet64;
long emitted = 0;
var elapsed = Stopwatch.StartNew();
long nextTrigger = ratePerSecond * 2L;
try
{
    TimeSpan interval = TimeSpan.FromSeconds(1d / ratePerSecond);
    while (elapsed.Elapsed < TimeSpan.FromSeconds(durationSeconds))
    {
        RewindRecorder.Debug("Soak", "Event", "representative constant payload");
        emitted++;
        if (emitted >= nextTrigger)
        {
            RewindRecorder.TriggerIncident("PeriodicSoakTrigger", emitted.ToString(CultureInfo.InvariantCulture));
            nextTrigger += ratePerSecond * 2L;
        }

        await Task.Delay(interval);
    }

    FlushResult flush = await RewindRecorder.FlushAsync(TimeSpan.FromSeconds(5));
    RewindHealthSnapshot health = RewindRecorder.GetHealthSnapshot();
    process.Refresh();
    long finalWorkingSet = process.WorkingSet64;
    var report = new
    {
        schemaVersion = 1,
        durationSeconds,
        targetRatePerSecond = ratePerSecond,
        emitted,
        health.Accepted,
        health.Sent,
        health.DroppedQueueFull,
        health.DroppedInvalid,
        health.TransportFailures,
        flush.Completed,
        flush.UnresolvedCount,
        initialWorkingSet,
        finalWorkingSet,
        workingSetGrowthBytes = finalWorkingSet - initialWorkingSet,
    };
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return flush.Completed && health.DroppedQueueFull == 0 ? 0 : 1;
}
finally
{
    await RewindRecorder.ShutdownAsync(TimeSpan.FromSeconds(1));
    cancellation.Cancel();
    try
    {
        await agentTask;
    }
    catch (OperationCanceledException)
    {
    }

    if (Directory.Exists(root))
    {
        Directory.Delete(root, true);
    }
}
