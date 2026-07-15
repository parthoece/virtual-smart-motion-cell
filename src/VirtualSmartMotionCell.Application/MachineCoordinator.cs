using System.Diagnostics;
using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Domain;

namespace VirtualSmartMotionCell.Application;

public sealed class MachineCoordinator
{
    private readonly object _gate = new();
    private readonly IMotionSystem _motion;
    private readonly RuntimeMetrics _runtimeMetrics;
    private readonly IntegrationStatusStore _integrationStatus;
    private readonly Queue<MachineEvent> _events = new();
    private readonly Dictionary<string, AlarmRecord> _alarms = new(StringComparer.OrdinalIgnoreCase);
    private MachineRecipe _recipe;
    private ProductionOrder? _order;
    private RecoveryCheckpoint? _recoveryCheckpoint;
    private MachineMode _mode = MachineMode.Manual;
    private ExecutionState _execution = ExecutionState.Stopped;
    private ProductionStep _step = ProductionStep.None;
    private bool _gripperClosed;
    private bool _emergencyStopOk = true;
    private bool _guardClosed = true;
    private bool _busHealthy = true;
    private long _revision;
    private double _simulationTime;
    private double _dwellRemaining;
    private long _cycleCount;
    private long _goodCount;
    private long _rejectCount;
    private string? _activePartId;
    private double? _cycleStartedSimulationTime;
    private double _lastCycleSeconds;
    private string _lastTransition = "Created";
    private string _correlationId = "startup";

    public MachineCoordinator(
        MachineRecipe recipe,
        IMotionSystem motion,
        RuntimeMetrics runtimeMetrics,
        IntegrationStatusStore integrationStatus)
    {
        _recipe = recipe;
        _motion = motion;
        _runtimeMetrics = runtimeMetrics;
        _integrationStatus = integrationStatus;
    }

    public CommandResult Execute(string commandId, MachineCommandRequest request)
    {
        lock (_gate)
        {
            var correlationId = request.CorrelationId ?? commandId;
            _correlationId = correlationId;
            var type = (request.Type ?? string.Empty).Trim().ToLowerInvariant();
            using var activity = MachineTelemetry.Activities.StartActivity("machine.command", ActivityKind.Internal);
            activity?.SetTag("command.id", commandId);
            activity?.SetTag("command.type", type);
            activity?.SetTag("correlation.id", correlationId);

            var result = type switch
            {
                "initialize" => Initialize(commandId, correlationId),
                "set-mode" => SetMode(commandId, request.Mode, correlationId),
                "load-order" => LoadOrder(commandId, request, correlationId),
                "cancel-order" => CancelOrder(commandId, correlationId),
                "home" => Home(commandId, correlationId),
                "start" => Start(commandId, correlationId),
                "pause" => Pause(commandId, correlationId),
                "resume" => Resume(commandId, correlationId),
                "stop" => Stop(commandId, correlationId),
                "abort" => Abort(commandId, correlationId),
                "reset" => Reset(commandId, correlationId),
                "recover-reset" or "recover-discard" => Recover(commandId, RecoveryAction.DiscardAndReset, correlationId),
                "recover-rehome" => Recover(commandId, RecoveryAction.RehomeAndReset, correlationId),
                "recover-resume" => Recover(commandId, RecoveryAction.ResumeSimulation, correlationId),
                "jog" => Jog(commandId, request.Axis, request.Value, correlationId),
                "inject-fault" => InjectFault(commandId, request.Fault, correlationId),
                "clear-fault" => ClearFault(commandId, request.Fault, correlationId),
                "acknowledge-alarms" => AcknowledgeAlarms(commandId, request.RequestedBy ?? "operator", correlationId),
                _ => CommandResult.Rejected(commandId, _revision, correlationId, "UNKNOWN_COMMAND", $"Unknown command type '{request.Type}'.")
            };

            MachineTelemetry.RecordCommand(type, result);
            activity?.SetTag("command.status", result.Status.ToString());
            activity?.SetTag("reason.code", result.ReasonCode);
            return result;
        }
    }

    public CommandResult ActivateRecipe(string commandId, MachineRecipe recipe, string correlationId)
    {
        lock (_gate)
        {
            var validation = recipe.Validate();
            if (validation.Count > 0) return Reject(commandId, correlationId, "INVALID_RECIPE", validation.ToArray());
            if (_execution is not (ExecutionState.Stopped or ExecutionState.Ready))
                return Reject(commandId, correlationId, "RECIPE_CHANGE_BLOCKED", "Recipe activation is allowed only from Stopped or Ready.");
            if (_order is { Status: ProductionOrderStatus.Active or ProductionOrderStatus.Paused })
                return Reject(commandId, correlationId, "ACTIVE_ORDER_PRESENT", "Complete or cancel the current order before activating another recipe.");

            _recipe = recipe.WithStatus(RecipeLifecycle.Active);
            IncrementRevision("Recipe activated");
            Record("recipe.activated", new RecipeActivatedPayload(_recipe.RecipeId, _recipe.Revision));
            return Accept(commandId, correlationId, "RECIPE_ACTIVATED");
        }
    }

    public MachineStateSnapshot Step(double dt)
    {
        lock (_gate)
        {
            _simulationTime += dt;
            _motion.Step(dt, _recipe.Motion.MaximumVelocity, _recipe.Motion.MaximumAcceleration);
            var currentMotion = _motion.Snapshot(MotionStateForExecution());
            var interlocks = BuildInterlocks(currentMotion);

            if (!interlocks.MotionPermitted && _execution is ExecutionState.Homing or ExecutionState.Starting or ExecutionState.Running or ExecutionState.Pausing or ExecutionState.Recovering)
            {
                RaiseFault("MOTION_PERMISSION_LOST", "Interlocks", AlarmSeverity.Critical,
                    string.Join("; ", interlocks.BlockingReasons), "Restore the blocking condition, acknowledge alarms, then reset.");
            }

            var followingError = Math.Max(Math.Abs(currentMotion.XAxis.FollowingError), Math.Abs(currentMotion.YAxis.FollowingError));
            if (followingError > _recipe.Motion.FollowingErrorLimit && _execution is ExecutionState.Homing or ExecutionState.Running or ExecutionState.Recovering)
            {
                RaiseFault("FOLLOWING_ERROR", "Motion", AlarmSeverity.Error,
                    $"Following error {followingError:F3} exceeded limit {_recipe.Motion.FollowingErrorLimit:F3}.",
                    "Inspect motion parameters, simulated load, and axis health before resetting.");
            }

            currentMotion = _motion.Snapshot(MotionStateForExecution());
            switch (_execution)
            {
                case ExecutionState.Initializing:
                    Transition(ExecutionState.Stopped, ProductionStep.None, "Initialization completed");
                    break;
                case ExecutionState.Homing when currentMotion.AtTarget:
                    _motion.MarkHomed();
                    Transition(ExecutionState.Ready, ProductionStep.None, "All axes homed");
                    break;
                case ExecutionState.Starting:
                    BeginCycle();
                    Transition(ExecutionState.Running, ProductionStep.MoveToPick, "Automatic cycle started");
                    CommandPosition(_recipe.Pick);
                    break;
                case ExecutionState.Running:
                    AdvanceProduction(dt, currentMotion);
                    break;
                case ExecutionState.Pausing when currentMotion.AtTarget:
                    _order?.Pause();
                    if (_order is not null) Record("order.status", new OrderStatusChangedPayload(_order.OrderId, _order.Status, _order.CompletedQuantity, "controlled-pause"));
                    Transition(ExecutionState.Paused, _step, "Controlled pause completed");
                    break;
                case ExecutionState.Stopping when currentMotion.AtTarget:
                    DiscardInterruptedPart();
                    _order?.Pause();
                    if (_order is not null) Record("order.status", new OrderStatusChangedPayload(_order.OrderId, _order.Status, _order.CompletedQuantity, "controlled-stop"));
                    Transition(ExecutionState.Ready, ProductionStep.None, "Controlled stop completed");
                    break;
                case ExecutionState.Aborting:
                    _motion.DisableAll();
                    DiscardInterruptedPart();
                    _order?.Pause();
                    if (_order is not null) Record("order.status", new OrderStatusChangedPayload(_order.OrderId, _order.Status, _order.CompletedQuantity, "abort"));
                    Transition(ExecutionState.Stopped, ProductionStep.None, "Abort completed");
                    break;
                case ExecutionState.Recovering when currentMotion.AtTarget:
                    _motion.MarkHomed();
                    _mode = MachineMode.Manual;
                    _recoveryCheckpoint = null;
                    Transition(ExecutionState.Ready, ProductionStep.None, "Recovery rehome completed");
                    Record("recovery.completed", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId, "rehome-and-reset", "Axes rehomed; interrupted part discarded."));
                    break;
            }

            return SnapshotUnsafe();
        }
    }

    public MachineStateSnapshot Snapshot()
    {
        lock (_gate) return SnapshotUnsafe();
    }

    public RecoveryCheckpoint CreateCheckpoint(string reason)
    {
        lock (_gate)
        {
            var motion = _motion.Snapshot(MotionStateForExecution());
            return new RecoveryCheckpoint(
                DateTimeOffset.UtcNow, _revision, _mode, _execution, _step, _activePartId,
                _order?.OrderId, _order?.TargetQuantity, _order?.CompletedQuantity ?? 0,
                _recipe.RecipeId, _recipe.Revision, _gripperClosed,
                motion.XAxis.ActualPosition, motion.YAxis.ActualPosition,
                motion.XAxis.TargetPosition, motion.YAxis.TargetPosition,
                _cycleStartedSimulationTime, reason);
        }
    }

    public void RestoreCheckpoint(RecoveryCheckpoint checkpoint)
    {
        lock (_gate)
        {
            _motion.Restore(checkpoint.XPosition, checkpoint.YPosition, false, checkpoint.XTarget, checkpoint.YTarget);
            _mode = MachineMode.Recovery;
            _execution = ExecutionState.RecoveryRequired;
            _step = checkpoint.ProductionStep;
            _activePartId = checkpoint.ActivePartId;
            _gripperClosed = checkpoint.GripperClosed;
            _cycleStartedSimulationTime = checkpoint.CycleStartedSimulationTime;
            _recoveryCheckpoint = checkpoint;
            if (checkpoint.ActiveOrderId is not null && checkpoint.OrderTargetQuantity is > 0)
            {
                _order = new ProductionOrder(checkpoint.ActiveOrderId, checkpoint.OrderTargetQuantity.Value, checkpoint.RecipeId, checkpoint.RecipeRevision);
                _order.Restore(checkpoint.OrderCompletedQuantity, ProductionOrderStatus.Paused);
            }
            _revision = Math.Max(_revision, checkpoint.MachineRevision) + 1;
            _lastTransition = $"Recovery required after restart: {checkpoint.Reason}";
            _correlationId = "startup-recovery";
            Record("recovery.required", new TraceabilityPayload(checkpoint.ActiveOrderId ?? "none", checkpoint.ActivePartId, "recovery-required", checkpoint.Reason));
        }
    }

    public IReadOnlyList<MachineEvent> DrainEvents()
    {
        lock (_gate)
        {
            var list = _events.ToArray();
            _events.Clear();
            return list;
        }
    }

    private CommandResult Initialize(string id, string correlation)
    {
        if (_execution is not ExecutionState.Stopped) return Reject(id, correlation, "INVALID_STATE", "Initialize is allowed only from Stopped.");
        _motion.EnableAll();
        Transition(ExecutionState.Initializing, ProductionStep.None, "Initialize command accepted");
        return Accept(id, correlation);
    }

    private CommandResult SetMode(string id, string? mode, string correlation)
    {
        if (_execution is ExecutionState.Running or ExecutionState.Starting or ExecutionState.Homing or ExecutionState.Recovering)
            return Reject(id, correlation, "MODE_CHANGE_BLOCKED", "Mode cannot change while motion is active.");
        if (!Enum.TryParse<MachineMode>(mode, true, out var parsed))
            return Reject(id, correlation, "INVALID_MODE", "Mode must be Manual, Automatic, Maintenance, Recovery, or Offline.");
        if (parsed == MachineMode.Offline && _execution != ExecutionState.Stopped)
            return Reject(id, correlation, "OFFLINE_REQUIRES_STOPPED", "Offline mode requires the machine to be Stopped.");
        _mode = parsed;
        Record("mode.changed", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId, "mode-changed", _mode.ToString()));
        IncrementRevision("Mode changed");
        return Accept(id, correlation);
    }

    private CommandResult LoadOrder(string id, MachineCommandRequest request, string correlation)
    {
        if (_execution is not (ExecutionState.Stopped or ExecutionState.Ready))
            return Reject(id, correlation, "ORDER_LOAD_BLOCKED", "Orders can be loaded only from Stopped or Ready.");
        if (_order is { Status: ProductionOrderStatus.Active or ProductionOrderStatus.Paused })
            return Reject(id, correlation, "ACTIVE_ORDER_PRESENT", "Cancel or complete the current order first.");
        if (string.IsNullOrWhiteSpace(request.OrderId)) return Reject(id, correlation, "ORDER_ID_REQUIRED", "OrderId is required.");
        if (request.Quantity is null or <= 0 or > 100000) return Reject(id, correlation, "INVALID_QUANTITY", "Quantity must be between 1 and 100000.");
        var recipeId = request.RecipeId ?? _recipe.RecipeId;
        var recipeRevision = request.RecipeRevision ?? _recipe.Revision;
        if (!string.Equals(recipeId, _recipe.RecipeId, StringComparison.OrdinalIgnoreCase) || recipeRevision != _recipe.Revision)
            return Reject(id, correlation, "RECIPE_MISMATCH", "The order recipe must match the active recipe.");

        _order = new ProductionOrder(request.OrderId, request.Quantity.Value, recipeId, recipeRevision);
        IncrementRevision("Production order loaded");
        Record("order.loaded", new OrderLoadedPayload(_order.OrderId, _order.TargetQuantity, _order.RecipeId, _order.RecipeRevision, _order.Status));
        return Accept(id, correlation, "ORDER_LOADED");
    }

    private CommandResult CancelOrder(string id, string correlation)
    {
        if (_order is null) return Reject(id, correlation, "NO_ACTIVE_ORDER", "No production order is loaded.");
        if (_execution is ExecutionState.Running or ExecutionState.Starting) return Reject(id, correlation, "ORDER_CANCEL_BLOCKED", "Stop or abort the running cycle before cancelling the order.");
        _order.Cancel();
        Record("order.status", new OrderStatusChangedPayload(_order.OrderId, _order.Status, _order.CompletedQuantity, "operator-cancelled"));
        IncrementRevision("Production order cancelled");
        return Accept(id, correlation, "ORDER_CANCELLED");
    }

    private CommandResult Home(string id, string correlation)
    {
        var motion = _motion.Snapshot(AxisMotionState.Standstill);
        var interlocks = BuildInterlocks(motion);
        if (!interlocks.MotionPermitted) return Reject(id, correlation, "MOTION_NOT_PERMITTED", interlocks.BlockingReasons.ToArray());
        if (_execution is ExecutionState.Running or ExecutionState.Starting) return Reject(id, correlation, "INVALID_STATE", "Stop automatic operation before homing.");
        _motion.EnableAll();
        _motion.SetTarget(_recipe.Home.X, _recipe.Home.Y);
        Transition(ExecutionState.Homing, ProductionStep.None, "Homing started");
        return Accept(id, correlation);
    }

    private CommandResult Start(string id, string correlation)
    {
        var motion = _motion.Snapshot(AxisMotionState.Standstill);
        var reasons = new List<string>();
        if (_mode != MachineMode.Automatic) reasons.Add("Machine mode is not Automatic.");
        if (_execution != ExecutionState.Ready) reasons.Add($"Execution state is {_execution}, not Ready.");
        if (!motion.AllHomed) reasons.Add("All axes must be homed.");
        if (_order is null) reasons.Add("No production order is loaded.");
        else if (_order.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled) reasons.Add($"Production order is {_order.Status}.");
        reasons.AddRange(BuildInterlocks(motion).BlockingReasons);
        if (reasons.Count > 0) return Reject(id, correlation, "START_CONDITIONS_NOT_MET", reasons.ToArray());
        _order!.Activate();
        Record("order.status", new OrderStatusChangedPayload(_order.OrderId, _order.Status, _order.CompletedQuantity, "automatic-start"));
        Transition(ExecutionState.Starting, ProductionStep.WaitForPart, "Start accepted");
        return Accept(id, correlation);
    }

    private CommandResult Pause(string id, string correlation)
    {
        if (_execution != ExecutionState.Running) return Reject(id, correlation, "INVALID_STATE", "Pause requires Running state.");
        _motion.Hold();
        Transition(ExecutionState.Pausing, _step, "Controlled pause requested");
        return Accept(id, correlation);
    }

    private CommandResult Resume(string id, string correlation)
    {
        if (_execution != ExecutionState.Paused) return Reject(id, correlation, "INVALID_STATE", "Resume requires Paused state.");
        var interlocks = BuildInterlocks(_motion.Snapshot(AxisMotionState.Holding));
        if (!interlocks.MotionPermitted) return Reject(id, correlation, "MOTION_NOT_PERMITTED", interlocks.BlockingReasons.ToArray());
        _order?.Resume();
        if (_order is not null) Record("order.status", new OrderStatusChangedPayload(_order.OrderId, _order.Status, _order.CompletedQuantity, "resume"));
        Transition(ExecutionState.Running, _step, "Resume accepted");
        CommandTargetForCurrentStep();
        return Accept(id, correlation);
    }

    private CommandResult Stop(string id, string correlation)
    {
        if (_execution is not (ExecutionState.Running or ExecutionState.Starting or ExecutionState.Paused or ExecutionState.Homing or ExecutionState.Pausing))
            return Reject(id, correlation, "INVALID_STATE", "Stop requires active or paused operation.");
        _motion.Hold();
        Transition(ExecutionState.Stopping, _step, "Controlled stop requested");
        return Accept(id, correlation);
    }

    private CommandResult Abort(string id, string correlation)
    {
        if (_execution is ExecutionState.Stopped or ExecutionState.Aborting)
            return Reject(id, correlation, "INVALID_STATE", "There is no active operation to abort.");
        Transition(ExecutionState.Aborting, _step, "Abort requested");
        return Accept(id, correlation);
    }

    private CommandResult Reset(string id, string correlation)
    {
        if (_execution != ExecutionState.Faulted && _alarms.Values.All(alarm => alarm.Lifecycle == AlarmLifecycle.Historical))
            return Reject(id, correlation, "NO_ACTIVE_FAULT", "Reset is intended for a faulted machine.");
        var interlocks = BuildInterlocks(_motion.Snapshot(AxisMotionState.Faulted));
        if (!interlocks.EmergencyStopOk || !interlocks.GuardClosed || !interlocks.BusHealthy)
            return Reject(id, correlation, "FAULT_CAUSE_PRESENT", interlocks.BlockingReasons.ToArray());
        _motion.ClearFault();
        _motion.EnableAll();
        foreach (var alarm in _alarms.Values.Where(alarm => alarm.Lifecycle is AlarmLifecycle.ActiveAcknowledged or AlarmLifecycle.ActiveUnacknowledged))
        {
            alarm.Clear();
            Record("alarm.cleared", new AlarmEventPayload(alarm.ToSnapshot(), "cleared"));
        }
        var motion = _motion.Snapshot(AxisMotionState.Standstill);
        Transition(motion.AllHomed ? ExecutionState.Ready : ExecutionState.Stopped, ProductionStep.None, "Fault reset completed");
        return Accept(id, correlation);
    }

    private CommandResult Recover(string id, RecoveryAction action, string correlation)
    {
        if (_execution != ExecutionState.RecoveryRequired || _recoveryCheckpoint is null)
            return Reject(id, correlation, "RECOVERY_NOT_REQUIRED", "Recovery commands are allowed only from RecoveryRequired.");

        switch (action)
        {
            case RecoveryAction.DiscardAndReset:
                DiscardInterruptedPart();
                _motion.Restore(_recoveryCheckpoint.XPosition, _recoveryCheckpoint.YPosition, false);
                _mode = MachineMode.Manual;
                _recoveryCheckpoint = null;
                Transition(ExecutionState.Stopped, ProductionStep.None, "Interrupted cycle discarded and recovery reset");
                Record("recovery.completed", new TraceabilityPayload(_order?.OrderId ?? "none", null, "discard-and-reset", "Interrupted part discarded."));
                return Accept(id, correlation, "RECOVERY_COMPLETED");

            case RecoveryAction.RehomeAndReset:
                DiscardInterruptedPart();
                _motion.Restore(_recoveryCheckpoint.XPosition, _recoveryCheckpoint.YPosition, false);
                _motion.EnableAll();
                _motion.SetTarget(_recipe.Home.X, _recipe.Home.Y);
                _mode = MachineMode.Recovery;
                Transition(ExecutionState.Recovering, ProductionStep.None, "Recovery rehome started");
                return Accept(id, correlation, "RECOVERY_REHOME_STARTED");

            case RecoveryAction.ResumeSimulation:
                if (_recoveryCheckpoint.ActiveOrderId is null || _recoveryCheckpoint.ActivePartId is null)
                    return Reject(id, correlation, "RECOVERY_RESUME_UNAVAILABLE", "The checkpoint does not contain an active order and part.");
                if (!BuildInterlocks(_motion.Snapshot(AxisMotionState.Holding)).MotionPermitted)
                    return Reject(id, correlation, "MOTION_NOT_PERMITTED", BuildInterlocks(_motion.Snapshot(AxisMotionState.Holding)).BlockingReasons.ToArray());
                _motion.Restore(_recoveryCheckpoint.XPosition, _recoveryCheckpoint.YPosition, true, _recoveryCheckpoint.XTarget, _recoveryCheckpoint.YTarget);
                _motion.EnableAll();
                _mode = MachineMode.Automatic;
                _execution = ExecutionState.Running;
                _step = _recoveryCheckpoint.ProductionStep;
                _activePartId = _recoveryCheckpoint.ActivePartId;
                _gripperClosed = _recoveryCheckpoint.GripperClosed;
                _cycleStartedSimulationTime = _recoveryCheckpoint.CycleStartedSimulationTime ?? _simulationTime;
                _order?.Resume();
                CommandTargetForCurrentStep();
                IncrementRevision("Simulation recovery resumed from checkpoint");
                Record("recovery.completed", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId, "resume-simulation", "Simulation-only checkpoint resume."));
                _recoveryCheckpoint = null;
                return Accept(id, correlation, "RECOVERY_RESUMED");
            default:
                return Reject(id, correlation, "UNKNOWN_RECOVERY_ACTION", "Unknown recovery action.");
        }
    }

    private void DiscardInterruptedPart()
    {
        if (_activePartId is not null)
        {
            _rejectCount++;
            Record("part.discarded", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId, "part-discarded", "Interrupted part discarded during recovery."));
        }
        _activePartId = null;
        _gripperClosed = false;
        _cycleStartedSimulationTime = null;
    }

    private CommandResult Jog(string id, string? axis, double? distance, string correlation)
    {
        if (_mode is not (MachineMode.Manual or MachineMode.Maintenance)) return Reject(id, correlation, "MODE_NOT_MANUAL", "Jog requires Manual or Maintenance mode.");
        if (_execution is ExecutionState.Running or ExecutionState.Starting or ExecutionState.Homing or ExecutionState.Faulted or ExecutionState.RecoveryRequired)
            return Reject(id, correlation, "INVALID_STATE", "Jog is blocked in the current execution state.");
        var motion = _motion.Snapshot(AxisMotionState.Standstill);
        var interlocks = BuildInterlocks(motion);
        if (!interlocks.MotionPermitted) return Reject(id, correlation, "MOTION_NOT_PERMITTED", interlocks.BlockingReasons.ToArray());
        if (distance is null or < -0.2 or > 0.2) return Reject(id, correlation, "INVALID_DISTANCE", "Jog distance must be between -0.2 and 0.2 simulation units.");
        if (!motion.AllHomed) return Reject(id, correlation, "AXIS_NOT_HOMED", "Axes must be homed before jogging.");

        var targetX = motion.XAxis.TargetPosition;
        var targetY = motion.YAxis.TargetPosition;
        if (string.Equals(axis, "X", StringComparison.OrdinalIgnoreCase)) targetX = Math.Clamp(motion.XAxis.ActualPosition + distance.Value, -1.0, 1.0);
        else if (string.Equals(axis, "Y", StringComparison.OrdinalIgnoreCase)) targetY = Math.Clamp(motion.YAxis.ActualPosition + distance.Value, 0.0, 1.2);
        else return Reject(id, correlation, "INVALID_AXIS", "Axis must be X or Y.");

        _motion.EnableAll();
        _motion.SetTarget(targetX, targetY);
        Transition(ExecutionState.Ready, ProductionStep.None, $"Manual jog {axis?.ToUpperInvariant()}");
        return Accept(id, correlation);
    }

    private CommandResult InjectFault(string id, string? fault, string correlation)
    {
        switch (fault?.Trim().ToLowerInvariant())
        {
            case "emergency-stop": _emergencyStopOk = false; break;
            case "guard-open": _guardClosed = false; break;
            case "bus-loss": _busHealthy = false; break;
            case "drive-fault-x": _motion.InjectFault(MotionFault.DriveFaultX); break;
            case "drive-fault-y": _motion.InjectFault(MotionFault.DriveFaultY); break;
            case "following-error": _motion.InjectFault(MotionFault.FollowingError); break;
            case "frozen-axis-x": _motion.InjectFault(MotionFault.FrozenAxisX); break;
            case "frozen-axis-y": _motion.InjectFault(MotionFault.FrozenAxisY); break;
            default: return Reject(id, correlation, "UNKNOWN_FAULT", "Fault must be emergency-stop, guard-open, bus-loss, drive-fault-x, drive-fault-y, following-error, frozen-axis-x, or frozen-axis-y.");
        }
        RaiseFault("INJECTED_FAULT", "Simulation", AlarmSeverity.Error, $"Injected fault: {fault}", "Clear the injected condition, acknowledge alarms, and reset.");
        return Accept(id, correlation, "FAULT_INJECTED");
    }

    private CommandResult ClearFault(string id, string? fault, string correlation)
    {
        switch (fault?.Trim().ToLowerInvariant())
        {
            case "emergency-stop": _emergencyStopOk = true; break;
            case "guard-open": _guardClosed = true; break;
            case "bus-loss": _busHealthy = true; break;
            case "drive-fault-x": _motion.ClearFault(MotionFault.DriveFaultX); break;
            case "drive-fault-y": _motion.ClearFault(MotionFault.DriveFaultY); break;
            case "following-error": _motion.ClearFault(MotionFault.FollowingError); break;
            case "frozen-axis-x": _motion.ClearFault(MotionFault.FrozenAxisX); break;
            case "frozen-axis-y": _motion.ClearFault(MotionFault.FrozenAxisY); break;
            case "all": _emergencyStopOk = true; _guardClosed = true; _busHealthy = true; _motion.ClearFault(); break;
            default: return Reject(id, correlation, "UNKNOWN_FAULT", "Specify a known fault or all.");
        }
        IncrementRevision("Injected condition cleared");
        Record("fault.condition-cleared", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId, "fault-cleared", fault ?? "unknown"));
        return Accept(id, correlation, "CONDITION_CLEARED");
    }

    private CommandResult AcknowledgeAlarms(string id, string user, string correlation)
    {
        foreach (var alarm in _alarms.Values)
        {
            var before = alarm.Lifecycle;
            alarm.Acknowledge(user);
            if (alarm.Lifecycle != before) Record("alarm.acknowledged", new AlarmEventPayload(alarm.ToSnapshot(), "acknowledged"));
        }
        IncrementRevision("Alarms acknowledged");
        return Accept(id, correlation);
    }

    private void AdvanceProduction(double dt, MotionSystemSnapshot motion)
    {
        switch (_step)
        {
            case ProductionStep.MoveToPick when motion.AtTarget:
                _gripperClosed = true;
                _dwellRemaining = _recipe.PickDwellSeconds;
                Transition(ExecutionState.Running, ProductionStep.Pick, "Part picked");
                Record("traceability", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId, "picked", "Part attached to gripper."));
                break;
            case ProductionStep.Pick:
                if ((_dwellRemaining -= dt) <= 0)
                {
                    Transition(ExecutionState.Running, ProductionStep.MoveToInspect, "Moving to inspection");
                    CommandPosition(_recipe.Inspect);
                }
                break;
            case ProductionStep.MoveToInspect when motion.AtTarget:
                _dwellRemaining = _recipe.InspectDwellSeconds;
                Transition(ExecutionState.Running, ProductionStep.Inspect, "Inspection started");
                break;
            case ProductionStep.Inspect:
                if ((_dwellRemaining -= dt) <= 0)
                {
                    Transition(ExecutionState.Running, ProductionStep.MoveToPlace, "Inspection passed");
                    Record("traceability", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId, "inspected", "Deterministic inspection result: good."));
                    CommandPosition(_recipe.Place);
                }
                break;
            case ProductionStep.MoveToPlace when motion.AtTarget:
                _gripperClosed = false;
                _dwellRemaining = _recipe.PlaceDwellSeconds;
                Transition(ExecutionState.Running, ProductionStep.Place, "Part placed");
                break;
            case ProductionStep.Place:
                if ((_dwellRemaining -= dt) <= 0)
                {
                    Transition(ExecutionState.Running, ProductionStep.ReturnHome, "Returning home");
                    CommandPosition(_recipe.Home);
                }
                break;
            case ProductionStep.ReturnHome when motion.AtTarget:
                var orderCompleted = CompleteCycle();
                if (orderCompleted)
                {
                    Transition(ExecutionState.Ready, ProductionStep.Complete, "Production order completed");
                }
                else
                {
                    BeginCycle();
                    Transition(ExecutionState.Running, ProductionStep.MoveToPick, "Next cycle started");
                    CommandPosition(_recipe.Pick);
                }
                break;
        }
    }

    private void CommandTargetForCurrentStep()
    {
        var motion = _motion.Snapshot(AxisMotionState.Holding);
        var target = _step switch
        {
            ProductionStep.MoveToPick => _recipe.Pick,
            ProductionStep.MoveToInspect => _recipe.Inspect,
            ProductionStep.MoveToPlace => _recipe.Place,
            ProductionStep.ReturnHome => _recipe.Home,
            _ => new Position2D(motion.XAxis.ActualPosition, motion.YAxis.ActualPosition)
        };
        CommandPosition(target);
    }

    private void BeginCycle()
    {
        if (_order is null) throw new InvalidOperationException("Cannot begin a cycle without a production order.");
        _activePartId = $"{_order.OrderId}-PART-{_order.CompletedQuantity + 1:000000}";
        _cycleStartedSimulationTime = _simulationTime;
        Record("cycle.started", new CycleStartedPayload(_order.OrderId, _activePartId, _recipe.RecipeId, _recipe.Revision, _simulationTime));
    }

    private bool CompleteCycle()
    {
        if (_order is null || _activePartId is null) throw new InvalidOperationException("Cycle completion requires an active order and part.");
        _cycleCount++;
        _goodCount++;
        _lastCycleSeconds = _cycleStartedSimulationTime is null ? 0 : Math.Max(0, _simulationTime - _cycleStartedSimulationTime.Value);
        var payload = new CycleCompletedPayload(_order.OrderId, _activePartId, _cycleCount, PartQuality.Good, _lastCycleSeconds, _recipe.RecipeId, _recipe.Revision);
        Record("cycle.completed", payload);
        MachineTelemetry.RecordCycle(_lastCycleSeconds, PartQuality.Good);
        var completed = _order.RecordCompletedPart();
        Record("order.status", new OrderStatusChangedPayload(_order.OrderId, _order.Status, _order.CompletedQuantity,
            completed ? "target-quantity-reached" : "part-completed"));
        _activePartId = null;
        _cycleStartedSimulationTime = null;
        return completed;
    }

    private void CommandPosition(Position2D position)
    {
        _motion.EnableAll();
        _motion.SetTarget(position.X, position.Y);
    }

    private InterlockSnapshot BuildInterlocks(MotionSystemSnapshot motion)
    {
        var reasons = new List<string>();
        if (!_emergencyStopOk) reasons.Add("Emergency stop is active.");
        if (!_guardClosed) reasons.Add("Safety guard is open.");
        if (!_busHealthy) reasons.Add("Simulated control bus is unavailable.");
        if (motion.AnyFaulted) reasons.Add("One or more axes are faulted.");
        return new InterlockSnapshot(_emergencyStopOk, _guardClosed, _busHealthy, !motion.AnyFaulted, reasons.Count == 0, reasons);
    }

    private void RaiseFault(string code, string source, AlarmSeverity severity, string message, string action)
    {
        if (!_alarms.TryGetValue(code, out var existing) || existing.Lifecycle == AlarmLifecycle.Historical)
        {
            var alarm = new AlarmRecord(code, source, severity, message, action, _correlationId);
            _alarms[code] = alarm;
            Record("alarm.raised", new AlarmEventPayload(alarm.ToSnapshot(), "raised"));
            MachineTelemetry.RecordAlarm(code, severity);
        }
        _motion.Hold();
        Transition(ExecutionState.Faulted, _step, $"Fault: {code}");
    }

    private void Transition(ExecutionState execution, ProductionStep step, string reason)
    {
        if (_execution == execution && _step == step) return;
        var previousExecution = _execution;
        var previousStep = _step;
        _execution = execution;
        _step = step;
        IncrementRevision(reason);
        Record("machine.transition", new TraceabilityPayload(_order?.OrderId ?? "none", _activePartId,
            "state-transition", $"{previousExecution}/{previousStep} -> {_execution}/{_step}: {reason}"));
        MachineTelemetry.RecordTransition(_execution, _step);
    }

    private void IncrementRevision(string transition)
    {
        _revision++;
        _lastTransition = transition;
    }

    private void Record(string type, object payload) => _events.Enqueue(new MachineEvent(
        Guid.NewGuid().ToString("N"), type, DateTimeOffset.UtcNow, _correlationId, _revision, payload));

    private CommandResult Accept(string id, string correlation, string code = "ACCEPTED") => CommandResult.Accepted(id, _revision, correlation, code);
    private CommandResult Reject(string id, string correlation, string code, params string[] reasons) => CommandResult.Rejected(id, _revision, correlation, code, reasons);

    private AxisMotionState MotionStateForExecution() => _execution switch
    {
        ExecutionState.Homing or ExecutionState.Recovering => AxisMotionState.Homing,
        ExecutionState.Starting or ExecutionState.Running or ExecutionState.Stopping => AxisMotionState.Moving,
        ExecutionState.Pausing or ExecutionState.Paused or ExecutionState.Ready or ExecutionState.RecoveryRequired => AxisMotionState.Holding,
        ExecutionState.Faulted => AxisMotionState.Faulted,
        _ => AxisMotionState.Standstill
    };

    private MachineStateSnapshot SnapshotUnsafe()
    {
        var motion = _motion.Snapshot(MotionStateForExecution());
        var availability = _execution == ExecutionState.Faulted ? 0.92 : 0.98;
        var performance = _cycleCount == 0 ? 1.0 : Math.Clamp(5.0 / Math.Max(5.0, _lastCycleSeconds), 0.0, 1.0);
        var quality = _cycleCount == 0 ? 1.0 : (double)_goodCount / Math.Max(1, _goodCount + _rejectCount);
        var production = new ProductionSnapshot(_cycleCount, _goodCount, _rejectCount, _order?.ToSnapshot(), _activePartId,
            _lastCycleSeconds, availability, performance, quality, availability * performance * quality);
        var runtime = new RuntimeSnapshot(_runtimeMetrics.TargetLoopMilliseconds, _runtimeMetrics.LastLoopMilliseconds,
            _runtimeMetrics.MaximumLoopMilliseconds, _runtimeMetrics.DeadlineMisses, _runtimeMetrics.QueueDepth,
            _runtimeMetrics.ConnectedClients, _runtimeMetrics.StartedAt, _runtimeMetrics.Uptime);
        var activePart = _activePartId is null ? null : new PartSnapshot(_activePartId, _gripperClosed, PartQuality.Unknown, _recipe.RecipeId, _recipe.Revision);
        var allowedRecovery = _execution == ExecutionState.RecoveryRequired
            ? _recoveryCheckpoint?.ActiveOrderId is not null && _recoveryCheckpoint.ActivePartId is not null
                ? new[] { RecoveryAction.DiscardAndReset, RecoveryAction.RehomeAndReset, RecoveryAction.ResumeSimulation }
                : new[] { RecoveryAction.DiscardAndReset, RecoveryAction.RehomeAndReset }
            : Array.Empty<RecoveryAction>();
        var recovery = new RecoverySnapshot(_execution == ExecutionState.RecoveryRequired, _recoveryCheckpoint?.Reason ?? string.Empty, allowedRecovery);

        return new MachineStateSnapshot(
            _revision, DateTimeOffset.UtcNow, _simulationTime, _mode, _execution, _step, _gripperClosed,
            motion.XAxis, motion.YAxis, BuildInterlocks(motion), activePart,
            _alarms.Values.Where(alarm => alarm.Lifecycle != AlarmLifecycle.Historical).Select(alarm => alarm.ToSnapshot()).ToArray(),
            production, runtime, _recipe.ToSnapshot(), _integrationStatus.Snapshot(), recovery, _lastTransition, _correlationId);
    }
}
