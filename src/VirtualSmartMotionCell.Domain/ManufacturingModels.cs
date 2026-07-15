using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Domain;

public sealed class ProductionOrder
{
    public ProductionOrder(string orderId, int targetQuantity, string recipeId, int recipeRevision)
    {
        if (string.IsNullOrWhiteSpace(orderId)) throw new ArgumentException("Order ID is required.", nameof(orderId));
        if (targetQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(targetQuantity));
        OrderId = orderId.Trim();
        TargetQuantity = targetQuantity;
        RecipeId = recipeId;
        RecipeRevision = recipeRevision;
        LoadedAt = DateTimeOffset.UtcNow;
        Status = ProductionOrderStatus.Queued;
    }

    public string OrderId { get; }
    public int TargetQuantity { get; }
    public long CompletedQuantity { get; private set; }
    public string RecipeId { get; }
    public int RecipeRevision { get; }
    public ProductionOrderStatus Status { get; private set; }
    public DateTimeOffset LoadedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Activate() => Status = ProductionOrderStatus.Active;
    public void Pause() { if (Status == ProductionOrderStatus.Active) Status = ProductionOrderStatus.Paused; }
    public void Resume() { if (Status == ProductionOrderStatus.Paused) Status = ProductionOrderStatus.Active; }
    public void Cancel() => Status = ProductionOrderStatus.Cancelled;

    public bool RecordCompletedPart()
    {
        CompletedQuantity++;
        if (CompletedQuantity < TargetQuantity) return false;
        Status = ProductionOrderStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public void Restore(long completedQuantity, ProductionOrderStatus status)
    {
        CompletedQuantity = Math.Clamp(completedQuantity, 0, TargetQuantity);
        Status = status;
        if (status == ProductionOrderStatus.Completed) CompletedAt = DateTimeOffset.UtcNow;
    }

    public ProductionOrderSnapshot ToSnapshot() => new(
        OrderId, TargetQuantity, CompletedQuantity, RecipeId, RecipeRevision, Status, LoadedAt, CompletedAt);
}

public sealed record PartRecord(
    string PartId,
    string OrderId,
    string RecipeId,
    int RecipeRevision,
    PartQuality Quality,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record CycleRecord(
    string CycleId,
    string PartId,
    string OrderId,
    long SequenceNumber,
    double DurationSeconds,
    PartQuality Quality,
    DateTimeOffset CompletedAt,
    string CorrelationId);

public sealed record TraceabilityRecord(
    string TraceId,
    string OrderId,
    string? PartId,
    string EventName,
    string Details,
    DateTimeOffset Timestamp,
    string CorrelationId);

public sealed record RecipeDescriptor(
    string RecipeId,
    int Revision,
    int SchemaVersion,
    RecipeLifecycle Lifecycle,
    string Checksum,
    DateTimeOffset UpdatedAt);
