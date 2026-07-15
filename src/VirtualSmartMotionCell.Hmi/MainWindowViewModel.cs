using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Hmi;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly MachineClient _client;
    private readonly CancellationTokenSource _cts = new();
    private MachineStateSnapshot? _state;
    private string _connection = "Connecting";
    private string _lastCommand = "No command sent";
    private int _demoOrderSequence = 1;

    public MainWindowViewModel(string endpoint)
    {
        Endpoint = endpoint;
        _client = new MachineClient(endpoint);

        InitializeCommand = Command("initialize");
        HomeCommand = Command("home");
        StartCommand = Command("start");
        PauseCommand = Command("pause");
        ResumeCommand = Command("resume");
        StopCommand = Command("stop");
        AbortCommand = Command("abort");
        ResetCommand = Command("reset");

        OfflineModeCommand = Command("set-mode", mode: "Offline");
        ManualModeCommand = Command("set-mode", mode: "Manual");
        AutomaticModeCommand = Command("set-mode", mode: "Automatic");
        MaintenanceModeCommand = Command("set-mode", mode: "Maintenance");

        LoadDemoOrderCommand = new AsyncCommand(LoadDemoOrderAsync);
        CancelOrderCommand = Command("cancel-order");
        ActivateDefaultRecipeCommand = Command("activate-recipe", recipeId: "standard-widget", recipeRevision: 1);

        JogXPositiveCommand = Command("jog", axis: "X", value: 0.05);
        JogXNegativeCommand = Command("jog", axis: "X", value: -0.05);
        JogYPositiveCommand = Command("jog", axis: "Y", value: 0.05);
        JogYNegativeCommand = Command("jog", axis: "Y", value: -0.05);

        EmergencyStopCommand = Command("inject-fault", fault: "emergency-stop");
        GuardOpenCommand = Command("inject-fault", fault: "guard-open");
        BusLossCommand = Command("inject-fault", fault: "bus-loss");
        DriveFaultXCommand = Command("inject-fault", fault: "drive-fault-x");
        DriveFaultYCommand = Command("inject-fault", fault: "drive-fault-y");
        FrozenAxisXCommand = Command("inject-fault", fault: "frozen-axis-x");
        FollowingErrorCommand = Command("inject-fault", fault: "following-error");
        ClearFaultsCommand = Command("clear-fault", fault: "all");

        AcknowledgeCommand = Command("acknowledge-alarms");
        RecoverDiscardCommand = Command("recover-discard");
        RecoverRehomeCommand = Command("recover-rehome");
        RecoverResumeCommand = Command("recover-resume");

        _ = Task.Run(() => _client.StreamAsync(UpdateAsync, _cts.Token));
    }

    public string Endpoint { get; }
    public string ViewerUrl => Endpoint.TrimEnd('/') + "/";
    public string Connection { get => _connection; private set => Set(ref _connection, value); }
    public string LastCommand { get => _lastCommand; private set => Set(ref _lastCommand, value); }

    public string Mode => _state?.Mode.ToString() ?? "--";
    public string ExecutionState => _state?.ExecutionState.ToString() ?? "--";
    public string ProductionStep => _state?.ProductionStep.ToString() ?? "--";
    public string LastTransition => _state?.LastTransition ?? "--";
    public string CorrelationId => _state?.CorrelationId ?? "--";
    public string Revision => _state?.Revision.ToString() ?? "0";
    public string SimulationTime => _state is null ? "--" : $"{_state.SimulationTime:F2} s";

    public string XPosition => Format(_state?.XAxis.ActualPosition);
    public string XTarget => Format(_state?.XAxis.TargetPosition);
    public string XVelocity => Format(_state?.XAxis.ActualVelocity);
    public string XError => Format(_state?.XAxis.FollowingError);
    public string XState => _state?.XAxis.MotionState.ToString() ?? "--";
    public string XSummary => _state is null ? "--" : $"Target {XTarget} · Velocity {XVelocity} · Error {XError}";
    public string YPosition => Format(_state?.YAxis.ActualPosition);
    public string YTarget => Format(_state?.YAxis.TargetPosition);
    public string YVelocity => Format(_state?.YAxis.ActualVelocity);
    public string YError => Format(_state?.YAxis.FollowingError);
    public string YState => _state?.YAxis.MotionState.ToString() ?? "--";
    public string YSummary => _state is null ? "--" : $"Target {YTarget} · Velocity {YVelocity} · Error {YError}";

    public string OrderId => _state?.Production.ActiveOrder?.OrderId ?? "No order loaded";
    public string OrderStatus => _state?.Production.ActiveOrder?.Status.ToString() ?? "--";
    public string OrderProgress => _state?.Production.ActiveOrder is { } order ? $"{order.CompletedQuantity} / {order.TargetQuantity}" : "0 / 0";
    public string ActivePart => _state?.ActivePart?.PartId ?? "--";
    public string Cycles => _state?.Production.CycleCount.ToString() ?? "0";
    public string GoodCount => _state?.Production.GoodCount.ToString() ?? "0";
    public string RejectCount => _state?.Production.RejectCount.ToString() ?? "0";
    public string LastCycle => _state is null ? "--" : $"{_state.Production.LastCycleSeconds:F2} s";
    public string Oee => _state is null ? "--" : $"{_state.Production.Oee:P1}";
    public string OeeComponents => _state is null ? "--" : $"A {_state.Production.Availability:P0} · P {_state.Production.Performance:P0} · Q {_state.Production.Quality:P0}";

    public string Recipe => _state is null ? "--" : $"{_state.ActiveRecipe.RecipeId} r{_state.ActiveRecipe.Revision}";
    public string RecipeLifecycle => _state?.ActiveRecipe.Lifecycle.ToString() ?? "--";
    public string RecipeChecksum => ShortChecksum(_state?.ActiveRecipe.Checksum);

    public string MesHealth => _state is null ? "--" : $"{_state.Integration.MesHealth}: {_state.Integration.MesStatus}";
    public string Outbox => _state is null ? "--" : $"{_state.Integration.OutboxPending} pending";
    public string OpcUa => _state is null ? "--" : _state.Integration.OpcUaEnabled ? _state.Integration.OpcUaEndpoint : "Disabled";

    public string LoopTiming => _state is null ? "--" : $"{_state.Runtime.LastLoopDurationMilliseconds:F2} ms / {_state.Runtime.LoopPeriodMilliseconds:F1} ms";
    public string MaxLoop => _state is null ? "--" : $"{_state.Runtime.MaximumLoopDurationMilliseconds:F2} ms";
    public string DeadlineMisses => _state?.Runtime.DeadlineMissCount.ToString() ?? "0";
    public string QueueDepth => _state?.Runtime.CommandQueueDepth.ToString() ?? "0";
    public string Clients => _state?.Runtime.ConnectedClients.ToString() ?? "0";
    public string Uptime => _state?.Runtime.Uptime.ToString("dd\\.hh\\:mm\\:ss") ?? "--";

    public string Interlocks => _state is null
        ? "Waiting for runtime"
        : _state.Interlocks.MotionPermitted ? "Motion permitted" : string.Join(" | ", _state.Interlocks.BlockingReasons);
    public string InterlockDetail => _state is null ? "--" :
        $"E-stop {_state.Interlocks.EmergencyStopOk} · Guard {_state.Interlocks.GuardClosed} · Bus {_state.Interlocks.BusHealthy} · Drives {_state.Interlocks.DrivesHealthy}";
    public string Alarms => _state is null || _state.ActiveAlarms.Count == 0
        ? "No active alarms"
        : string.Join(Environment.NewLine, _state.ActiveAlarms.Select(a => $"[{a.Severity}] {a.Code} · {a.Lifecycle}\n{a.Message}\nAction: {a.RecommendedAction}"));
    public string Recovery => _state is null || !_state.Recovery.Required
        ? "Recovery is not required"
        : $"{_state.Recovery.Reason}\nAllowed: {string.Join(", ", _state.Recovery.AllowedActions)}";

    public AsyncCommand InitializeCommand { get; }
    public AsyncCommand HomeCommand { get; }
    public AsyncCommand StartCommand { get; }
    public AsyncCommand PauseCommand { get; }
    public AsyncCommand ResumeCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand AbortCommand { get; }
    public AsyncCommand ResetCommand { get; }
    public AsyncCommand OfflineModeCommand { get; }
    public AsyncCommand AutomaticModeCommand { get; }
    public AsyncCommand ManualModeCommand { get; }
    public AsyncCommand MaintenanceModeCommand { get; }
    public AsyncCommand LoadDemoOrderCommand { get; }
    public AsyncCommand CancelOrderCommand { get; }
    public AsyncCommand ActivateDefaultRecipeCommand { get; }
    public AsyncCommand JogXPositiveCommand { get; }
    public AsyncCommand JogXNegativeCommand { get; }
    public AsyncCommand JogYPositiveCommand { get; }
    public AsyncCommand JogYNegativeCommand { get; }
    public AsyncCommand EmergencyStopCommand { get; }
    public AsyncCommand GuardOpenCommand { get; }
    public AsyncCommand BusLossCommand { get; }
    public AsyncCommand DriveFaultXCommand { get; }
    public AsyncCommand DriveFaultYCommand { get; }
    public AsyncCommand FrozenAxisXCommand { get; }
    public AsyncCommand FollowingErrorCommand { get; }
    public AsyncCommand ClearFaultsCommand { get; }
    public AsyncCommand AcknowledgeCommand { get; }
    public AsyncCommand RecoverDiscardCommand { get; }
    public AsyncCommand RecoverRehomeCommand { get; }
    public AsyncCommand RecoverResumeCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private Task LoadDemoOrderAsync()
    {
        var orderId = $"DEMO-{DateTimeOffset.UtcNow:yyyyMMdd}-{_demoOrderSequence++:D3}";
        return SendAsync(new MachineCommandRequest(
            "load-order", RequestedBy: Environment.UserName, OrderId: orderId, Quantity: 5,
            RecipeId: "standard-widget", RecipeRevision: 1));
    }

    private AsyncCommand Command(
        string type,
        string? axis = null,
        double? value = null,
        string? mode = null,
        string? fault = null,
        string? recipeId = null,
        int? recipeRevision = null) => new(() => SendAsync(new MachineCommandRequest(
            type, Axis: axis, Value: value, Mode: mode, Fault: fault, RequestedBy: Environment.UserName,
            RecipeId: recipeId, RecipeRevision: recipeRevision)));

    private async Task SendAsync(MachineCommandRequest request)
    {
        try
        {
            var result = await _client.SendAsync(request, _cts.Token).ConfigureAwait(false);
            var message = result is null
                ? "No response"
                : $"{result.Status}: {result.ReasonCode}{(result.Reasons.Count == 0 ? string.Empty : " — " + string.Join("; ", result.Reasons))}";
            Dispatcher.UIThread.Post(() => LastCommand = message);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() => LastCommand = "Command failed: " + exception.Message);
        }
    }

    private Task UpdateAsync(MachineStateSnapshot snapshot)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            _state = snapshot;
            Connection = "Connected";
            foreach (var property in SnapshotProperties)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
            completion.TrySetResult(true);
        });
        return completion.Task;
    }

    private static readonly string[] SnapshotProperties =
    {
        nameof(Mode), nameof(ExecutionState), nameof(ProductionStep), nameof(LastTransition), nameof(CorrelationId), nameof(Revision), nameof(SimulationTime),
        nameof(XPosition), nameof(XTarget), nameof(XVelocity), nameof(XError), nameof(XState), nameof(XSummary), nameof(YPosition), nameof(YTarget), nameof(YVelocity), nameof(YError), nameof(YState), nameof(YSummary),
        nameof(OrderId), nameof(OrderStatus), nameof(OrderProgress), nameof(ActivePart), nameof(Cycles), nameof(GoodCount), nameof(RejectCount), nameof(LastCycle), nameof(Oee), nameof(OeeComponents),
        nameof(Recipe), nameof(RecipeLifecycle), nameof(RecipeChecksum), nameof(MesHealth), nameof(Outbox), nameof(OpcUa),
        nameof(LoopTiming), nameof(MaxLoop), nameof(DeadlineMisses), nameof(QueueDepth), nameof(Clients), nameof(Uptime),
        nameof(Interlocks), nameof(InterlockDetail), nameof(Alarms), nameof(Recovery)
    };

    private static string Format(double? value) => value is null ? "--" : value.Value.ToString("F3");
    private static string ShortChecksum(string? checksum) => string.IsNullOrEmpty(checksum) ? "--" : checksum[..Math.Min(12, checksum.Length)];

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _client.DisposeAsync();
        _cts.Dispose();
    }
}
