using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Control;

public sealed class SimulatedInspectionAdapter : IInspectionAdapter
{
    public AdapterDescriptor Descriptor { get; } = new(
        "inspection.deterministic", "Deterministic simulated inspection", new Version(1, 0, 0),
        new[] { "inspection", "deterministic", "quality" });

    public ValueTask InitializeAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    public ValueTask<AdapterHealth> CheckHealthAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new AdapterHealth(true, "ready", DateTimeOffset.UtcNow, new Dictionary<string, string>())); }

    public ValueTask<(PartQuality Quality, double Score)> InspectAsync(string partId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var score = 0.95 + (Math.Abs(partId.GetHashCode(StringComparison.Ordinal)) % 40) / 1000.0;
        return ValueTask.FromResult((score >= 0.92 ? PartQuality.Good : PartQuality.Reject, score));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
