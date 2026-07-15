namespace VirtualSmartMotionCell.Contracts;

public sealed record MachineCommandRequest(
    string Type,
    string? Axis = null,
    double? Value = null,
    string? Mode = null,
    string? Fault = null,
    string? RequestedBy = null,
    string? CorrelationId = null,
    string? OrderId = null,
    int? Quantity = null,
    string? RecipeId = null,
    int? RecipeRevision = null,
    string? RecoveryAction = null);

public sealed record CommandResult(
    string CommandId,
    CommandStatus Status,
    string ReasonCode,
    IReadOnlyList<string> Reasons,
    DateTimeOffset CompletedAt,
    long MachineRevision,
    string CorrelationId)
{
    public static CommandResult Accepted(string commandId, long revision, string correlationId, string reasonCode = "ACCEPTED") =>
        new(commandId, CommandStatus.Accepted, reasonCode, Array.Empty<string>(), DateTimeOffset.UtcNow, revision, correlationId);

    public static CommandResult Rejected(string commandId, long revision, string correlationId, string reasonCode, params string[] reasons) =>
        new(commandId, CommandStatus.Rejected, reasonCode, reasons, DateTimeOffset.UtcNow, revision, correlationId);
}

public sealed record MachineEvent(
    string EventId,
    string EventType,
    DateTimeOffset Timestamp,
    string CorrelationId,
    long MachineRevision,
    object Payload);

public sealed record ProductionOrderRequest(string OrderId, int Quantity, string RecipeId, int RecipeRevision = 1);

public sealed record OrderLoadedPayload(string OrderId, int Quantity, string RecipeId, int RecipeRevision, ProductionOrderStatus Status);
public sealed record OrderStatusChangedPayload(string OrderId, ProductionOrderStatus Status, long CompletedQuantity, string Reason);
public sealed record CycleStartedPayload(string OrderId, string PartId, string RecipeId, int RecipeRevision, double SimulationTime);
public sealed record CycleCompletedPayload(string OrderId, string PartId, long CycleNumber, PartQuality Quality, double DurationSeconds, string RecipeId, int RecipeRevision);
public sealed record TraceabilityPayload(string OrderId, string? PartId, string EventName, string Details);
public sealed record RecipeActivatedPayload(string RecipeId, int Revision);
public sealed record AlarmEventPayload(AlarmSnapshot Alarm, string Action);

public sealed record RecoveryCheckpoint(
    DateTimeOffset Timestamp,
    long MachineRevision,
    MachineMode Mode,
    ExecutionState ExecutionState,
    ProductionStep ProductionStep,
    string? ActivePartId,
    string? ActiveOrderId,
    int? OrderTargetQuantity,
    long OrderCompletedQuantity,
    string RecipeId,
    int RecipeRevision,
    bool GripperClosed,
    double XPosition,
    double YPosition,
    double XTarget,
    double YTarget,
    double? CycleStartedSimulationTime,
    string Reason);
