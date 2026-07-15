namespace VirtualSmartMotionCell.Control;

public sealed class PidController
{
    private double _integral;
    private double _previousError;
    private bool _hasPrevious;

    public PidController(double kp, double ki, double kd, double outputLimit, double integralLimit)
    {
        Kp = kp;
        Ki = ki;
        Kd = kd;
        OutputLimit = Math.Abs(outputLimit);
        IntegralLimit = Math.Abs(integralLimit);
    }

    public double Kp { get; }
    public double Ki { get; }
    public double Kd { get; }
    public double OutputLimit { get; }
    public double IntegralLimit { get; }

    public double Step(double setpoint, double measurement, double dt)
    {
        if (dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        var error = setpoint - measurement;
        _integral = Math.Clamp(_integral + error * dt, -IntegralLimit, IntegralLimit);
        var derivative = _hasPrevious ? (error - _previousError) / dt : 0.0;
        _previousError = error;
        _hasPrevious = true;
        return Math.Clamp(Kp * error + Ki * _integral + Kd * derivative, -OutputLimit, OutputLimit);
    }

    public void Reset()
    {
        _integral = 0;
        _previousError = 0;
        _hasPrevious = false;
    }
}
