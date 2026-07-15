using VirtualSmartMotionCell.AdapterSdk;

await using var adapter = new SimulatedBarcodeAdapter();
await adapter.InitializeAsync(CancellationToken.None);
var health = await adapter.CheckHealthAsync(CancellationToken.None);
Console.WriteLine($"{adapter.Descriptor.DisplayName}: {health.Status}");

public sealed class SimulatedBarcodeAdapter : IEquipmentAdapter
{
    public AdapterDescriptor Descriptor { get; } = new(
        "sample.simulated-barcode", "Simulated barcode reader", new Version(1, 0, 0),
        new[] { "barcode.read", "health" });

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<AdapterHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AdapterHealth(true, "Ready", DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["mode"] = "simulation" }));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
