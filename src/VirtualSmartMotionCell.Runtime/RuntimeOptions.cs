namespace VirtualSmartMotionCell.Runtime;

public sealed class RuntimeOptions
{
    public int SimulationPeriodMilliseconds { get; set; } = 10;
    public int StatePublishPeriodMilliseconds { get; set; } = 50;
    public int CheckpointPeriodMilliseconds { get; set; } = 1000;
    public string DataDirectory { get; set; } = "runtime-data";
    public string RecipePath { get; set; } = "config/recipes/standard-widget.v1.json";
    public bool AllowRemoteCommands { get; set; }
    public string MotionAdapter { get; set; } = "simulated";
    public string ReplayPath { get; set; } = "config/replay/motion-replay.json";
}

public sealed class MesOptions
{
    public string Mode { get; set; } = "Http";
    public string BaseUrl { get; set; } = "http://localhost:8090/";
    public string MachineId { get; set; } = "VSMC-001";
    public bool PollOrders { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 2000;
}

public sealed class OpcUaOptions
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "opc.tcp://localhost:4840/vsmc";
    public string ApplicationName { get; set; } = "Virtual Smart Motion Cell";
}

public sealed class ObservabilityOptions
{
    public string? OtlpEndpoint { get; set; }
}
