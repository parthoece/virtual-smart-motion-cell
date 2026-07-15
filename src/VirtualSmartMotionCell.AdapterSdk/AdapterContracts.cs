using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.AdapterSdk;

public sealed record AdapterDescriptor(string Id, string DisplayName, Version Version, IReadOnlyList<string> Capabilities);
public sealed record AdapterHealth(bool Healthy, string Status, DateTimeOffset CheckedAt, IReadOnlyDictionary<string, string> Details);

public interface IEquipmentAdapter : IAsyncDisposable
{
    AdapterDescriptor Descriptor { get; }
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    ValueTask<AdapterHealth> CheckHealthAsync(CancellationToken cancellationToken);
}

public interface IMotionSystem : IEquipmentAdapter
{
    MotionSystemSnapshot Snapshot(AxisMotionState requestedState);
    void Step(double dt, double maximumVelocity, double maximumAcceleration);
    void EnableAll();
    void DisableAll();
    void SetTarget(double x, double y);
    void Hold();
    void MarkHomed();
    void Restore(double xPosition, double yPosition, bool homed, double? xTarget = null, double? yTarget = null);
    void InjectFault(MotionFault fault);
    void ClearFault(MotionFault fault = MotionFault.None);
}

public interface IInspectionAdapter : IEquipmentAdapter
{
    ValueTask<(PartQuality Quality, double Score)> InspectAsync(string partId, CancellationToken cancellationToken);
}

public interface IBarcodeAdapter : IEquipmentAdapter
{
    ValueTask<string> ReadAsync(CancellationToken cancellationToken);
}
