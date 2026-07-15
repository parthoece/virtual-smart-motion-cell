using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Control;

public sealed record MotionReplayFrame(double Time, AxisSnapshot XAxis, AxisSnapshot YAxis);

public sealed class ReplayMotionSystem : IMotionSystem
{
    private readonly IReadOnlyList<MotionReplayFrame> _frames;
    private int _index;

    public ReplayMotionSystem(IReadOnlyList<MotionReplayFrame> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("At least one replay frame is required.", nameof(frames));
        _frames = frames;
    }

    public AdapterDescriptor Descriptor { get; } = new(
        "motion.replay", "Recorded motion replay adapter", new Version(1, 0, 0),
        new[] { "replay", "offline-analysis", "regression-testing" });

    public ValueTask InitializeAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _index = 0; return ValueTask.CompletedTask; }
    public ValueTask<AdapterHealth> CheckHealthAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new AdapterHealth(true, "replaying", DateTimeOffset.UtcNow, new Dictionary<string, string>())); }

    public MotionSystemSnapshot Snapshot(AxisMotionState requestedState)
    {
        var frame = _frames[Math.Clamp(_index, 0, _frames.Count - 1)];
        var x = frame.XAxis with { MotionState = requestedState };
        var y = frame.YAxis with { MotionState = requestedState };
        var atTarget = Math.Abs(x.TargetPosition - x.ActualPosition) < 0.01 && Math.Abs(y.TargetPosition - y.ActualPosition) < 0.01;
        return new MotionSystemSnapshot(x, y, atTarget, x.Homed && y.Homed, x.Faulted || y.Faulted, Descriptor.Id, "replaying");
    }

    public void Step(double dt, double maximumVelocity, double maximumAcceleration) { if (_index < _frames.Count - 1) _index++; }
    public void EnableAll() { }
    public void DisableAll() { }
    public void SetTarget(double x, double y) { }
    public void Hold() { }
    public void MarkHomed() { }
    public void Restore(double xPosition, double yPosition, bool homed, double? xTarget = null, double? yTarget = null) => _index = 0;
    public void InjectFault(MotionFault fault) { }
    public void ClearFault(MotionFault fault = MotionFault.None) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
