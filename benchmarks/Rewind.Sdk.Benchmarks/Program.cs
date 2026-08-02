using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Rewind.Sdk;

int iterations = args.Length == 1
    && int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
    && parsed > 0
        ? parsed
        : 10_000;

RewindRecorder.Initialize(new RewindOptions
{
    AgentPipeName = "Rewind.Benchmark.Absent." + Guid.NewGuid().ToString("N"),
    EventQueueCapacity = Math.Max(131_072, iterations + 1),
    ConnectTimeoutMilliseconds = 25,
});

try
{
    for (int index = 0; index < 1_000; index++)
    {
        RewindRecorder.Debug("Benchmark", "Warmup", "constant payload");
    }

    var samples = new long[iterations];
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < iterations; index++)
    {
        long started = Stopwatch.GetTimestamp();
        RewindRecorder.Debug("Benchmark", "CallerLatency", "constant payload");
        samples[index] = Stopwatch.GetTimestamp() - started;
    }

    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Array.Sort(samples);
    double tickNanoseconds = 1_000_000_000d / Stopwatch.Frequency;
    RewindHealthSnapshot health = RewindRecorder.GetHealthSnapshot();
    var result = new
    {
        schemaVersion = 1,
        runtime = Environment.Version.ToString(),
        os = Environment.OSVersion.ToString(),
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        iterations,
        workload = "Agent absent; constant small event; queue sized above iteration count",
        latencyMicroseconds = new
        {
            p50 = Percentile(samples, 0.50) * tickNanoseconds / 1_000d,
            p95 = Percentile(samples, 0.95) * tickNanoseconds / 1_000d,
            p99 = Percentile(samples, 0.99) * tickNanoseconds / 1_000d,
            max = samples[^1] * tickNanoseconds / 1_000d,
        },
        allocatedBytesPerCall = (double)allocatedBytes / iterations,
        health.Accepted,
        health.DroppedQueueFull,
        health.DroppedInvalid,
    };
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    RewindRecorder.Shutdown();
}

static long Percentile(long[] sorted, double percentile)
{
    int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
}
