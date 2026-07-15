using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Application;

public sealed class IntegrationStatusStore
{
    private readonly object _gate = new();
    private IntegrationHealth _mesHealth = IntegrationHealth.Unknown;
    private string _mesStatus = "not checked";
    private int _outboxPending;
    private DateTimeOffset? _lastSuccessfulDelivery;
    private string _opcUaEndpoint = "opc.tcp://localhost:4840/vsmc";
    private bool _opcUaEnabled;

    public void UpdateMes(IntegrationHealth health, string status)
    {
        lock (_gate) { _mesHealth = health; _mesStatus = status; }
    }

    public void SetOutboxPending(int count) { lock (_gate) _outboxPending = Math.Max(0, count); }
    public void MarkDelivered(DateTimeOffset timestamp) { lock (_gate) _lastSuccessfulDelivery = timestamp; }
    public void ConfigureOpcUa(bool enabled, string endpoint) { lock (_gate) { _opcUaEnabled = enabled; _opcUaEndpoint = endpoint; } }

    public IntegrationSnapshot Snapshot()
    {
        lock (_gate) return new IntegrationSnapshot(_mesHealth, _mesStatus, _outboxPending, _lastSuccessfulDelivery, _opcUaEndpoint, _opcUaEnabled);
    }
}
