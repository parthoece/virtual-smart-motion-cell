using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Control;
using VirtualSmartMotionCell.Domain;

var tests = new List<(string Name, Action Test)>
{
    ("PID output is bounded", TestPid),
    ("Recipe validates and checksums", TestRecipe),
    ("Unsafe start explains rejection", TestUnsafeStart),
    ("Order mismatch is rejected", TestOrderRecipeMismatch),
    ("Normal order completes", TestNormalOrder),
    ("Pause and resume preserve operation", TestPauseResume),
    ("Controlled stop returns ready", TestControlledStop),
    ("Abort disables motion and stops", TestAbort),
    ("Guard fault enters faulted state", () => TestFault("guard-open")),
    ("Bus fault enters faulted state", () => TestFault("bus-loss")),
    ("Drive fault enters faulted state", () => TestFault("drive-fault-y")),
    ("Following error is detected", () => TestFault("following-error")),
    ("Frozen axis is detected", () => TestFault("frozen-axis-x")),
    ("Illegal mode change while running is rejected", TestModeChange),
    ("Maintenance mode permits jog", TestMaintenanceJog),
    ("Manual jog requires homing", TestJogRequiresHoming),
    ("Alarm acknowledgement and reset lifecycle", TestAlarmLifecycle),
    ("Snapshots are monotonic", TestRevision),
    ("Interrupted checkpoint supports discard recovery", TestRecoveryDiscard),
    ("Interrupted checkpoint supports rehome recovery", TestRecoveryRehome),
    ("Interrupted checkpoint supports simulation resume", TestRecoveryResume),
    ("Empty commands are rejected", TestEmptyCommand),
    ("Bounded command queue rejects overflow", TestCommandQueue),
    ("Replay adapter advances deterministically", TestReplayAdapter),
    ("Fault decorator reports degraded health", TestFaultDecorator)
};

var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures.Add($"{name}: {exception.Message}"); Console.WriteLine($"FAIL {name}: {exception.Message}"); }
}
Console.WriteLine($"{tests.Count - failures.Count}/{tests.Count} specifications passed");
if (failures.Count > 0) Console.WriteLine(string.Join(Environment.NewLine, failures));
return failures.Count == 0 ? 0 : 1;

static (MachineCoordinator Coordinator, IMotionSystem Motion) CreateCoordinator()
{
    IMotionSystem motion = new FaultInjectingMotionSystem(new SimulatedMotionSystem());
    var integration = new IntegrationStatusStore();
    integration.ConfigureOpcUa(true, "opc.tcp://localhost:4840/vsmc");
    return (new MachineCoordinator(MachineRecipe.Default, motion, new RuntimeMetrics(), integration), motion);
}

static void TestPid()
{
    var pid = new PidController(100, 10, 0, 2, 1);
    Assert(Math.Abs(pid.Step(10, 0, 0.01)) <= 2.0, "PID output exceeded configured limit.");
    AssertThrows<ArgumentOutOfRangeException>(() => pid.Step(1, 0, 0), "PID must reject a non-positive period.");
}

static void TestRecipe()
{
    var recipe = MachineRecipe.Default;
    Assert(recipe.Validate().Count == 0, "Default recipe must be valid.");
    Assert(recipe.Checksum.Length == 64, "Recipe checksum must be SHA-256 hex.");
    var invalid = recipe with { Motion = recipe.Motion with { MaximumVelocity = -1 } };
    Assert(invalid.Validate().Count > 0, "Invalid motion values must reject.");
}

static void TestUnsafeStart()
{
    var (c, _) = CreateCoordinator();
    var result = c.Execute("1", new MachineCommandRequest("start"));
    Assert(result.Status == CommandStatus.Rejected, "Start must reject before preparation.");
    Assert(result.ReasonCode == "START_CONDITIONS_NOT_MET" && result.Reasons.Count >= 3, "Rejected start must explain unmet conditions.");
}

static void TestOrderRecipeMismatch()
{
    var (c, _) = CreateCoordinator();
    InitializeAndHome(c);
    var result = c.Execute("order", new MachineCommandRequest("load-order", OrderId: "O-1", Quantity: 1, RecipeId: "other", RecipeRevision: 1));
    Assert(result.ReasonCode == "RECIPE_MISMATCH", "Order recipe mismatch must reject explicitly.");
}

static void TestNormalOrder()
{
    var (c, _) = CreateCoordinator();
    PrepareAutomatic(c, quantity: 2);
    Assert(Accept(c, "start").Status == CommandStatus.Accepted, "Start should be accepted after preparation.");
    var snapshot = StepUntil(c, state => state.Production.ActiveOrder?.Status == ProductionOrderStatus.Completed, 20_000);
    Assert(snapshot.Production.CycleCount == 2 && snapshot.Production.GoodCount == 2, "Two good parts must complete the order.");
    Assert(snapshot.ExecutionState == ExecutionState.Ready, "Completed order must leave the machine Ready.");
}

static void TestPauseResume()
{
    var (c, _) = CreateCoordinator(); PrepareAutomatic(c, 2); Accept(c, "start");
    StepUntil(c, state => state.ExecutionState == ExecutionState.Running && state.ProductionStep == ProductionStep.MoveToInspect, 8_000);
    Accept(c, "pause");
    var paused = StepUntil(c, state => state.ExecutionState == ExecutionState.Paused, 2_000);
    Assert(paused.Production.ActiveOrder?.Status == ProductionOrderStatus.Paused, "Order must pause with machine.");
    Accept(c, "resume");
    Assert(StepUntil(c, state => state.Production.CycleCount >= 1, 12_000).Production.CycleCount >= 1, "Cycle must continue after resume.");
}

static void TestControlledStop()
{
    var (c, _) = CreateCoordinator(); PrepareAutomatic(c, 2); Accept(c, "start"); c.Step(0.01);
    Accept(c, "stop");
    var stopped = StepUntil(c, state => state.ExecutionState == ExecutionState.Ready, 2_000);
    Assert(stopped.Production.ActiveOrder?.Status == ProductionOrderStatus.Paused, "Controlled stop must pause the order.");
}

static void TestAbort()
{
    var (c, _) = CreateCoordinator(); PrepareAutomatic(c, 2); Accept(c, "start"); c.Step(0.01);
    Accept(c, "abort");
    var stopped = StepUntil(c, state => state.ExecutionState == ExecutionState.Stopped, 100);
    Assert(!stopped.XAxis.Enabled && !stopped.YAxis.Enabled, "Abort must disable both axes.");
}

static void TestFault(string fault)
{
    var (c, _) = CreateCoordinator(); PrepareAutomatic(c, 2); Accept(c, "start"); c.Step(0.01);
    Accept(c, "inject-fault", fault: fault);
    var faulted = StepUntil(c, state => state.ExecutionState == ExecutionState.Faulted, 500);
    Assert(faulted.ActiveAlarms.Count > 0, $"{fault} must create an active alarm.");
}

static void TestModeChange()
{
    var (c, _) = CreateCoordinator(); PrepareAutomatic(c, 2); Accept(c, "start"); c.Step(0.01);
    var result = c.Execute("mode", new MachineCommandRequest("set-mode", Mode: "Manual"));
    Assert(result.Status == CommandStatus.Rejected && result.ReasonCode == "MODE_CHANGE_BLOCKED", "Mode change while running must reject.");
}

static void TestMaintenanceJog()
{
    var (c, _) = CreateCoordinator(); InitializeAndHome(c);
    Accept(c, "set-mode", mode: "Maintenance");
    var before = c.Snapshot().XAxis.ActualPosition;
    Accept(c, "jog", axis: "X", value: 0.1);
    var after = StepUntil(c, state => Math.Abs(state.XAxis.ActualPosition - before) > 0.05, 2_000);
    Assert(after.Mode == MachineMode.Maintenance, "Maintenance jog must retain Maintenance mode.");
}

static void TestJogRequiresHoming()
{
    var (c, _) = CreateCoordinator();
    Accept(c, "initialize"); c.Step(0.01);
    var result = c.Execute("jog", new MachineCommandRequest("jog", Axis: "X", Value: 0.1));
    Assert(result.ReasonCode == "AXIS_NOT_HOMED", "Unhomed jog must reject.");
}

static void TestAlarmLifecycle()
{
    var (c, _) = CreateCoordinator(); InitializeAndHome(c);
    Accept(c, "inject-fault", fault: "guard-open"); c.Step(0.01);
    Accept(c, "acknowledge-alarms");
    Assert(c.Snapshot().ActiveAlarms.All(a => a.Lifecycle == AlarmLifecycle.ActiveAcknowledged), "Alarm must become acknowledged.");
    Accept(c, "clear-fault", fault: "all");
    Accept(c, "reset");
    Assert(c.Snapshot().ExecutionState is ExecutionState.Ready or ExecutionState.Stopped, "Reset must return to a safe non-fault state.");
}

static void TestRecoveryDiscard()
{
    var (c, _) = CreateCoordinator(); c.RestoreCheckpoint(Checkpoint());
    Assert(c.Snapshot().ExecutionState == ExecutionState.RecoveryRequired, "Checkpoint must require recovery.");
    Accept(c, "recover-discard");
    Assert(c.Snapshot().ExecutionState == ExecutionState.Stopped && c.Snapshot().Production.RejectCount == 1, "Discard recovery must reject interrupted part and stop.");
}

static void TestRecoveryRehome()
{
    var (c, _) = CreateCoordinator(); c.RestoreCheckpoint(Checkpoint());
    Accept(c, "recover-rehome");
    var ready = StepUntil(c, state => state.ExecutionState == ExecutionState.Ready, 5_000);
    Assert(ready.XAxis.Homed && ready.YAxis.Homed, "Rehome recovery must finish homed.");
}

static void TestRecoveryResume()
{
    var (c, _) = CreateCoordinator(); c.RestoreCheckpoint(Checkpoint());
    Accept(c, "recover-resume");
    var resumed = c.Snapshot();
    Assert(resumed.ExecutionState == ExecutionState.Running && resumed.ActivePart is not null, "Simulation recovery must restore running part state.");
}

static RecoveryCheckpoint Checkpoint() => new(
    DateTimeOffset.UtcNow, 10, MachineMode.Automatic, ExecutionState.Running, ProductionStep.MoveToInspect,
    "ORDER-42-PART-000001", "ORDER-42", 2, 0, "standard-widget", 1, true,
    0.4, 0.3, 0.2, 0.9, 1.25, "process terminated");

static void TestEmptyCommand()
{
    var (c, _) = CreateCoordinator();
    var result = c.Execute("empty", new MachineCommandRequest(null!));
    Assert(result.Status == CommandStatus.Rejected && result.ReasonCode == "UNKNOWN_COMMAND", "Empty command types must reject safely.");
}

static void TestCommandQueue()
{
    var metrics = new RuntimeMetrics();
    var bus = new MachineCommandBus(metrics, capacity: 1);
    _ = bus.SendAsync(new MachineCommandRequest("initialize"), CancellationToken.None);
    var overflow = bus.SendAsync(new MachineCommandRequest("home"), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    Assert(overflow.Status == CommandStatus.Rejected && overflow.ReasonCode == "COMMAND_QUEUE_FULL", "Queue overflow must reject explicitly.");
}

static void TestReplayAdapter()
{
    var first = Axis("X", 0.0); var second = Axis("X", 0.5);
    var y = Axis("Y", 0.1);
    var replay = new ReplayMotionSystem(new[] { new MotionReplayFrame(0, first, y), new MotionReplayFrame(.1, second, y) });
    Assert(replay.Snapshot(AxisMotionState.Holding).XAxis.ActualPosition == 0, "Replay must begin at first frame.");
    replay.Step(.1, 1, 1);
    Assert(replay.Snapshot(AxisMotionState.Holding).XAxis.ActualPosition == .5, "Replay must advance deterministically.");
}

static void TestFaultDecorator()
{
    var adapter = new FaultInjectingMotionSystem(new SimulatedMotionSystem());
    adapter.InjectFault(MotionFault.DriveFaultX);
    var health = adapter.CheckHealthAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    Assert(!health.Healthy && health.Status.Contains("DriveFaultX", StringComparison.Ordinal), "Fault decorator must expose degraded health.");
}

static AxisSnapshot Axis(string name, double position) => new(name, position, position, 0, 0, position, true, true, false, false, AxisMotionState.Holding);

static void PrepareAutomatic(MachineCoordinator c, int quantity)
{
    InitializeAndHome(c);
    Accept(c, "load-order", orderId: "ORDER-001", quantity: quantity, recipeId: "standard-widget", recipeRevision: 1);
    Accept(c, "set-mode", mode: "Automatic");
}

static void InitializeAndHome(MachineCoordinator c)
{
    Accept(c, "initialize");
    StepUntil(c, state => state.ExecutionState == ExecutionState.Stopped, 100);
    Accept(c, "home");
    StepUntil(c, state => state.ExecutionState == ExecutionState.Ready, 5_000);
}

static CommandResult Accept(MachineCoordinator c, string type, string? axis = null, double? value = null, string? mode = null, string? fault = null, string? orderId = null, int? quantity = null, string? recipeId = null, int? recipeRevision = null)
{
    var result = c.Execute(Guid.NewGuid().ToString("N"), new MachineCommandRequest(type, Axis: axis, Value: value, Mode: mode, Fault: fault, RequestedBy: "spec", CorrelationId: "spec", OrderId: orderId, Quantity: quantity, RecipeId: recipeId, RecipeRevision: recipeRevision));
    Assert(result.Status == CommandStatus.Accepted, $"Command {type} rejected: {result.ReasonCode} {string.Join("; ", result.Reasons)}");
    return result;
}

static MachineStateSnapshot StepUntil(MachineCoordinator c, Func<MachineStateSnapshot, bool> predicate, int maximumTicks)
{
    var snapshot = c.Snapshot();
    for (var index = 0; index < maximumTicks; index++)
    {
        snapshot = c.Step(0.01);
        if (predicate(snapshot)) return snapshot;
    }
    throw new TimeoutException($"Condition was not reached. State={snapshot.ExecutionState}/{snapshot.ProductionStep}");
}

static void TestRevision()
{
    var (c, _) = CreateCoordinator(); var before = c.Snapshot().Revision;
    Accept(c, "initialize"); c.Step(0.01);
    Assert(c.Snapshot().Revision > before, "Revision must increase after transitions.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}
