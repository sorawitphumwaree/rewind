using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rewind.Agent.Core;
using Rewind.Storage;

namespace Rewind.Agent.Host;

public sealed class AgentWorker : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> Listening =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, nameof(Listening)),
            "Rewind Agent listening on pipe '{PipeName}'.");
    private static readonly Action<ILogger, string, Exception?> DataDirectory =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, nameof(DataDirectory)),
            "Data directory: {DataDirectory}");
    private static readonly Action<ILogger, Exception?> ImmutableConfiguration =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(3, nameof(ImmutableConfiguration)),
            "Configuration is immutable until the Agent is restarted.");
    private readonly AgentOptions _options;
    private readonly ILogger<AgentWorker> _logger;
    private RewindAgent? _agent;

    public AgentWorker(AgentOptions options, ILogger<AgentWorker> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _agent = RewindAgentFactory.Create(_options);
        Listening(_logger, _options.PipeName, null);
        DataDirectory(_logger, Path.GetFullPath(_options.DataDirectory), null);
        ImmutableConfiguration(_logger, null);
        await _agent.RunAsync(stoppingToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _agent?.Dispose();
        base.Dispose();
    }
}
