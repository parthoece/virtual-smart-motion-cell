using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("VSMC_MES_URLS") ?? "http://localhost:8090");
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<MesState>();
var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (MesState state) => state.Offline
    ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
    : Results.Ok(new { status = "ready", queuedOrders = state.QueuedOrders, results = state.ResultCount }));

app.MapPost("/api/v1/orders", (MesOrderRequest order, MesState state) =>
{
    if (string.IsNullOrWhiteSpace(order.OrderId) || order.Quantity <= 0) return Results.BadRequest(new { error = "OrderId and positive Quantity are required." });
    if (!state.TryAddOrder(order)) return Results.Conflict(new { error = "Order already exists." });
    return Results.Created($"/api/v1/orders/{order.OrderId}", order);
});

app.MapGet("/api/v1/orders", (MesState state) => Results.Ok(state.GetOrders()));
app.MapGet("/api/v1/orders/next", async (string machineId, MesState state, CancellationToken cancellationToken) =>
{
    await state.ApplyDelayAsync(cancellationToken).ConfigureAwait(false);
    if (state.Offline) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    return state.TryAssign(machineId, out var order) ? Results.Ok(order) : Results.NoContent();
});

app.MapPost("/api/v1/results", async (HttpContext context, MesResultEnvelope result, MesState state, CancellationToken cancellationToken) =>
{
    await state.ApplyDelayAsync(cancellationToken).ConfigureAwait(false);
    if (state.Offline) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? result.IdempotencyKey;
    if (string.IsNullOrWhiteSpace(idempotencyKey)) return Results.BadRequest(new { error = "Idempotency-Key is required." });
    if (!state.TryAddResult(idempotencyKey, result)) return Results.Conflict(new { status = "duplicate", idempotencyKey });
    return Results.Accepted(value: new { status = "accepted", idempotencyKey, duplicateEcho = state.DuplicateResponseMode });
});

app.MapGet("/api/v1/results", (MesState state) => Results.Ok(state.GetResults()));
app.MapPost("/admin/state", (MesAdminRequest request, MesState state) =>
{
    state.Offline = request.Offline;
    state.DelayMilliseconds = Math.Clamp(request.DelayMilliseconds, 0, 30000);
    state.DuplicateResponseMode = request.DuplicateResponseMode;
    return Results.Ok(new { state.Offline, state.DelayMilliseconds, state.DuplicateResponseMode });
});

app.MapGet("/", () => Results.Content("""
<!doctype html><html><head><title>VSMC MES Simulator</title><style>body{font:16px system-ui;margin:2rem;max-width:70rem}code{background:#eee;padding:.2rem}.card{border:1px solid #ccc;border-radius:10px;padding:1rem;margin:1rem 0}</style></head>
<body><h1>Virtual Smart Motion Cell — MES Simulator</h1><div class=card><p>POST orders to <code>/api/v1/orders</code>, poll <code>/api/v1/orders/next</code>, and receive idempotent results at <code>/api/v1/results</code>.</p></div><div class=card><p>Use <code>/admin/state</code> to simulate outages, latency, and duplicate-response behavior.</p></div></body></html>
""", "text/html"));

app.Run();

public sealed record MesOrderRequest(string OrderId, int Quantity, string RecipeId = "standard-widget", int RecipeRevision = 1);
public sealed record MesOrder(
    string OrderId,
    int Quantity,
    int CompletedQuantity,
    string RecipeId,
    int RecipeRevision,
    string Status,
    string? AssignedMachine,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? CompletedAt);
public sealed record MesResultEnvelope(string MachineId, string MessageId, string IdempotencyKey, JsonElement MachineEvent);
public sealed record MesAdminRequest(bool Offline, int DelayMilliseconds, bool DuplicateResponseMode);

public sealed class MesState
{
    private readonly ConcurrentDictionary<string, MesOrder> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MesResultEnvelope> _results = new(StringComparer.OrdinalIgnoreCase);
    public bool Offline { get; set; }
    public int DelayMilliseconds { get; set; }
    public bool DuplicateResponseMode { get; set; }
    public int QueuedOrders => _orders.Values.Count(order => order.Status == "queued");
    public int ResultCount => _results.Count;

    public bool TryAddOrder(MesOrderRequest request) => _orders.TryAdd(request.OrderId,
        new MesOrder(request.OrderId, request.Quantity, 0, request.RecipeId, request.RecipeRevision, "queued", null, DateTimeOffset.UtcNow, null, null));

    public bool TryAssign(string machineId, out MesOrder? assigned)
    {
        foreach (var pair in _orders.OrderBy(pair => pair.Value.CreatedAt))
        {
            if (pair.Value.Status != "queued") continue;
            var updated = pair.Value with { Status = "assigned", AssignedMachine = machineId, AssignedAt = DateTimeOffset.UtcNow };
            if (_orders.TryUpdate(pair.Key, updated, pair.Value)) { assigned = updated; return true; }
        }
        assigned = null;
        return false;
    }

    public bool TryAddResult(string key, MesResultEnvelope result)
    {
        if (!_results.TryAdd(key, result)) return false;
        UpdateOrderProgress(result);
        return true;
    }

    private void UpdateOrderProgress(MesResultEnvelope result)
    {
        if (!result.MachineEvent.TryGetProperty("eventType", out var eventType) ||
            !string.Equals(eventType.GetString(), "cycle.completed", StringComparison.OrdinalIgnoreCase) ||
            !result.MachineEvent.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("orderId", out var orderIdElement)) return;
        var orderId = orderIdElement.GetString();
        if (string.IsNullOrWhiteSpace(orderId)) return;

        while (_orders.TryGetValue(orderId, out var current))
        {
            var completed = Math.Min(current.Quantity, current.CompletedQuantity + 1);
            var updated = current with
            {
                CompletedQuantity = completed,
                Status = completed >= current.Quantity ? "completed" : current.Status,
                CompletedAt = completed >= current.Quantity ? DateTimeOffset.UtcNow : current.CompletedAt
            };
            if (_orders.TryUpdate(orderId, updated, current)) return;
        }
    }
    public IReadOnlyList<MesOrder> GetOrders() => _orders.Values.OrderByDescending(order => order.CreatedAt).ToArray();
    public IReadOnlyDictionary<string, MesResultEnvelope> GetResults() => new Dictionary<string, MesResultEnvelope>(_results);
    public Task ApplyDelayAsync(CancellationToken cancellationToken) => DelayMilliseconds <= 0 ? Task.CompletedTask : Task.Delay(DelayMilliseconds, cancellationToken);
}
