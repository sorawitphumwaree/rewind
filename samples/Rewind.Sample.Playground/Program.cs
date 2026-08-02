using Rewind.Sample.Playground.Component;
using Rewind.Sdk;

InitializationResult initialization = RewindRecorder.Initialize(new RewindOptions
{
    AgentPipeName = "Rewind.Playground",
    EventQueueCapacity = 1024,
    ControlQueueCapacity = 32,
});

Console.WriteLine($"Recorder initialization: {initialization.Status}");
RewindRecorder.SetContext("MachineId", "PLAYGROUND-01");
RewindRecorder.SetContext("SoftwareVersion", "0.1.0-alpha.1");

int cycle = 0;
bool running = true;
while (running)
{
    Console.WriteLine();
    Console.WriteLine("1 Information from this executable");
    Console.WriteLine("2 Information from the separate component DLL");
    Console.WriteLine("3 Warning from the component DLL");
    Console.WriteLine("4 Error from the component DLL (default automatic trigger)");
    Console.WriteLine("5 Manual incident trigger");
    Console.WriteLine("6 Recorder health");
    Console.WriteLine("0 Flush, shut down, and exit");
    Console.Write("> ");

    switch (Console.ReadLine())
    {
        case "1":
            RewindRecorder.Information("Playground", "OperatorAction", "Information emitted by the executable.");
            break;
        case "2":
            MachineComponent.ReportCycle(++cycle);
            break;
        case "3":
            MachineComponent.ReportWarning();
            break;
        case "4":
            MachineComponent.ReportError();
            break;
        case "5":
            RewindRecorder.TriggerIncident("ManualPlaygroundTrigger", "Requested from the playground menu.");
            break;
        case "6":
            PrintHealth(RewindRecorder.GetHealthSnapshot());
            break;
        case "0":
            running = false;
            break;
        default:
            Console.WriteLine("Choose 0-6.");
            break;
    }
}

ShutdownResult shutdown = await RewindRecorder.ShutdownAsync(TimeSpan.FromSeconds(2));
Console.WriteLine($"Shutdown completed={shutdown.Completed}, unresolved={shutdown.UnresolvedCount}");

static void PrintHealth(RewindHealthSnapshot health)
{
    Console.WriteLine(
        $"accepted={health.Accepted}, sent={health.Sent}, "
        + $"dropped={health.DroppedQueueFull + health.DroppedInvalid}, "
        + $"transportFailures={health.TransportFailures}, pending={health.Pending}");
}
