using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Control;

public sealed class FaultInjectingMotionSystem(IMotionSystem inner) : IMotionSystem
{
    private MotionFault _activeFault;

    public AdapterDescriptor Descriptor { get; } = new(
        "motion.fault-injecting", "Fault-injecting motion decorator", new Version(1, 0, 0),
        new[] { "decorator", "deterministic-faults", "recovery-testing" });

    public ValueTask InitializeAsync(CancellationToken cancellationToken) => inner.InitializeAsync(cancellationToken);

    public async ValueTask<AdapterHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var health = await inner.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        return _activeFault == MotionFault.None
            ? health
            : health with { Healthy = false, Status = $"injected:{_activeFault}" };
    }

    public MotionSystemSnapshot Snapshot(AxisMotionState requestedState)
    {
        var snapshot = inner.Snapshot(requestedState);
        return snapshot with { AdapterId = Descriptor.Id, AdapterStatus = _activeFault == MotionFault.None ? snapshot.AdapterStatus : $"injected:{_activeFault}" };
    }

    public void Step(double dt, double maximumVelocity, double maximumAcceleration) => inner.Step(dt, maximumVelocity, maximumAcceleration);
    public void EnableAll() => inner.EnableAll();
    public void DisableAll() => inner.DisableAll();
    public void SetTarget(double x, double y) => inner.SetTarget(x, y);
    public void Hold() => inner.Hold();
    public void MarkHomed() => inner.MarkHomed();
    public void Restore(double xPosition, double yPosition, bool homed, double? xTarget = null, double? yTarget = null) => inner.Restore(xPosition, yPosition, homed, xTarget, yTarget);

    public void InjectFault(MotionFault fault)
    {
        _activeFault = fault;
        inner.InjectFault(fault);
    }

    public void ClearFault(MotionFault fault = MotionFault.None)
    {
        inner.ClearFault(fault);
        if (fault == MotionFault.None || fault == _activeFault) _activeFault = MotionFault.None;
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
