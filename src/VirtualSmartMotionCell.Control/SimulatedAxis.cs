using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Control;

public sealed class SimulatedAxis
{
    private readonly PidController _controller = new(30.0, 2.5, 1.8, 8.0, 1.5);
    private double _profileVelocity;

    public SimulatedAxis(string name, double minimum, double maximum)
    {
        Name = name;
        Minimum = minimum;
        Maximum = maximum;
    }

    public string Name { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double CommandPosition { get; private set; }
    public double ActualPosition { get; private set; }
    public double ActualVelocity { get; private set; }
    public double TargetPosition { get; private set; }
    public bool Enabled { get; private set; }
    public bool Homed { get; private set; }
    public bool Faulted { get; private set; }
    public bool ForceFollowingError { get; set; }
    public bool Frozen { get; set; }

    public void Enable() => Enabled = !Faulted;
    public void Disable() { Enabled = false; _profileVelocity = 0; }
    public void MarkHomed() => Homed = true;
    public void ClearHomed() => Homed = false;

    public void SetTarget(double target)
    {
        if (target < Minimum || target > Maximum) throw new ArgumentOutOfRangeException(nameof(target));
        TargetPosition = target;
    }

    public void Hold()
    {
        TargetPosition = ActualPosition;
        CommandPosition = ActualPosition;
        _profileVelocity = 0;
        _controller.Reset();
    }

    public void InjectFault() { Faulted = true; Enabled = false; }
    public void ClearFault() { Faulted = false; ForceFollowingError = false; Frozen = false; _controller.Reset(); }

    public void Restore(double position, bool homed, double? target = null)
    {
        ActualPosition = Math.Clamp(position, Minimum, Maximum);
        CommandPosition = ActualPosition;
        TargetPosition = Math.Clamp(target ?? ActualPosition, Minimum, Maximum);
        ActualVelocity = 0;
        _profileVelocity = 0;
        Homed = homed;
        Enabled = false;
        Faulted = false;
        ForceFollowingError = false;
        Frozen = false;
        _controller.Reset();
    }

    public void Step(double dt, double maximumVelocity, double maximumAcceleration)
    {
        if (!Enabled || Faulted)
        {
            ActualVelocity *= Math.Exp(-8.0 * dt);
            ActualPosition += ActualVelocity * dt;
            return;
        }

        var remaining = TargetPosition - CommandPosition;
        var direction = Math.Sign(remaining);
        var stoppingDistance = (_profileVelocity * _profileVelocity) / (2.0 * Math.Max(maximumAcceleration, 1e-6));
        var desiredVelocity = Math.Abs(remaining) <= stoppingDistance ? 0.0 : direction * maximumVelocity;
        var maxDeltaV = maximumAcceleration * dt;
        _profileVelocity += Math.Clamp(desiredVelocity - _profileVelocity, -maxDeltaV, maxDeltaV);
        if (Math.Abs(remaining) < Math.Abs(_profileVelocity * dt))
        {
            CommandPosition = TargetPosition;
            _profileVelocity = 0;
        }
        else
        {
            CommandPosition += _profileVelocity * dt;
        }

        if (Frozen)
        {
            ActualVelocity = 0;
            return;
        }

        var effort = _controller.Step(CommandPosition, ActualPosition, dt);
        if (ForceFollowingError) effort *= 0.02;
        var acceleration = effort - 4.0 * ActualVelocity;
        ActualVelocity += acceleration * dt;

        var nextPosition = ActualPosition + ActualVelocity * dt;
        var clampedPosition = Math.Clamp(nextPosition, Minimum, Maximum);

        var hitLowerLimit =
            clampedPosition <= Minimum && ActualVelocity < 0;

        var hitUpperLimit =
            clampedPosition >= Maximum && ActualVelocity > 0;

        ActualPosition = clampedPosition;

        if (hitLowerLimit || hitUpperLimit)
        {
            ActualVelocity = 0;

            if (Math.Abs(TargetPosition - ActualPosition) <= 0.01)
            {
                CommandPosition = TargetPosition;
                _profileVelocity = 0;
                _controller.Reset();
            }
        }
    }

    public bool AtTarget(double tolerance = 0.01) =>
        Math.Abs(TargetPosition - ActualPosition) <= tolerance && Math.Abs(ActualVelocity) <= 0.04;

    public AxisSnapshot Snapshot(AxisMotionState requestedState)
    {
        var followingError = CommandPosition - ActualPosition;
        var state = Faulted ? AxisMotionState.Faulted : !Enabled ? AxisMotionState.Disabled : requestedState;
        return new AxisSnapshot(Name, CommandPosition, ActualPosition, ActualVelocity, followingError, TargetPosition,
            Enabled, Homed, Math.Abs(ActualVelocity) > 0.02, Faulted, state);
    }
}
