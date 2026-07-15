using System.Diagnostics;
using System.Diagnostics.Metrics;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Application;

public static class MachineTelemetry
{
    public const string ServiceName = "VirtualSmartMotionCell";
    public const string ActivitySourceName = "VirtualSmartMotionCell.Machine";
    public const string MeterName = "VirtualSmartMotionCell.Machine";

    public static readonly ActivitySource Activities = new(ActivitySourceName, "0.5.0");
    private static readonly Meter Meter = new(MeterName, "0.5.0");
    private static readonly Counter<long> Commands = Meter.CreateCounter<long>("vsmc.commands");
    private static readonly Counter<long> RejectedCommands = Meter.CreateCounter<long>("vsmc.commands.rejected");
    private static readonly Counter<long> Transitions = Meter.CreateCounter<long>("vsmc.transitions");
    private static readonly Counter<long> Cycles = Meter.CreateCounter<long>("vsmc.cycles");
    private static readonly Counter<long> Alarms = Meter.CreateCounter<long>("vsmc.alarms");
    private static readonly Histogram<double> LoopDuration = Meter.CreateHistogram<double>("vsmc.loop.duration", "ms");
    private static readonly Histogram<double> CycleDuration = Meter.CreateHistogram<double>("vsmc.cycle.duration", "s");

    public static void RecordCommand(string type, CommandResult result)
    {
        var tags = new TagList { { "command.type", type }, { "command.status", result.Status.ToString() }, { "reason.code", result.ReasonCode } };
        Commands.Add(1, tags);
        if (result.Status != CommandStatus.Accepted) RejectedCommands.Add(1, tags);
    }

    public static void RecordTransition(ExecutionState state, ProductionStep step) =>
        Transitions.Add(1, new TagList { { "execution.state", state.ToString() }, { "production.step", step.ToString() } });

    public static void RecordCycle(double durationSeconds, PartQuality quality)
    {
        Cycles.Add(1, new TagList { { "quality", quality.ToString() } });
        CycleDuration.Record(durationSeconds, new TagList { { "quality", quality.ToString() } });
    }

    public static void RecordAlarm(string code, AlarmSeverity severity) =>
        Alarms.Add(1, new TagList { { "alarm.code", code }, { "alarm.severity", severity.ToString() } });

    public static void RecordLoop(double durationMilliseconds) => LoopDuration.Record(durationMilliseconds);
}
