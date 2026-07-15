namespace VirtualSmartMotionCell.Contracts;

public sealed record AxisSnapshot(
    string Name,
    double CommandPosition,
    double ActualPosition,
    double ActualVelocity,
    double FollowingError,
    double TargetPosition,
    bool Enabled,
    bool Homed,
    bool Moving,
    bool Faulted,
    AxisMotionState MotionState);

public sealed record MotionSystemSnapshot(
    AxisSnapshot XAxis,
    AxisSnapshot YAxis,
    bool AtTarget,
    bool AllHomed,
    bool AnyFaulted,
    string AdapterId,
    string AdapterStatus);

public sealed record InterlockSnapshot(
    bool EmergencyStopOk,
    bool GuardClosed,
    bool BusHealthy,
    bool DrivesHealthy,
    bool MotionPermitted,
    IReadOnlyList<string> BlockingReasons);

public sealed record AlarmSnapshot(
    string Code,
    string Source,
    AlarmSeverity Severity,
    AlarmLifecycle Lifecycle,
    string Message,
    string RecommendedAction,
    DateTimeOffset RaisedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ClearedAt,
    string? AcknowledgedBy,
    string CorrelationId);

public sealed record PartSnapshot(
    string PartId,
    bool AttachedToGripper,
    PartQuality Quality,
    string RecipeId,
    int RecipeRevision);

public sealed record ProductionOrderSnapshot(
    string OrderId,
    int TargetQuantity,
    long CompletedQuantity,
    string RecipeId,
    int RecipeRevision,
    ProductionOrderStatus Status,
    DateTimeOffset LoadedAt,
    DateTimeOffset? CompletedAt);

public sealed record ProductionSnapshot(
    long CycleCount,
    long GoodCount,
    long RejectCount,
    ProductionOrderSnapshot? ActiveOrder,
    string? ActivePartId,
    double LastCycleSeconds,
    double Availability,
    double Performance,
    double Quality,
    double Oee);

public sealed record RuntimeSnapshot(
    double LoopPeriodMilliseconds,
    double LastLoopDurationMilliseconds,
    double MaximumLoopDurationMilliseconds,
    long DeadlineMissCount,
    int CommandQueueDepth,
    int ConnectedClients,
    DateTimeOffset StartedAt,
    TimeSpan Uptime);

public sealed record RecipeSnapshot(
    string RecipeId,
    int Revision,
    int SchemaVersion,
    RecipeLifecycle Lifecycle,
    string Checksum);

public sealed record IntegrationSnapshot(
    IntegrationHealth MesHealth,
    string MesStatus,
    int OutboxPending,
    DateTimeOffset? LastSuccessfulDelivery,
    string OpcUaEndpoint,
    bool OpcUaEnabled);

public sealed record RecoverySnapshot(
    bool Required,
    string Reason,
    IReadOnlyList<RecoveryAction> AllowedActions);

public sealed record MachineStateSnapshot(
    long Revision,
    DateTimeOffset Timestamp,
    double SimulationTime,
    MachineMode Mode,
    ExecutionState ExecutionState,
    ProductionStep ProductionStep,
    bool GripperClosed,
    AxisSnapshot XAxis,
    AxisSnapshot YAxis,
    InterlockSnapshot Interlocks,
    PartSnapshot? ActivePart,
    IReadOnlyList<AlarmSnapshot> ActiveAlarms,
    ProductionSnapshot Production,
    RuntimeSnapshot Runtime,
    RecipeSnapshot ActiveRecipe,
    IntegrationSnapshot Integration,
    RecoverySnapshot Recovery,
    string LastTransition,
    string CorrelationId);
