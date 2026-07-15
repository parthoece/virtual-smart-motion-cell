using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Control;
using VirtualSmartMotionCell.Domain;

var targetCycles = ReadInt(args, "--cycles", 10_000);
var durationMinutes = ReadDouble(args, "--duration-minutes", 0);
var output = ReadString(args, "--output", "artifacts/reliability/report.json");
const double dt = 0.01;

IMotionSystem motion = new FaultInjectingMotionSystem(new SimulatedMotionSystem());
var metrics = new RuntimeMetrics();
var integration = new IntegrationStatusStore();
var machine = new MachineCoordinator(MachineRecipe.Default, motion, metrics, integration);
var transitions = new StringBuilder();
var maximumFollowingError = 0.0;
var ticks = 0L;
var deadlineMisses = 0L;
var maximumStepMilliseconds = 0.0;
var orderBatch = 0;
var completedOrders = 0;

Execute(machine, "initialize");
StepUntil(machine, state => state.ExecutionState == ExecutionState.Stopped, 100);
Execute(machine, "home");
StepUntil(machine, state => state.ExecutionState == ExecutionState.Ready, 5_000);
LoadAndStartNextOrder(machine, targetCycles, ref orderBatch);

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
var process = Process.GetCurrentProcess();
var workingSetBefore = process.WorkingSet64;
var stopwatch = Stopwatch.StartNew();
var durationTarget = durationMinutes > 0 ? TimeSpan.FromMinutes(durationMinutes) : TimeSpan.Zero;
var durationTicks = durationTarget > TimeSpan.Zero
    ? (long)Math.Ceiling(durationTarget.TotalSeconds / dt * 1.25) + 10_000
    : 0;
var maximumTicks = Math.Max(Math.Max(200_000L, (long)targetCycles * 20_000L), durationTicks);
MachineStateSnapshot snapshot;

do
{
    var stepTimer = Stopwatch.StartNew();
    snapshot = machine.Step(dt);
    stepTimer.Stop();
    ticks++;

    maximumStepMilliseconds = Math.Max(maximumStepMilliseconds, stepTimer.Elapsed.TotalMilliseconds);
    if (stepTimer.Elapsed.TotalMilliseconds > dt * 1_000) deadlineMisses++;
    maximumFollowingError = Math.Max(
        maximumFollowingError,
        Math.Max(Math.Abs(snapshot.XAxis.FollowingError), Math.Abs(snapshot.YAxis.FollowingError)));

    foreach (var machineEvent in machine.DrainEvents())
    {
        if (machineEvent.EventType == "machine.transition")
            transitions.Append(machineEvent.MachineRevision).Append(':').Append(machineEvent.EventType).Append(':').Append(machineEvent.Payload).Append('\n');
    }

    if (snapshot.ExecutionState == ExecutionState.Faulted)
        throw new InvalidOperationException($"Reliability campaign faulted: {string.Join("; ", snapshot.ActiveAlarms.Select(alarm => alarm.Code))}");

    var activeOrder = snapshot.Production.ActiveOrder;
    if (activeOrder?.Status == ProductionOrderStatus.Completed && snapshot.ExecutionState == ExecutionState.Ready)
    {
        completedOrders++;
        var mustContinue = snapshot.Production.CycleCount < targetCycles ||
            (durationTarget > TimeSpan.Zero && stopwatch.Elapsed < durationTarget);
        if (mustContinue)
        {
            var remaining = Math.Max(0, targetCycles - (int)Math.Min(int.MaxValue, snapshot.Production.CycleCount));
            LoadAndStartNextOrder(machine, remaining, ref orderBatch);
        }
    }

    if (durationTarget > TimeSpan.Zero)
    {
        var expectedElapsed = TimeSpan.FromSeconds(ticks * dt);
        var delay = expectedElapsed - stopwatch.Elapsed;
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
    }

    if (ticks > maximumTicks)
        throw new TimeoutException($"Campaign exceeded {maximumTicks} ticks.");
}
while (snapshot.Production.CycleCount < targetCycles ||
       (durationTarget > TimeSpan.Zero && stopwatch.Elapsed < durationTarget));

stopwatch.Stop();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
process.Refresh();
var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
var transitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transitions.ToString()))).ToLowerInvariant();
var report = new
{
    schemaVersion = 3,
    generatedAt = DateTimeOffset.UtcNow,
    environment = new
    {
        os = Environment.OSVersion.ToString(),
        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
    },
    targetCycles,
    requestedDurationMinutes = durationMinutes,
    completedCycles = snapshot.Production.CycleCount,
    completedOrders,
    goodParts = snapshot.Production.GoodCount,
    rejectParts = snapshot.Production.RejectCount,
    simulatedSeconds = snapshot.SimulationTime,
    wallClockSeconds = stopwatch.Elapsed.TotalSeconds,
    realTimeRatio = stopwatch.Elapsed.TotalSeconds > 0 ? snapshot.SimulationTime / stopwatch.Elapsed.TotalSeconds : 0,
    ticks,
    maximumFollowingError,
    maximumStepMilliseconds,
    measuredDeadlineMisses = deadlineMisses,
    memoryBeforeBytes = memoryBefore,
    memoryAfterBytes = memoryAfter,
    managedMemoryDeltaBytes = memoryAfter - memoryBefore,
    workingSetBeforeBytes = workingSetBefore,
    workingSetAfterBytes = process.WorkingSet64,
    transitionHash,
    finalState = new { snapshot.Mode, snapshot.ExecutionState, snapshot.ProductionStep, snapshot.Revision, snapshot.Production.Oee }
};

var outputPath = Path.GetFullPath(output);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Completed {snapshot.Production.CycleCount} virtual cycles in {stopwatch.Elapsed.TotalSeconds:F2} seconds. Report: {outputPath}");
return 0;

static void LoadAndStartNextOrder(MachineCoordinator machine, int remainingCycles, ref int orderBatch)
{
    orderBatch++;
    var quantity = remainingCycles > 0
        ? Math.Min(100_000, remainingCycles)
        : 1_000;
    Execute(machine, "load-order", orderId: $"RELIABILITY-{orderBatch:D4}", quantity: quantity, recipeId: "standard-widget", recipeRevision: 1);
    Execute(machine, "set-mode", mode: "Automatic");
    Execute(machine, "start");
}

static void Execute(
    MachineCoordinator machine,
    string type,
    string? mode = null,
    string? orderId = null,
    int? quantity = null,
    string? recipeId = null,
    int? recipeRevision = null)
{
    var id = Guid.NewGuid().ToString("N");
    var result = machine.Execute(id, new MachineCommandRequest(
        type,
        Mode: mode,
        CorrelationId: "reliability-campaign",
        OrderId: orderId,
        Quantity: quantity,
        RecipeId: recipeId,
        RecipeRevision: recipeRevision));
    if (result.Status != CommandStatus.Accepted)
        throw new InvalidOperationException($"Command {type} rejected: {result.ReasonCode} {string.Join("; ", result.Reasons)}");
}

static MachineStateSnapshot StepUntil(
    MachineCoordinator machine,
    Func<MachineStateSnapshot, bool> predicate,
    int maximumTicks)
{
    var snapshot = machine.Snapshot();
    for (var index = 0; index < maximumTicks; index++)
    {
        snapshot = machine.Step(0.01);
        machine.DrainEvents();
        if (predicate(snapshot)) return snapshot;
    }
    throw new TimeoutException($"Expected condition was not reached. State={snapshot.ExecutionState}/{snapshot.ProductionStep}");
}

static int ReadInt(string[] arguments, string name, int fallback)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length && int.TryParse(arguments[index + 1], out var value) && value > 0 ? value : fallback;
}

static double ReadDouble(string[] arguments, string name, double fallback)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length && double.TryParse(arguments[index + 1], out var value) && value >= 0 ? value : fallback;
}

static string ReadString(string[] arguments, string name, string fallback)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
}
