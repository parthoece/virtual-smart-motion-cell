using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Domain;

namespace VirtualSmartMotionCell.Application;

public interface IMachineEventStore
{
    ValueTask AppendAsync(MachineEvent machineEvent, CancellationToken cancellationToken);
}

public interface ICheckpointStore
{
    ValueTask SaveAsync(RecoveryCheckpoint checkpoint, CancellationToken cancellationToken);
    ValueTask<RecoveryCheckpoint?> LoadAsync(CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    ValueTask EnqueueAsync(MachineEvent machineEvent, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<MachineEvent>> ReadPendingAsync(int maximum, CancellationToken cancellationToken);
    ValueTask MarkDeliveredAsync(string eventId, CancellationToken cancellationToken);
    ValueTask<int> CountPendingAsync(CancellationToken cancellationToken);
}

public interface IRecipeStore
{
    ValueTask<MachineRecipe> LoadActiveAsync(CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<RecipeDescriptor>> ListAsync(CancellationToken cancellationToken);
    ValueTask<MachineRecipe?> LoadAsync(string recipeId, int revision, CancellationToken cancellationToken);
    ValueTask SaveDraftAsync(MachineRecipe recipe, CancellationToken cancellationToken);
    ValueTask<MachineRecipe> ApproveAsync(string recipeId, int revision, CancellationToken cancellationToken);
    ValueTask<MachineRecipe> ActivateAsync(string recipeId, int revision, CancellationToken cancellationToken);
}

public interface IProductionRepository
{
    ValueTask HandleEventAsync(MachineEvent machineEvent, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<ProductionOrderSnapshot>> ReadOrdersAsync(int maximum, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<PartRecord>> ReadPartsAsync(int maximum, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CycleRecord>> ReadCyclesAsync(int maximum, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<TraceabilityRecord>> ReadTraceabilityAsync(int maximum, CancellationToken cancellationToken);
}

public interface IAlarmHistoryStore
{
    ValueTask AppendAsync(AlarmSnapshot alarm, string action, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<AlarmEventPayload>> ReadAsync(int maximum, CancellationToken cancellationToken);
}

public interface IMachineStatePublisher
{
    ValueTask PublishAsync(MachineStateSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IManufacturingGateway
{
    ValueTask<bool> DeliverAsync(MachineEvent machineEvent, CancellationToken cancellationToken);
    ValueTask<(IntegrationHealth Health, string Status)> CheckHealthAsync(CancellationToken cancellationToken);
}
