using System.Net;
using System.Text;
using System.Text.Json;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Control;
using VirtualSmartMotionCell.Domain;
using VirtualSmartMotionCell.Infrastructure;
using VirtualSmartMotionCell.Runtime;

var tests = new List<(string Name, Func<Task> Test)>
{
    ("Checkpoint round trip", CheckpointRoundTrip),
    ("Outbox is durable and idempotent", OutboxRoundTrip),
    ("File MES delivery is idempotent", FileMesIdempotency),
    ("HTTP MES delivery sends idempotency key", HttpMesIdempotency),
    ("Recipe draft approval activation lifecycle", RecipeLifecycleSpec),
    ("Production repository materializes records", ProductionMaterialization),
    ("Alarm history persists lifecycle", AlarmHistory),
    ("Architecture project references follow boundaries", ArchitectureBoundaries),
    ("State publication drops stale snapshots", StatePublicationDropsStaleSnapshots),
    ("Replay adapter data parses and advances", ReplayAdapterParses),
    ("Solution contains every project", SolutionMembership),
    ("Configuration and recipe JSON parse", ConfigurationParses)
};
var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures.Add($"{name}: {exception.Message}"); Console.WriteLine($"FAIL {name}: {exception.Message}"); }
}
Console.WriteLine($"{tests.Count - failures.Count}/{tests.Count} integration specifications passed");
return failures.Count == 0 ? 0 : 1;

static async Task CheckpointRoundTrip()
{
    using var temp = TempDirectory.Create();
    var store = new FileCheckpointStore(temp.Path);
    var checkpoint = new RecoveryCheckpoint(DateTimeOffset.UtcNow, 42, MachineMode.Automatic, ExecutionState.Running, ProductionStep.MoveToPlace,
        "P-1", "O-1", 10, 3, "standard-widget", 1, true, .1, .2, .3, .4, 12.3, "test");
    await store.SaveAsync(checkpoint, CancellationToken.None);
    var loaded = await store.LoadAsync(CancellationToken.None);
    Equal(checkpoint, loaded, "Checkpoint must round-trip exactly.");
    await store.ClearAsync(CancellationToken.None);
    Assert(await store.LoadAsync(CancellationToken.None) is null, "Cleared checkpoint must disappear.");
}

static async Task OutboxRoundTrip()
{
    using var temp = TempDirectory.Create();
    var store = new FileOutboxStore(temp.Path);
    var message = Event("cycle.completed", new CycleCompletedPayload("O", "P", 1, PartQuality.Good, 1.2, "standard-widget", 1));
    await store.EnqueueAsync(message, CancellationToken.None);
    await store.EnqueueAsync(message, CancellationToken.None);
    Assert(await store.CountPendingAsync(CancellationToken.None) == 1, "Duplicate enqueue must not create duplicate messages.");
    Equal(message.EventId, (await store.ReadPendingAsync(10, CancellationToken.None)).Single().EventId, "Pending message must be readable.");
    await store.MarkDeliveredAsync(message.EventId, CancellationToken.None);
    Assert(await store.CountPendingAsync(CancellationToken.None) == 0, "Delivered message must leave pending queue.");
}

static async Task FileMesIdempotency()
{
    using var temp = TempDirectory.Create();
    var gateway = new FileManufacturingGateway(temp.Path);
    var message = Event("cycle.completed", new { part = "P" });
    Assert(await gateway.DeliverAsync(message, CancellationToken.None), "First delivery must succeed.");
    Assert(await gateway.DeliverAsync(message, CancellationToken.None), "Duplicate delivery must be acknowledged.");
    var lines = await File.ReadAllLinesAsync(System.IO.Path.Combine(temp.Path, "manufacturing-delivery.jsonl"));
    Assert(lines.Length == 1, "Idempotent gateway must persist one receipt.");
}


static async Task HttpMesIdempotency()
{
    var handler = new CapturingHandler(HttpStatusCode.Conflict);
    using var client = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test/") };
    var gateway = new HttpManufacturingGateway(client, "VSMC-TEST");
    var message = Event("cycle.completed", new { part = "P-HTTP" });
    Assert(await gateway.DeliverAsync(message, CancellationToken.None), "HTTP 409 must be treated as an idempotent acknowledgement.");
    Assert(handler.IdempotencyKey == message.EventId,
        "The HTTP gateway must send the machine event ID as Idempotency-Key.");
    Assert(handler.LastBody?.Contains(message.EventId, StringComparison.Ordinal) == true,
        "The delivery envelope must include the event ID.");
}

static async Task RecipeLifecycleSpec()
{
    using var temp = TempDirectory.Create();
    var initialPath = System.IO.Path.Combine(temp.Path, "standard-widget.v1.json");
    await File.WriteAllTextAsync(initialPath, JsonSerializer.Serialize(MachineRecipe.Default with { Status = "approved" }, JsonDefaults.Options));
    var store = new JsonRecipeStore(initialPath);
    var draft = MachineRecipe.Default with { RecipeId = "contributor-recipe", Revision = 2, Status = "draft" };
    await store.SaveDraftAsync(draft, CancellationToken.None);
    var approved = await store.ApproveAsync(draft.RecipeId, draft.Revision, CancellationToken.None);
    Assert(approved.Lifecycle == RecipeLifecycle.Approved, "Recipe must approve.");
    var active = await store.ActivateAsync(draft.RecipeId, draft.Revision, CancellationToken.None);
    Assert(active.Lifecycle == RecipeLifecycle.Active, "Recipe must activate.");
    Assert((await store.LoadActiveAsync(CancellationToken.None)).RecipeId == draft.RecipeId, "Active pointer must select activated recipe.");
}

static async Task ProductionMaterialization()
{
    using var temp = TempDirectory.Create();
    var store = new FileProductionRepository(temp.Path);
    await store.HandleEventAsync(Event("order.loaded", new OrderLoadedPayload("O-9", 2, "standard-widget", 1, ProductionOrderStatus.Queued)), CancellationToken.None);
    await store.HandleEventAsync(Event("order.status", new OrderStatusChangedPayload("O-9", ProductionOrderStatus.Active, 0, "start")), CancellationToken.None);
    await store.HandleEventAsync(Event("cycle.completed", new CycleCompletedPayload("O-9", "P-9", 1, PartQuality.Good, 4.2, "standard-widget", 1)), CancellationToken.None);
    Assert((await store.ReadOrdersAsync(10, CancellationToken.None)).Single().OrderId == "O-9", "Order journal must materialize.");
    Assert((await store.ReadPartsAsync(10, CancellationToken.None)).Single().PartId == "P-9", "Part record must persist.");
    Assert((await store.ReadCyclesAsync(10, CancellationToken.None)).Single().DurationSeconds == 4.2, "Cycle record must persist.");
    Assert((await store.ReadTraceabilityAsync(10, CancellationToken.None)).Count >= 1, "Traceability must persist.");
}

static async Task AlarmHistory()
{
    using var temp = TempDirectory.Create();
    var store = new FileAlarmHistoryStore(temp.Path);
    var alarm = new AlarmSnapshot("A1", "Motion", AlarmSeverity.Error, AlarmLifecycle.ActiveUnacknowledged, "Fault", "Reset", DateTimeOffset.UtcNow, null, null, null, "corr");
    await store.AppendAsync(alarm, "raised", CancellationToken.None);
    var history = await store.ReadAsync(10, CancellationToken.None);
    Assert(history.Single().Alarm.Code == "A1" && history.Single().Action == "raised", "Alarm history must round-trip.");
}

static Task ArchitectureBoundaries()
{
    var root = FindRepositoryRoot();
    var domain = File.ReadAllText(System.IO.Path.Combine(root, "src", "VirtualSmartMotionCell.Domain", "VirtualSmartMotionCell.Domain.csproj"));
    var application = File.ReadAllText(System.IO.Path.Combine(root, "src", "VirtualSmartMotionCell.Application", "VirtualSmartMotionCell.Application.csproj"));
    var control = File.ReadAllText(System.IO.Path.Combine(root, "src", "VirtualSmartMotionCell.Control", "VirtualSmartMotionCell.Control.csproj"));
    Assert(!domain.Contains("Infrastructure", StringComparison.Ordinal) && !domain.Contains("Avalonia", StringComparison.Ordinal), "Domain must not depend on infrastructure or UI.");
    Assert(!application.Contains("Infrastructure", StringComparison.Ordinal) && !application.Contains("Avalonia", StringComparison.Ordinal), "Application must not depend on infrastructure or UI.");
    Assert(!control.Contains("Infrastructure", StringComparison.Ordinal), "Control must not depend on infrastructure.");
    return Task.CompletedTask;
}


static async Task StatePublicationDropsStaleSnapshots()
{
    var machine = new MachineCoordinator(MachineRecipe.Default, new FaultInjectingMotionSystem(new SimulatedMotionSystem()), new RuntimeMetrics(), new IntegrationStatusStore());
    var first = machine.Snapshot();
    machine.Execute("initialize", new MachineCommandRequest("initialize", CorrelationId: "queue-spec"));
    var second = machine.Snapshot();
    var queue = new StatePublicationQueue();
    queue.Publish(first);
    queue.Publish(second);
    var received = await queue.Reader.ReadAsync(CancellationToken.None);
    Assert(received.Revision == second.Revision, "A slow client must receive the newest snapshot rather than stale state.");
}

static Task ReplayAdapterParses()
{
    var root = FindRepositoryRoot();
    var path = System.IO.Path.Combine(root, "config", "replay", "motion-replay.json");
    var frames = JsonSerializer.Deserialize<MotionReplayFrame[]>(File.ReadAllText(path), JsonDefaults.Options);
    Assert(frames is { Length: > 2 }, "Replay data must contain multiple frames.");
    var replay = new ReplayMotionSystem(frames!);
    var before = replay.Snapshot(AxisMotionState.Holding);
    replay.Step(0.01, 1, 1);
    var after = replay.Snapshot(AxisMotionState.Holding);
    Assert(after.XAxis.ActualPosition != before.XAxis.ActualPosition || after.YAxis.ActualPosition != before.YAxis.ActualPosition,
        "Replay adapter must advance through recorded frames.");
    return Task.CompletedTask;
}

static Task SolutionMembership()
{
    var root = FindRepositoryRoot();
    var solution = File.ReadAllText(System.IO.Path.Combine(root, "VirtualSmartMotionCell.sln"))
        .Replace('\\', '/');

    var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}node_modules{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Select(path => System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'))
        .ToArray();
    foreach (var project in projects)
        Assert(solution.Contains(project, StringComparison.OrdinalIgnoreCase), $"Solution is missing {project}.");
    return Task.CompletedTask;
}

static Task ConfigurationParses()
{
    var root = FindRepositoryRoot();
    using var runtime = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, "config", "runtime.json")));
    using var recipe = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, "config", "recipes", "standard-widget.v1.json")));
    Assert(runtime.RootElement.TryGetProperty("OpcUa", out _) && runtime.RootElement.TryGetProperty("Mes", out _), "Runtime config must include OPC UA and MES.");
    Assert(recipe.RootElement.GetProperty("status").GetString() is "approved" or "active", "Bundled recipe must be activatable.");
    return Task.CompletedTask;
}

static MachineEvent Event(string type, object payload) => new(Guid.NewGuid().ToString("N"), type, DateTimeOffset.UtcNow, "integration-spec", 1, payload);
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException(message); }

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(System.IO.Path.Combine(directory.FullName, "VirtualSmartMotionCell.sln"))) return directory.FullName;
        directory = directory.Parent;
    }
    directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        if (File.Exists(System.IO.Path.Combine(directory.FullName, "VirtualSmartMotionCell.sln"))) return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path) { Path = path; }
    public string Path { get; }
    public static TempDirectory Create() { var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vsmc-spec-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return new TempDirectory(path); }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}


sealed class CapturingHandler(HttpStatusCode responseStatus) : HttpMessageHandler
{
    public string? IdempotencyKey { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.SingleOrDefault() : null;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(responseStatus)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
    }
}
