using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Control;

public sealed class SimulatedMotionSystem : IMotionSystem
{
    private readonly SimulatedAxis _x = new("X", -1.0, 1.0);
    private readonly SimulatedAxis _y = new("Y", 0.0, 1.2);

    public AdapterDescriptor Descriptor { get; } = new(
        "motion.simulated.xy", "Deterministic simulated XY motion system", new Version(1, 0, 0),
        new[] { "two-axis", "pid", "motion-profile", "fault-injection", "cross-platform" });

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<AdapterHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var healthy = !_x.Faulted && !_y.Faulted;
        return ValueTask.FromResult(new AdapterHealth(healthy, healthy ? "healthy" : "faulted", DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["x"] = _x.Faulted ? "faulted" : "ready", ["y"] = _y.Faulted ? "faulted" : "ready" }));
    }

    public MotionSystemSnapshot Snapshot(AxisMotionState requestedState) => new(
        _x.Snapshot(requestedState), _y.Snapshot(requestedState), _x.AtTarget() && _y.AtTarget(),
        _x.Homed && _y.Homed, _x.Faulted || _y.Faulted, Descriptor.Id, _x.Faulted || _y.Faulted ? "faulted" : "ready");

    public void Step(double dt, double maximumVelocity, double maximumAcceleration)
    {
        _x.Step(dt, maximumVelocity, maximumAcceleration);
        _y.Step(dt, maximumVelocity, maximumAcceleration);
    }

    public void EnableAll() { _x.Enable(); _y.Enable(); }
    public void DisableAll() { _x.Disable(); _y.Disable(); }
    public void SetTarget(double x, double y) { _x.SetTarget(x); _y.SetTarget(y); }
    public void Hold() { _x.Hold(); _y.Hold(); }
    public void MarkHomed() { _x.MarkHomed(); _y.MarkHomed(); }
    public void Restore(double xPosition, double yPosition, bool homed, double? xTarget = null, double? yTarget = null)
    {
        _x.Restore(xPosition, homed, xTarget);
        _y.Restore(yPosition, homed, yTarget);
    }

    public void InjectFault(MotionFault fault)
    {
        switch (fault)
        {
            case MotionFault.DriveFaultX: _x.InjectFault(); break;
            case MotionFault.DriveFaultY: _y.InjectFault(); break;
            case MotionFault.FollowingError: _x.ForceFollowingError = true; break;
            case MotionFault.FrozenAxisX: _x.Frozen = true; break;
            case MotionFault.FrozenAxisY: _y.Frozen = true; break;
        }
    }

    public void ClearFault(MotionFault fault = MotionFault.None)
    {
        if (fault is MotionFault.None or MotionFault.DriveFaultX or MotionFault.FollowingError or MotionFault.FrozenAxisX) _x.ClearFault();
        if (fault is MotionFault.None or MotionFault.DriveFaultY or MotionFault.FrozenAxisY) _y.ClearFault();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
