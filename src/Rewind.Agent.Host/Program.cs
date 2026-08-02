using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rewind.Agent.Core;
using Rewind.Agent.Host;

Dictionary<string, string> values = ParseArguments(args);
string? configurationPath = values.GetValueOrDefault("--config");
AgentOptions options = configurationPath == null
    ? LoadCommandLineOptions(values)
    : AgentConfiguration.Load(configurationPath);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(service => service.ServiceName = "Rewind Agent");
builder.Services.AddSingleton(options);
builder.Services.AddHostedService<AgentWorker>();

using IHost host = builder.Build();
await host.RunAsync();

static Dictionary<string, string> ParseArguments(string[] arguments)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int index = 0; index < arguments.Length; index++)
    {
        string key = arguments[index];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unexpected argument '{key}'.");
        }

        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Argument '{key}' requires a value.");
        }

        result[key] = arguments[++index];
    }

    return result;
}

static AgentOptions LoadCommandLineOptions(IReadOnlyDictionary<string, string> values)
{
    var options = new AgentOptions
    {
        PipeName = values.GetValueOrDefault("--pipe", "Rewind.Agent"),
        DataDirectory = values.GetValueOrDefault(
            "--data",
            Path.Combine(Environment.CurrentDirectory, "data")),
        PreTrigger = TimeSpan.FromSeconds(ParseNonNegative(values, "--pre", 300)),
        PostTrigger = TimeSpan.FromSeconds(ParseNonNegative(values, "--post", 60)),
    };
    AgentConfiguration.Validate(options);
    return options;
}

static int ParseNonNegative(IReadOnlyDictionary<string, string> values, string key, int fallback)
{
    if (!values.TryGetValue(key, out string? text))
    {
        return fallback;
    }

    return int.TryParse(text, out int value) && value >= 0
        ? value
        : throw new ArgumentException($"Argument '{key}' must be a non-negative integer.");
}
