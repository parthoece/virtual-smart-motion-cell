using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Domain;

namespace VirtualSmartMotionCell.Infrastructure;

internal static class FileStoreHelpers
{
    private static readonly JsonSerializerOptions JsonLineOptions =
        new(JsonDefaults.Options)
        {
            WriteIndented = false
        };

    public static string SerializeJsonLine<T>(T value) =>
        JsonSerializer.Serialize(value, JsonLineOptions) + Environment.NewLine;
    public static async ValueTask AppendJsonLineAsync<T>(string path, T value, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = SerializeJsonLine(value);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public static async ValueTask<IReadOnlyList<T>> ReadJsonLinesAsync<T>(
        string path,
        int maximum,
        CancellationToken cancellationToken,
        SemaphoreSlim? gate = null)
    {
        if (gate is not null) await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return Array.Empty<T>();
            var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            var result = new List<T>();
            foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(Math.Max(1, maximum)))
            {
                try
                {
                    var item = JsonSerializer.Deserialize<T>(line, JsonDefaults.Options);
                    if (item is not null) result.Add(item);
                }
                catch (JsonException)
                {
                    // Ignore a malformed or interrupted journal line; appenders are serialized and future lines remain readable.
                }
            }
            return result;
        }
        finally
        {
            if (gate is not null) gate.Release();
        }
    }
}

public sealed class JsonlMachineEventStore(string dataDirectory) : IMachineEventStore
{
    private readonly string _path = Path.Combine(dataDirectory, "events.jsonl");
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ValueTask AppendAsync(MachineEvent machineEvent, CancellationToken cancellationToken) =>
        FileStoreHelpers.AppendJsonLineAsync(_path, machineEvent, _gate, cancellationToken);
}

public sealed class FileCheckpointStore(string dataDirectory) : ICheckpointStore
{
    private readonly string _path = Path.Combine(dataDirectory, "checkpoint.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask SaveAsync(RecoveryCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(checkpoint, JsonDefaults.Options), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, true);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<RecoveryCheckpoint?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<RecoveryCheckpoint>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
            if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
        }
        finally { _gate.Release(); }
    }
}

public sealed class FileOutboxStore : IOutboxStore
{
    private readonly string _pending;
    private readonly string _delivered;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileOutboxStore(string dataDirectory)
    {
        var root = Path.Combine(dataDirectory, "outbox");
        _pending = Path.Combine(root, "pending");
        _delivered = Path.Combine(root, "delivered");
    }

    public async ValueTask EnqueueAsync(MachineEvent machineEvent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_pending);
            var path = Path.Combine(_pending, machineEvent.EventId + ".json");
            if (File.Exists(path)) return;
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(machineEvent, JsonDefaults.Options), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<MachineEvent>> ReadPendingAsync(int maximum, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_pending)) return Array.Empty<MachineEvent>();
            var result = new List<MachineEvent>();
            foreach (var path in Directory.EnumerateFiles(_pending, "*.json").OrderBy(path => path).Take(Math.Max(1, maximum)))
            {
                await using var stream = File.OpenRead(path);
                var item = await JsonSerializer.DeserializeAsync<MachineEvent>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
                if (item is not null) result.Add(item);
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask MarkDeliveredAsync(string eventId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_delivered);
            var source = Path.Combine(_pending, eventId + ".json");
            if (File.Exists(source)) File.Move(source, Path.Combine(_delivered, eventId + ".json"), true);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<int> CountPendingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return Directory.Exists(_pending) ? Directory.EnumerateFiles(_pending, "*.json").Count() : 0; }
        finally { _gate.Release(); }
    }
}

public sealed class JsonRecipeStore
    : IRecipeStore
{
    private readonly string _initialRecipePath;
    private readonly string _directory;
    private readonly string _activePointer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonRecipeStore(string recipePath)
    {
        _initialRecipePath = recipePath;
        _directory = Path.GetDirectoryName(recipePath) ?? "config/recipes";
        _activePointer = Path.Combine(_directory, "active-recipe.json");
    }

    public async ValueTask<MachineRecipe> LoadActiveAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_activePointer))
        {
            await using var pointerStream = File.OpenRead(_activePointer);
            var pointer = await JsonSerializer.DeserializeAsync<RecipePointer>(pointerStream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
            if (pointer is not null && await LoadAsync(pointer.RecipeId, pointer.Revision, cancellationToken).ConfigureAwait(false) is { } selected)
                return selected.WithStatus(RecipeLifecycle.Active);
        }

        var initial = await ReadRecipeAsync(_initialRecipePath, cancellationToken).ConfigureAwait(false) ?? MachineRecipe.Default;
        var errors = initial.Validate(requireActivatable: false);
        if (errors.Count > 0) throw new InvalidDataException("Invalid recipe: " + string.Join(" | ", errors));
        return initial.WithStatus(RecipeLifecycle.Active);
    }

    public async ValueTask<IReadOnlyList<RecipeDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory)) return new[] { Descriptor(MachineRecipe.Default) };
        var result = new List<RecipeDescriptor>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").Where(path => !string.Equals(Path.GetFileName(path), "active-recipe.json", StringComparison.OrdinalIgnoreCase)))
        {
            var recipe = await ReadRecipeAsync(path, cancellationToken).ConfigureAwait(false);
            if (recipe is not null) result.Add(Descriptor(recipe));
        }
        return result.OrderBy(item => item.RecipeId).ThenBy(item => item.Revision).ToArray();
    }

    public async ValueTask<MachineRecipe?> LoadAsync(string recipeId, int revision, CancellationToken cancellationToken)
    {
        var path = RecipePath(recipeId, revision);
        if (File.Exists(path)) return await ReadRecipeAsync(path, cancellationToken).ConfigureAwait(false);
        if (File.Exists(_initialRecipePath))
        {
            var initial = await ReadRecipeAsync(_initialRecipePath, cancellationToken).ConfigureAwait(false);
            if (initial is not null && string.Equals(initial.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase) && initial.Revision == revision) return initial;
        }
        return null;
    }

    public async ValueTask SaveDraftAsync(MachineRecipe recipe, CancellationToken cancellationToken)
    {
        var draft = recipe.WithStatus(RecipeLifecycle.Draft);
        var errors = draft.Validate(requireActivatable: false);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" | ", errors));
        await WriteRecipeAsync(draft, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<MachineRecipe> ApproveAsync(string recipeId, int revision, CancellationToken cancellationToken)
    {
        var recipe = await LoadAsync(recipeId, revision, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Recipe was not found.", RecipePath(recipeId, revision));
        var approved = recipe.WithStatus(RecipeLifecycle.Approved);
        var errors = approved.Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" | ", errors));
        await WriteRecipeAsync(approved, cancellationToken).ConfigureAwait(false);
        return approved;
    }

    public async ValueTask<MachineRecipe> ActivateAsync(string recipeId, int revision, CancellationToken cancellationToken)
    {
        var recipe = await LoadAsync(recipeId, revision, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Recipe was not found.", RecipePath(recipeId, revision));
        if (recipe.Lifecycle is not (RecipeLifecycle.Approved or RecipeLifecycle.Active)) throw new InvalidOperationException("Only approved recipes may be activated.");
        var active = recipe.WithStatus(RecipeLifecycle.Active);
        await WriteRecipeAsync(active, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_activePointer, JsonSerializer.Serialize(new RecipePointer(recipeId, revision), JsonDefaults.Options), cancellationToken).ConfigureAwait(false);
        return active;
    }

    private async ValueTask WriteRecipeAsync(MachineRecipe recipe, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = RecipePath(recipe.RecipeId, recipe.Revision);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(recipe, JsonDefaults.Options), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { _gate.Release(); }
    }

    private static async ValueTask<MachineRecipe?> ReadRecipeAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MachineRecipe>(stream, JsonDefaults.Options, cancellationToken).ConfigureAwait(false);
    }

    private string RecipePath(string recipeId, int revision) => Path.Combine(_directory, $"{Sanitize(recipeId)}.v{revision}.json");
    private static string Sanitize(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
    private static RecipeDescriptor Descriptor(MachineRecipe recipe) => new(recipe.RecipeId, recipe.Revision, recipe.SchemaVersion, recipe.Lifecycle, recipe.Checksum, DateTimeOffset.UtcNow);
    private sealed record RecipePointer(string RecipeId, int Revision);
}

public sealed class FileProductionRepository : IProductionRepository
{
    private readonly string _ordersPath;
    private readonly string _partsPath;
    private readonly string _cyclesPath;
    private readonly string _tracePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileProductionRepository(string dataDirectory)
    {
        _ordersPath = Path.Combine(dataDirectory, "production", "orders.jsonl");
        _partsPath = Path.Combine(dataDirectory, "production", "parts.jsonl");
        _cyclesPath = Path.Combine(dataDirectory, "production", "cycles.jsonl");
        _tracePath = Path.Combine(dataDirectory, "production", "traceability.jsonl");
    }

    public async ValueTask HandleEventAsync(MachineEvent machineEvent, CancellationToken cancellationToken)
    {
        switch (machineEvent.Payload)
        {
            case OrderLoadedPayload loaded:
                await FileStoreHelpers.AppendJsonLineAsync(_ordersPath, OrderJournalEntry.Loaded(loaded, machineEvent.Timestamp), _gate, cancellationToken).ConfigureAwait(false);
                break;
            case OrderStatusChangedPayload status:
                await FileStoreHelpers.AppendJsonLineAsync(_ordersPath, OrderJournalEntry.StatusChanged(status, machineEvent.Timestamp), _gate, cancellationToken).ConfigureAwait(false);
                break;
            case CycleStartedPayload started:
                await AppendTraceAsync(new TraceabilityRecord(machineEvent.EventId, started.OrderId, started.PartId, "cycle-started", "Production cycle started.", machineEvent.Timestamp, machineEvent.CorrelationId), cancellationToken).ConfigureAwait(false);
                break;
            case CycleCompletedPayload completed:
                var completedAt = machineEvent.Timestamp;
                var startedAt = completedAt - TimeSpan.FromSeconds(completed.DurationSeconds);
                await FileStoreHelpers.AppendJsonLineAsync(_partsPath,
                    new PartRecord(completed.PartId, completed.OrderId, completed.RecipeId, completed.RecipeRevision, completed.Quality, startedAt, completedAt), _gate, cancellationToken).ConfigureAwait(false);
                await FileStoreHelpers.AppendJsonLineAsync(_cyclesPath,
                    new CycleRecord(machineEvent.EventId, completed.PartId, completed.OrderId, completed.CycleNumber, completed.DurationSeconds, completed.Quality, completedAt, machineEvent.CorrelationId), _gate, cancellationToken).ConfigureAwait(false);
                await AppendTraceAsync(new TraceabilityRecord(machineEvent.EventId, completed.OrderId, completed.PartId, "cycle-completed", $"Quality={completed.Quality}; duration={completed.DurationSeconds:F3}s", completedAt, machineEvent.CorrelationId), cancellationToken).ConfigureAwait(false);
                break;
            case TraceabilityPayload trace:
                await AppendTraceAsync(new TraceabilityRecord(machineEvent.EventId, trace.OrderId, trace.PartId, trace.EventName, trace.Details, machineEvent.Timestamp, machineEvent.CorrelationId), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public async ValueTask<IReadOnlyList<ProductionOrderSnapshot>> ReadOrdersAsync(int maximum, CancellationToken cancellationToken)
    {
        var journal = await FileStoreHelpers.ReadJsonLinesAsync<OrderJournalEntry>(_ordersPath, int.MaxValue, cancellationToken, _gate).ConfigureAwait(false);
        var states = new Dictionary<string, MutableOrder>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in journal)
        {
            if (!states.TryGetValue(entry.OrderId, out var state))
            {
                state = new MutableOrder(entry.OrderId, entry.TargetQuantity ?? 0, entry.RecipeId ?? "unknown", entry.RecipeRevision ?? 0, entry.Timestamp);
                states[entry.OrderId] = state;
            }
            state.TargetQuantity = entry.TargetQuantity ?? state.TargetQuantity;
            state.RecipeId = entry.RecipeId ?? state.RecipeId;
            state.RecipeRevision = entry.RecipeRevision ?? state.RecipeRevision;
            state.CompletedQuantity = entry.CompletedQuantity ?? state.CompletedQuantity;
            state.Status = entry.Status;
            if (entry.Status == ProductionOrderStatus.Completed) state.CompletedAt = entry.Timestamp;
        }
        return states.Values.OrderByDescending(state => state.LoadedAt).Take(maximum).Select(state => state.ToSnapshot()).ToArray();
    }

    public ValueTask<IReadOnlyList<PartRecord>> ReadPartsAsync(int maximum, CancellationToken cancellationToken) => FileStoreHelpers.ReadJsonLinesAsync<PartRecord>(_partsPath, maximum, cancellationToken, _gate);
    public ValueTask<IReadOnlyList<CycleRecord>> ReadCyclesAsync(int maximum, CancellationToken cancellationToken) => FileStoreHelpers.ReadJsonLinesAsync<CycleRecord>(_cyclesPath, maximum, cancellationToken, _gate);
    public ValueTask<IReadOnlyList<TraceabilityRecord>> ReadTraceabilityAsync(int maximum, CancellationToken cancellationToken) => FileStoreHelpers.ReadJsonLinesAsync<TraceabilityRecord>(_tracePath, maximum, cancellationToken, _gate);

    private ValueTask AppendTraceAsync(TraceabilityRecord record, CancellationToken cancellationToken) =>
        FileStoreHelpers.AppendJsonLineAsync(_tracePath, record, _gate, cancellationToken);

    private sealed record OrderJournalEntry(string OrderId, int? TargetQuantity, long? CompletedQuantity, string? RecipeId, int? RecipeRevision, ProductionOrderStatus Status, DateTimeOffset Timestamp)
    {
        public static OrderJournalEntry Loaded(OrderLoadedPayload value, DateTimeOffset timestamp) => new(value.OrderId, value.Quantity, 0, value.RecipeId, value.RecipeRevision, value.Status, timestamp);
        public static OrderJournalEntry StatusChanged(OrderStatusChangedPayload value, DateTimeOffset timestamp) => new(value.OrderId, null, value.CompletedQuantity, null, null, value.Status, timestamp);
    }

    private sealed class MutableOrder(string orderId, int targetQuantity, string recipeId, int recipeRevision, DateTimeOffset loadedAt)
    {
        public string OrderId { get; } = orderId;
        public int TargetQuantity { get; set; } = targetQuantity;
        public long CompletedQuantity { get; set; }
        public string RecipeId { get; set; } = recipeId;
        public int RecipeRevision { get; set; } = recipeRevision;
        public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Queued;
        public DateTimeOffset LoadedAt { get; } = loadedAt;
        public DateTimeOffset? CompletedAt { get; set; }
        public ProductionOrderSnapshot ToSnapshot() => new(OrderId, TargetQuantity, CompletedQuantity, RecipeId, RecipeRevision, Status, LoadedAt, CompletedAt);
    }
}

public sealed class FileAlarmHistoryStore(string dataDirectory) : IAlarmHistoryStore
{
    private readonly string _path = Path.Combine(dataDirectory, "alarms", "history.jsonl");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ValueTask AppendAsync(AlarmSnapshot alarm, string action, CancellationToken cancellationToken) =>
        FileStoreHelpers.AppendJsonLineAsync(_path, new AlarmEventPayload(alarm, action), _gate, cancellationToken);

    public ValueTask<IReadOnlyList<AlarmEventPayload>> ReadAsync(int maximum, CancellationToken cancellationToken) =>
        FileStoreHelpers.ReadJsonLinesAsync<AlarmEventPayload>(_path, maximum, cancellationToken, _gate);
}

public sealed class FileManufacturingGateway(string dataDirectory) : IManufacturingGateway
{
    private readonly string _deliveryPath = Path.Combine(dataDirectory, "manufacturing-delivery.jsonl");
    private readonly string _offlineMarker = Path.Combine(dataDirectory, "mes-offline.flag");
    private readonly string _receipts = Path.Combine(dataDirectory, "manufacturing-received");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<bool> DeliverAsync(MachineEvent machineEvent, CancellationToken cancellationToken)
    {
        if (File.Exists(_offlineMarker)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(_deliveryPath)!);
        Directory.CreateDirectory(_receipts);
        var receipt = Path.Combine(_receipts, machineEvent.EventId + ".received");
        if (File.Exists(receipt)) return true;
        var envelope = new { messageId = machineEvent.EventId, deliveredAt = DateTimeOffset.UtcNow, idempotencyKey = machineEvent.EventId, machineEvent };
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(receipt))
            {
                await File.AppendAllTextAsync(_deliveryPath, FileStoreHelpers.SerializeJsonLine(envelope), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(receipt, machineEvent.EventId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
        return true;
    }

    public ValueTask<(IntegrationHealth Health, string Status)> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(File.Exists(_offlineMarker)
            ? (IntegrationHealth.Offline, "file MES offline marker is present")
            : (IntegrationHealth.Healthy, "file MES gateway ready"));
    }
}

public sealed class HttpManufacturingGateway(HttpClient httpClient, string machineId) : IManufacturingGateway
{
    public async ValueTask<bool> DeliverAsync(MachineEvent machineEvent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/results")
        {
            Content = JsonContent.Create(new { machineId, messageId = machineEvent.EventId, idempotencyKey = machineEvent.EventId, machineEvent }, options: JsonDefaults.Options)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", machineEvent.EventId);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Conflict;
    }

    public async ValueTask<(IntegrationHealth Health, string Status)> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync("health/ready", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? (IntegrationHealth.Healthy, "HTTP MES ready")
                : (IntegrationHealth.Degraded, $"HTTP MES returned {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception)
        {
            return (IntegrationHealth.Offline, exception.Message);
        }
    }
}
