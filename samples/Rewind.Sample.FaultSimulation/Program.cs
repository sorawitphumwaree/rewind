using Rewind.Sdk;

RewindRecorder.Initialize();
RewindRecorder.SetContext("MachineId", "SIM-01");
RewindRecorder.SetContext("SoftwareVersion", "0.1.0");

for (int index = 0; index < 20; index++)
{
    RewindRecorder.Debug("FaultSimulation", "Cycle", $"Cycle {index} completed.");
    await Task.Delay(50);
}

RewindRecorder.Warning("FaultSimulation", "PressureDrift", "Pressure exceeded the simulated warning threshold.");
RewindRecorder.TriggerIncident("SimulatedFailure", "Operator initiated a walking-skeleton capture.");

for (int index = 0; index < 10; index++)
{
    RewindRecorder.Information("FaultSimulation", "Recovery", $"Recovery step {index}.");
    await Task.Delay(50);
}

await Task.Delay(500);
RewindHealthSnapshot health = RewindRecorder.GetHealthSnapshot();
Console.WriteLine($"accepted={health.Accepted} sent={health.Sent} dropped={health.DroppedQueueFull + health.DroppedInvalid}");
RewindRecorder.Shutdown();
