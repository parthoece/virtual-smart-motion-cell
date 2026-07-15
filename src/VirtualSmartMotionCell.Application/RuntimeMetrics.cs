namespace VirtualSmartMotionCell.Application;

public sealed class RuntimeMetrics
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private long _deadlineMisses;
    private double _lastLoopMs;
    private double _maxLoopMs;
    private double _targetLoopMs = 10.0;
    private int _connectedClients;
    private int _queueDepth;

    public DateTimeOffset StartedAt => _startedAt;
    public long DeadlineMisses => Interlocked.Read(ref _deadlineMisses);
    public double LastLoopMilliseconds => Volatile.Read(ref _lastLoopMs);
    public double MaximumLoopMilliseconds => Volatile.Read(ref _maxLoopMs);
    public double TargetLoopMilliseconds => Volatile.Read(ref _targetLoopMs);
    public int ConnectedClients => Volatile.Read(ref _connectedClients);
    public int QueueDepth => Volatile.Read(ref _queueDepth);
    public TimeSpan Uptime => DateTimeOffset.UtcNow - _startedAt;

    public void RecordLoop(double elapsedMilliseconds, double targetMilliseconds)
    {
        Volatile.Write(ref _targetLoopMs, targetMilliseconds);
        Volatile.Write(ref _lastLoopMs, elapsedMilliseconds);
        if (elapsedMilliseconds > _maxLoopMs) Volatile.Write(ref _maxLoopMs, elapsedMilliseconds);
        if (elapsedMilliseconds > targetMilliseconds) Interlocked.Increment(ref _deadlineMisses);
    }

    public void SetConnectedClients(int count) => Volatile.Write(ref _connectedClients, count);
    public void SetQueueDepth(int count) => Volatile.Write(ref _queueDepth, count);
}
