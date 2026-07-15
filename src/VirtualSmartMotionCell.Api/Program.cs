using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VirtualSmartMotionCell.Api;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Domain;
using VirtualSmartMotionCell.OpcUa;
using VirtualSmartMotionCell.Runtime;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("config/runtime.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables(prefix: "VSMC_");
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("VSMC_URLS") ?? "http://localhost:8080");
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<WebSocketStatePublisher>();
builder.Services.AddSingleton<IMachineStatePublisher>(services => services.GetRequiredService<WebSocketStatePublisher>());
builder.Services.AddVirtualSmartMotionCell(builder.Configuration);
builder.Services.AddVirtualSmartMotionCellOpcUa();
builder.Services.AddHealthChecks();

var observability = builder.Configuration.GetSection("Observability").Get<ObservabilityOptions>() ?? new ObservabilityOptions();
var openTelemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(MachineTelemetry.ServiceName));
openTelemetry.WithTracing(tracing =>
{
    tracing.AddSource(MachineTelemetry.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation();
    if (!string.IsNullOrWhiteSpace(observability.OtlpEndpoint))
        tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(observability.OtlpEndpoint, UriKind.Absolute));
});
openTelemetry.WithMetrics(metrics =>
{
    metrics.AddMeter(MachineTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation();
    if (!string.IsNullOrWhiteSpace(observability.OtlpEndpoint))
        metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(observability.OtlpEndpoint, UriKind.Absolute));
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Activity.Current?.TraceId.ToString()
        ?? context.TraceIdentifier;
    context.Items["correlation-id"] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    Activity.Current?.SetTag("correlation.id", correlationId);
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("VirtualSmartMotionCell.Request");
    using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        await next().ConfigureAwait(false);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.MapGet("/health/live", () => Results.Ok(new { status = "live", timestamp = DateTimeOffset.UtcNow }));
app.MapGet("/health/ready", (MachineStateStore store) => store.Current is null
    ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
    : Results.Ok(new { status = "ready", revision = store.Current.Revision, state = store.Current.ExecutionState }));

app.MapGet("/api/v1/state", (MachineStateStore store) => store.Current is { } state
    ? Results.Ok(state)
    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapGet("/api/v1/diagnostics", (MachineStateStore store) => store.Current is { } state
    ? Results.Ok(new { state.Runtime, state.Interlocks, state.Integration, state.Recovery, state.LastTransition, state.CorrelationId })
    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapGet("/api/v1/alarms", (MachineStateStore store) => Results.Ok(store.Current?.ActiveAlarms ?? Array.Empty<AlarmSnapshot>()));
app.MapGet("/api/v1/alarms/history", async (int? limit, IAlarmHistoryStore alarms, CancellationToken cancellationToken) =>
    Results.Ok(await alarms.ReadAsync(Math.Clamp(limit ?? 100, 1, 1000), cancellationToken).ConfigureAwait(false)));

app.MapGet("/api/v1/orders", async (int? limit, IProductionRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.ReadOrdersAsync(Math.Clamp(limit ?? 100, 1, 1000), cancellationToken).ConfigureAwait(false)));
app.MapPost("/api/v1/orders", async (ProductionOrderRequest order, HttpContext context, MachineCommandBus bus, CancellationToken cancellationToken) =>
{
    var command = new MachineCommandRequest("load-order", RequestedBy: "api", CorrelationId: Correlation(context),
        OrderId: order.OrderId, Quantity: order.Quantity, RecipeId: order.RecipeId, RecipeRevision: order.RecipeRevision);
    return CommandHttpResult(await bus.SendAsync(command, cancellationToken).ConfigureAwait(false));
});
app.MapGet("/api/v1/parts", async (int? limit, IProductionRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.ReadPartsAsync(Math.Clamp(limit ?? 100, 1, 1000), cancellationToken).ConfigureAwait(false)));
app.MapGet("/api/v1/cycles", async (int? limit, IProductionRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.ReadCyclesAsync(Math.Clamp(limit ?? 100, 1, 1000), cancellationToken).ConfigureAwait(false)));
app.MapGet("/api/v1/traceability", async (int? limit, IProductionRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.ReadTraceabilityAsync(Math.Clamp(limit ?? 200, 1, 2000), cancellationToken).ConfigureAwait(false)));

app.MapGet("/api/v1/recipes", async (IRecipeStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.ListAsync(cancellationToken).ConfigureAwait(false)));
app.MapGet("/api/v1/recipes/active", (MachineStateStore store) => Results.Ok(store.Current?.ActiveRecipe));
app.MapPost("/api/v1/recipes/drafts", async (MachineRecipe recipe, IRecipeStore store, CancellationToken cancellationToken) =>
{
    await store.SaveDraftAsync(recipe, cancellationToken).ConfigureAwait(false);
    return Results.Created($"/api/v1/recipes/{recipe.RecipeId}/{recipe.Revision}", recipe.WithStatus(RecipeLifecycle.Draft).ToSnapshot());
});
app.MapPost("/api/v1/recipes/{recipeId}/{revision:int}/approve", async (string recipeId, int revision, IRecipeStore store, CancellationToken cancellationToken) =>
    Results.Ok((await store.ApproveAsync(recipeId, revision, cancellationToken).ConfigureAwait(false)).ToSnapshot()));
app.MapPost("/api/v1/recipes/{recipeId}/{revision:int}/activate", async (string recipeId, int revision, HttpContext context, MachineCommandBus bus, CancellationToken cancellationToken) =>
    CommandHttpResult(await bus.SendAsync(new MachineCommandRequest("activate-recipe", RequestedBy: "api", CorrelationId: Correlation(context), RecipeId: recipeId, RecipeRevision: revision), cancellationToken).ConfigureAwait(false)));

app.MapGet("/api/v1/integration", (MachineStateStore store) => Results.Ok(store.Current?.Integration));
app.MapGet("/api/v1/opcua", (IOptions<OpcUaOptions> options) => Results.Ok(new { options.Value.Enabled, options.Value.Endpoint, security = "Anonymous / SecurityPolicy None for local simulation only" }));
app.MapGet("/api/v1/outbox", async (IOutboxStore outbox, CancellationToken cancellationToken) =>
    Results.Ok(new { pending = await outbox.CountPendingAsync(cancellationToken).ConfigureAwait(false) }));

app.MapPost("/api/v1/commands", async (MachineCommandRequest command, HttpContext context, MachineCommandBus bus, IOptions<RuntimeOptions> runtimeOptions, CancellationToken cancellationToken) =>
{
    var remoteAddress = context.Connection.RemoteIpAddress;
    if (!runtimeOptions.Value.AllowRemoteCommands && remoteAddress is not null && !System.Net.IPAddress.IsLoopback(remoteAddress))
        return Results.Json(new { reasonCode = "REMOTE_COMMANDS_DISABLED", reasons = new[] { "Commands are limited to localhost by default." } }, statusCode: StatusCodes.Status403Forbidden);

    var enriched = command with { CorrelationId = command.CorrelationId ?? Correlation(context) };
    return CommandHttpResult(await bus.SendAsync(enriched, cancellationToken).ConfigureAwait(false));
});

app.MapGet("/metrics", (MachineStateStore store) =>
{
    var state = store.Current;
    if (state is null) return Results.Text("# runtime not ready\n", "text/plain");
    var text = $$"""
# HELP vsmc_machine_revision Current machine state revision.
# TYPE vsmc_machine_revision gauge
vsmc_machine_revision {{state.Revision}}
# HELP vsmc_runtime_deadline_misses_total Simulation loop deadline misses.
# TYPE vsmc_runtime_deadline_misses_total counter
vsmc_runtime_deadline_misses_total {{state.Runtime.DeadlineMissCount}}
# HELP vsmc_axis_following_error Axis following error.
# TYPE vsmc_axis_following_error gauge
vsmc_axis_following_error{axis="X"} {{Invariant(state.XAxis.FollowingError)}}
vsmc_axis_following_error{axis="Y"} {{Invariant(state.YAxis.FollowingError)}}
# HELP vsmc_production_cycles_total Completed production cycles.
# TYPE vsmc_production_cycles_total counter
vsmc_production_cycles_total {{state.Production.CycleCount}}
# HELP vsmc_oee Demonstration overall equipment effectiveness.
# TYPE vsmc_oee gauge
vsmc_oee {{Invariant(state.Production.Oee)}}
# HELP vsmc_outbox_pending Pending manufacturing messages.
# TYPE vsmc_outbox_pending gauge
vsmc_outbox_pending {{state.Integration.OutboxPending}}
""";
    return Results.Text(text, "text/plain; version=0.0.4");
});

app.Map("/ws/state", async (HttpContext context, WebSocketStatePublisher publisher) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
    await publisher.AcceptAsync(socket, context.RequestAborted).ConfigureAwait(false);
});

app.MapFallbackToFile("index.html");
app.Run();

static string Correlation(HttpContext context) => context.Items["correlation-id"]?.ToString() ?? context.TraceIdentifier;
static IResult CommandHttpResult(CommandResult result) => result.Status == CommandStatus.Accepted ? Results.Accepted(value: result) : Results.BadRequest(result);
static string Invariant(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
