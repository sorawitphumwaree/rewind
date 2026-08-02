using Rewind.Sdk;

namespace Rewind.Sample.Playground.Component;

public static class MachineComponent
{
    public static void ReportCycle(int cycle)
        => RewindRecorder.Information(
            "MachineComponent",
            "CycleCompleted",
            $"Cycle {cycle} completed in a separately built class library.");

    public static void ReportWarning()
        => RewindRecorder.Warning(
            "MachineComponent",
            "PressureDrift",
            "Pressure is approaching the configured limit.");

    public static void ReportError()
        => RewindRecorder.Error(
            "MachineComponent",
            "MotionFault",
            "The simulated axis did not reach its target.");
}
