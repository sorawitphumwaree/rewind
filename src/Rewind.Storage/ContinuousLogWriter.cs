using System.Text.Json;
using System.Globalization;
using Rewind.Agent.Core;

namespace Rewind.Storage;

public sealed class ContinuousLogWriter : IContinuousLogWriter
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private readonly long _maximumTotalBytes;
    private readonly int _maximumFileCount;

    public ContinuousLogWriter(
        string dataDirectory,
        string directoryName,
        long maximumFileBytes,
        long maximumTotalBytes,
        int maximumFileCount)
    {
        if (Path.IsPathRooted(directoryName)
            || directoryName.IndexOfAny(Path.GetInvalidPathChars()) >= 0
            || directoryName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Continuous log directory must be a safe relative name.", nameof(directoryName));
        }

        _directory = Path.Combine(Path.GetFullPath(dataDirectory), directoryName);
        _maximumFileBytes = maximumFileBytes;
        _maximumTotalBytes = maximumTotalBytes;
        _maximumFileCount = maximumFileCount;
    }

    public void Append(JsonElement value)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            string path = CurrentPath(payload.Length + Environment.NewLine.Length);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(payload);
            stream.WriteByte((byte)'\n');
            stream.Flush(flushToDisk: false);
            EnforceQuota();
        }
    }

    private string CurrentPath(int incomingBytes)
    {
        string prefix = DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        for (int index = 0; ; index++)
        {
            string candidate = Path.Combine(_directory, $"{prefix}-{index:D4}.jsonl");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length + incomingBytes <= _maximumFileBytes)
            {
                return candidate;
            }
        }
    }

    private void EnforceQuota()
    {
        FileInfo[] files = new DirectoryInfo(_directory)
            .EnumerateFiles("*.jsonl")
            .OrderBy(file => file.CreationTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        long total = files.Sum(file => file.Length);
        int remaining = files.Length;
        foreach (FileInfo file in files)
        {
            if (remaining <= _maximumFileCount && total <= _maximumTotalBytes)
            {
                break;
            }

            long length = file.Length;
            file.Delete();
            total -= length;
            remaining--;
        }
    }
}
