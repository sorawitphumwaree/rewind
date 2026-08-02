using System.Text.Json;
using System.Text.Json.Serialization;
using Rewind.Abstractions;

namespace Rewind.Agent.Core;

public static class AgentConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static AgentOptions Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using FileStream stream = File.OpenRead(fullPath);
        ConfigurationDto configuration = JsonSerializer.Deserialize<ConfigurationDto>(
            stream,
            SerializerOptions) ?? throw new InvalidDataException("Configuration is empty.");

        var defaults = new AgentOptions();
        string dataDirectory = configuration.Agent?.DataDirectory ?? defaults.DataDirectory;
        if (!Path.IsPathRooted(dataDirectory))
        {
            dataDirectory = Path.GetFullPath(dataDirectory, Path.GetDirectoryName(fullPath)!);
        }

        var result = new AgentOptions
        {
            PipeName = configuration.Agent?.PipeName ?? defaults.PipeName,
            DataDirectory = dataDirectory,
            MaximumConcurrentClients = configuration.Agent?.MaximumConcurrentClients ?? defaults.MaximumConcurrentClients,
            Retention = Seconds(configuration.Buffer?.RetentionSeconds, defaults.Retention),
            MaximumEventCount = configuration.Buffer?.MaximumEventCount ?? defaults.MaximumEventCount,
            MaximumBufferBytes = configuration.Buffer?.MaximumBytes ?? defaults.MaximumBufferBytes,
            PreTrigger = Seconds(configuration.Capture?.PreTriggerSeconds, defaults.PreTrigger),
            PostTrigger = Seconds(configuration.Capture?.PostTriggerSeconds, defaults.PostTrigger),
            MaximumCapture = Seconds(configuration.Capture?.MaximumCaptureSeconds, defaults.MaximumCapture),
            MergeTriggers = configuration.Capture?.MergeTriggers ?? defaults.MergeTriggers,
            MaximumTriggersPerCapture = configuration.Capture?.MaximumTriggersPerCapture ?? defaults.MaximumTriggersPerCapture,
            MaximumStoredIncidents = configuration.IncidentStorage?.MaximumIncidentCount ?? defaults.MaximumStoredIncidents,
            MaximumStorageBytes = configuration.IncidentStorage?.MaximumBytes ?? defaults.MaximumStorageBytes,
            LevelPolicies = LoadLevelPolicies(configuration.Levels),
            ContinuousLog = new ContinuousLogOptions
            {
                DirectoryName = configuration.ContinuousLog?.DirectoryName ?? defaults.ContinuousLog.DirectoryName,
                MaximumFileBytes = configuration.ContinuousLog?.MaximumFileBytes ?? defaults.ContinuousLog.MaximumFileBytes,
                MaximumTotalBytes = configuration.ContinuousLog?.MaximumTotalBytes ?? defaults.ContinuousLog.MaximumTotalBytes,
                MaximumFileCount = configuration.ContinuousLog?.MaximumFileCount ?? defaults.ContinuousLog.MaximumFileCount,
            },
        };
        Validate(result);
        return result;
    }

    public static void Validate(AgentOptions value)
    {
        if (string.IsNullOrWhiteSpace(value.PipeName) || value.PipeName.Length > 200)
        {
            throw new InvalidDataException("Agent pipeName must contain 1-200 characters.");
        }

        if (value.MaximumConcurrentClients is < 1 or > 254)
        {
            throw new InvalidDataException("maximumConcurrentClients must be between 1 and 254.");
        }

        if (value.MaximumEventCount <= 0 || value.MaximumBufferBytes <= 0)
        {
            throw new InvalidDataException("Buffer count and byte limits must be positive.");
        }

        if (value.Retention <= TimeSpan.Zero
            || value.PreTrigger < TimeSpan.Zero
            || value.PostTrigger < TimeSpan.Zero
            || value.MaximumCapture < value.PostTrigger
            || value.PreTrigger > value.Retention)
        {
            throw new InvalidDataException("Capture durations conflict with retention or maximum capture.");
        }

        if (value.MaximumTriggersPerCapture <= 0
            || value.MaximumStoredIncidents <= 0
            || value.MaximumStorageBytes <= 0
            || value.ContinuousLog.MaximumFileBytes <= 0
            || value.ContinuousLog.MaximumTotalBytes < value.ContinuousLog.MaximumFileBytes
            || value.ContinuousLog.MaximumFileCount <= 0)
        {
            throw new InvalidDataException("Trigger, incident, and continuous-log limits are invalid.");
        }

        ValidateDataDirectory(value.DataDirectory);
        ValidateRelativeDirectory(value.ContinuousLog.DirectoryName);
    }

    private static TimeSpan Seconds(double? value, TimeSpan fallback)
        => value.HasValue ? TimeSpan.FromSeconds(value.Value) : fallback;

    private static void ValidateDataDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException("Data directory has no filesystem root.");
        if (string.Equals(
            fullPath.TrimEnd(Path.DirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A filesystem root cannot be used as the data directory.");
        }

        DirectoryInfo? current = new DirectoryInfo(fullPath);
        while (current != null && current.Exists)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Data directory cannot traverse a reparse point.");
            }

            current = current.Parent;
        }
    }

    private static void ValidateRelativeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Contains("..", StringComparison.Ordinal)
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidDataException("continuousLog.directoryName must be a safe relative directory.");
        }
    }

    private static Dictionary<RewindLevel, LevelPolicy> LoadLevelPolicies(LevelsDto? values)
    {
        var result = new Dictionary<RewindLevel, LevelPolicy>();
        foreach (RewindLevel level in Enum.GetValues<RewindLevel>())
        {
            LevelPolicy defaults = LevelPolicy.Defaults[level];
            LevelPolicyDto? configured = level switch
            {
                RewindLevel.Trace => values?.Trace,
                RewindLevel.Debug => values?.Debug,
                RewindLevel.Information => values?.Information,
                RewindLevel.Warning => values?.Warning,
                RewindLevel.Error => values?.Error,
                RewindLevel.Critical => values?.Critical,
                _ => null,
            };
            result[level] = new LevelPolicy(
                configured?.Buffer ?? defaults.Buffer,
                configured?.PersistContinuously ?? defaults.PersistContinuously,
                configured?.TriggerIncident ?? defaults.TriggerIncident,
                configured?.IncludeInIncident ?? defaults.IncludeInIncident);
        }

        return result;
    }

    private sealed class ConfigurationDto
    {
        public AgentDto? Agent { get; set; }
        public BufferDto? Buffer { get; set; }
        public CaptureDto? Capture { get; set; }
        public IncidentStorageDto? IncidentStorage { get; set; }
        public LevelsDto? Levels { get; set; }
        public ContinuousLogDto? ContinuousLog { get; set; }
    }

    private sealed class AgentDto
    {
        public string? PipeName { get; set; }
        public string? DataDirectory { get; set; }
        public int? MaximumConcurrentClients { get; set; }
    }

    private sealed class BufferDto
    {
        public double? RetentionSeconds { get; set; }
        public int? MaximumEventCount { get; set; }
        public long? MaximumBytes { get; set; }
    }

    private sealed class CaptureDto
    {
        public double? PreTriggerSeconds { get; set; }
        public double? PostTriggerSeconds { get; set; }
        public double? MaximumCaptureSeconds { get; set; }
        public bool? MergeTriggers { get; set; }
        public int? MaximumTriggersPerCapture { get; set; }
    }

    private sealed class IncidentStorageDto
    {
        public int? MaximumIncidentCount { get; set; }
        public long? MaximumBytes { get; set; }
    }

    private sealed class LevelsDto
    {
        public LevelPolicyDto? Trace { get; set; }
        public LevelPolicyDto? Debug { get; set; }
        public LevelPolicyDto? Information { get; set; }
        public LevelPolicyDto? Warning { get; set; }
        public LevelPolicyDto? Error { get; set; }
        public LevelPolicyDto? Critical { get; set; }
    }

    private sealed class LevelPolicyDto
    {
        public bool? Buffer { get; set; }
        public bool? PersistContinuously { get; set; }
        public bool? TriggerIncident { get; set; }
        public bool? IncludeInIncident { get; set; }
    }

    private sealed class ContinuousLogDto
    {
        public string? DirectoryName { get; set; }
        public long? MaximumFileBytes { get; set; }
        public long? MaximumTotalBytes { get; set; }
        public int? MaximumFileCount { get; set; }
    }
}
