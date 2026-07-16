using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Domain;

namespace VirtualSmartMotionCell.Runtime;

public sealed class MachineCommandProcessorService(
    MachineCommandBus bus,
    MachineCoordinator coordinator,
    IRecipeStore recipeStore,
    ILogger<MachineCommandProcessorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var queued in bus.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            bus.MarkRead();
            using var activity = MachineTelemetry.Activities.StartActivity(
                "command.process", ActivityKind.Internal, queued.ParentContext);
            activity?.SetTag("command.id", queued.CommandId);
            activity?.SetTag("command.type", queued.Request.Type);
            try
            {
                CommandResult result;
                if (string.Equals(queued.Request.Type, "activate-recipe", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(queued.Request.RecipeId) || queued.Request.RecipeRevision is null)
                    {
                        result = CommandResult.Rejected(queued.CommandId, coordinator.Snapshot().Revision,
                            queued.Request.CorrelationId ?? queued.CommandId, "RECIPE_ID_REQUIRED", "RecipeId and RecipeRevision are required.");
                    }
                    else
                    {
                        var recipe = await recipeStore.ActivateAsync(queued.Request.RecipeId, queued.Request.RecipeRevision.Value, stoppingToken).ConfigureAwait(false);
                        result = coordinator.ActivateRecipe(queued.CommandId, recipe, queued.Request.CorrelationId ?? queued.CommandId);
                    }
                }
                else
                {
                    result = coordinator.Execute(queued.CommandId, queued.Request);
                }

                queued.Completion.TrySetResult(result);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Command {CommandId} failed", queued.CommandId);
                queued.Completion.TrySetResult(new CommandResult(
                    queued.CommandId, CommandStatus.Failed, "UNHANDLED_COMMAND_ERROR",
                    new[] { exception.Message }, DateTimeOffset.UtcNow, coordinator.Snapshot().Revision,
                    queued.Request.CorrelationId ?? queued.CommandId));
            }
        }
    }

}

public sealed class MachineRuntimeService(
    MachineCoordinator coordinator,
    IMotionSystem motionSystem,
    MachineStateStore stateStore,
    RuntimeMetrics metrics,
    IMachineEventStore eventStore,
    ICheckpointStore checkpointStore,
    IOutboxStore outboxStore,
    IProductionRepository productionRepository,
    IAlarmHistoryStore alarmHistoryStore,
    StatePublicationQueue publicationQueue,
    IntegrationStatusStore integrationStatus,
    IOptions<RuntimeOptions> options,
    ILogger<MachineRuntimeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await motionSystem.InitializeAsync(stoppingToken).ConfigureAwait(false);
        var checkpoint = await checkpointStore.LoadAsync(stoppingToken).ConfigureAwait(false);
        var checkpointPresent = checkpoint is not null;
        if (checkpoint is not null && checkpoint.ExecutionState is not (ExecutionState.Stopped or ExecutionState.Ready))
        {
            coordinator.RestoreCheckpoint(checkpoint);
            logger.LogWarning("Recovery is required from checkpoint revision {Revision}", checkpoint.MachineRevision);
        }

        var periodMs = Math.Max(1, options.Value.SimulationPeriodMilliseconds);
        var publishEvery = Math.Max(1, options.Value.StatePublishPeriodMilliseconds / periodMs);
        var checkpointEvery = Math.Max(1, options.Value.CheckpointPeriodMilliseconds / periodMs);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(periodMs));
        var tick = 0L;
        var lastCheckpointRevision = -1L;
        logger.LogInformation("Machine runtime started with {PeriodMs} ms simulation period", periodMs);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var stopwatch = Stopwatch.StartNew();
            var snapshot = coordinator.Step(periodMs / 1000.0);
            stateStore.Publish(snapshot);

            var checkpointRequested = false;
            foreach (var machineEvent in coordinator.DrainEvents())
            {
                await eventStore.AppendAsync(machineEvent, stoppingToken).ConfigureAwait(false);
                await productionRepository.HandleEventAsync(machineEvent, stoppingToken).ConfigureAwait(false);
                if (machineEvent.Payload is AlarmEventPayload alarmEvent)
                    await alarmHistoryStore.AppendAsync(alarmEvent.Alarm, alarmEvent.Action, stoppingToken).ConfigureAwait(false);
                if (machineEvent.EventType == "cycle.completed")
                    await outboxStore.EnqueueAsync(machineEvent, stoppingToken).ConfigureAwait(false);
                if (machineEvent.EventType is "machine.transition" or "alarm.raised" or "cycle.completed" or "order.loaded" or "order.status")
                    checkpointRequested = true;
            }

            integrationStatus.SetOutboxPending(await outboxStore.CountPendingAsync(stoppingToken).ConfigureAwait(false));
            if (++tick % publishEvery == 0) publicationQueue.Publish(snapshot);

            var activeState = snapshot.ExecutionState is ExecutionState.Homing or ExecutionState.Starting or ExecutionState.Running or ExecutionState.Pausing or ExecutionState.Paused or ExecutionState.Stopping or ExecutionState.Faulted or ExecutionState.RecoveryRequired or ExecutionState.Recovering;
            var periodicCheckpoint = activeState && tick % checkpointEvery == 0;
            if ((checkpointRequested && snapshot.Revision != lastCheckpointRevision) || periodicCheckpoint)
            {
                await checkpointStore.SaveAsync(coordinator.CreateCheckpoint(snapshot.LastTransition), stoppingToken).ConfigureAwait(false);
                checkpointPresent = true;
                lastCheckpointRevision = snapshot.Revision;
            }

            var safeStableState = snapshot.ExecutionState is ExecutionState.Stopped or ExecutionState.Ready
                && snapshot.ActivePart is null
                && !snapshot.Recovery.Required;
            if (safeStableState && checkpointPresent)
            {
                await checkpointStore.ClearAsync(stoppingToken).ConfigureAwait(false);
                checkpointPresent = false;
                lastCheckpointRevision = -1;
            }

            stopwatch.Stop();
            metrics.RecordLoop(stopwatch.Elapsed.TotalMilliseconds, periodMs);
            MachineTelemetry.RecordLoop(stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}

public sealed class MachineStatePublicationService(
    StatePublicationQueue queue,
    IEnumerable<IMachineStatePublisher> publishers,
    ILogger<MachineStatePublicationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var snapshot in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var publisher in publishers)
            {
                try { await publisher.PublishAsync(snapshot, stoppingToken).ConfigureAwait(false); }
                catch (Exception exception) { logger.LogWarning(exception, "Machine state publisher failed"); }
            }
        }
    }
}

public sealed class OutboxDeliveryService(
    IOutboxStore outboxStore,
    IManufacturingGateway manufacturingGateway,
    IntegrationStatusStore integrationStatus,
    ILogger<OutboxDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var health = await manufacturingGateway.CheckHealthAsync(stoppingToken).ConfigureAwait(false);
            integrationStatus.UpdateMes(health.Health, health.Status);
            var pending = await outboxStore.ReadPendingAsync(20, stoppingToken).ConfigureAwait(false);
            integrationStatus.SetOutboxPending(pending.Count);
            foreach (var message in pending)
            {
                try
                {
                    using var activity = MachineTelemetry.Activities.StartActivity("mes.deliver", ActivityKind.Producer);
                    activity?.SetTag("message.id", message.EventId);
                    if (await manufacturingGateway.DeliverAsync(message, stoppingToken).ConfigureAwait(false))
                    {
                        await outboxStore.MarkDeliveredAsync(message.EventId, stoppingToken).ConfigureAwait(false);
                        integrationStatus.MarkDelivered(DateTimeOffset.UtcNow);
                        integrationStatus.UpdateMes(IntegrationHealth.Healthy, "delivery successful");
                        delay = TimeSpan.FromSeconds(1);
                    }
                    else
                    {
                        integrationStatus.UpdateMes(IntegrationHealth.Degraded, "delivery rejected or unavailable");
                        delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                        break;
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Manufacturing delivery failed for event {EventId}", message.EventId);
                    integrationStatus.UpdateMes(IntegrationHealth.Offline, exception.Message);
                    delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
                    break;
                }
            }
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}

public sealed class MesOrderPollingService(
    IHttpClientFactory httpClientFactory,
    MachineCommandBus bus,
    MachineStateStore stateStore,
    IOptions<MesOptions> options,
    ILogger<MesOrderPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PollOrders || !string.Equals(options.Value.Mode, "Http", StringComparison.OrdinalIgnoreCase)) return;
        var client = httpClientFactory.CreateClient("mes");
        var delay = TimeSpan.FromMilliseconds(Math.Max(250, options.Value.PollIntervalMilliseconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var currentOrder = stateStore.Current?.Production.ActiveOrder;
                if (currentOrder?.Status is ProductionOrderStatus.Queued or ProductionOrderStatus.Active or ProductionOrderStatus.Paused)
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                    continue;
                }
                var endpoint =
                    $"api/v1/orders/next?machineId={Uri.EscapeDataString(options.Value.MachineId)}";

                using var response = await client.GetAsync(endpoint, stoppingToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var payload = await response.Content
                    .ReadAsStringAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(payload))
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var order = System.Text.Json.JsonSerializer.Deserialize<MesOrder>(
                    payload,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                if (order is not null)
                {
                    var result = await bus.SendAsync(new MachineCommandRequest(
                        "load-order", RequestedBy: "mes", OrderId: order.OrderId, Quantity: order.Quantity,
                        RecipeId: order.RecipeId, RecipeRevision: order.RecipeRevision), stoppingToken).ConfigureAwait(false);
                    logger.LogInformation("MES order {OrderId} load result: {Result}", order.OrderId, result.ReasonCode);
                }
            }
            catch (HttpRequestException exception)
            {
                logger.LogDebug(exception, "MES order polling unavailable");
            }
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    private sealed record MesOrder(string OrderId, int Quantity, string RecipeId, int RecipeRevision);
}
