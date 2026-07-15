using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Domain;

public sealed class AlarmRecord
{
    public AlarmRecord(string code, string source, AlarmSeverity severity, string message, string recommendedAction, string correlationId)
    {
        Code = code;
        Source = source;
        Severity = severity;
        Message = message;
        RecommendedAction = recommendedAction;
        CorrelationId = correlationId;
        RaisedAt = DateTimeOffset.UtcNow;
        Lifecycle = AlarmLifecycle.ActiveUnacknowledged;
    }

    public string Code { get; }
    public string Source { get; }
    public AlarmSeverity Severity { get; }
    public string Message { get; }
    public string RecommendedAction { get; }
    public string CorrelationId { get; }
    public DateTimeOffset RaisedAt { get; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? ClearedAt { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public AlarmLifecycle Lifecycle { get; private set; }

    public void Acknowledge(string user)
    {
        if (Lifecycle == AlarmLifecycle.ActiveUnacknowledged)
        {
            Lifecycle = AlarmLifecycle.ActiveAcknowledged;
            AcknowledgedAt = DateTimeOffset.UtcNow;
            AcknowledgedBy = user;
        }
        else if (Lifecycle == AlarmLifecycle.ClearedUnacknowledged)
        {
            Lifecycle = AlarmLifecycle.Historical;
            AcknowledgedAt = DateTimeOffset.UtcNow;
            AcknowledgedBy = user;
        }
    }

    public void Clear()
    {
        ClearedAt = DateTimeOffset.UtcNow;
        Lifecycle = Lifecycle == AlarmLifecycle.ActiveAcknowledged
            ? AlarmLifecycle.Historical
            : AlarmLifecycle.ClearedUnacknowledged;
    }

    public AlarmSnapshot ToSnapshot() => new(
        Code, Source, Severity, Lifecycle, Message, RecommendedAction,
        RaisedAt, AcknowledgedAt, ClearedAt, AcknowledgedBy, CorrelationId);
}
