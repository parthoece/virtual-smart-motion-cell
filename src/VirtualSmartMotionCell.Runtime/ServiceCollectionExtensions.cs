using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtualSmartMotionCell.AdapterSdk;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Control;
using VirtualSmartMotionCell.Infrastructure;

namespace VirtualSmartMotionCell.Runtime;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualSmartMotionCell(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RuntimeOptions>(configuration.GetSection("Runtime"));
        services.Configure<MesOptions>(configuration.GetSection("Mes"));
        services.Configure<OpcUaOptions>(configuration.GetSection("OpcUa"));
        services.Configure<ObservabilityOptions>(configuration.GetSection("Observability"));
        services.PostConfigure<RuntimeOptions>(ApplyRuntimeEnvironment);

        var runtime = configuration.GetSection("Runtime").Get<RuntimeOptions>() ?? new RuntimeOptions();
        ApplyRuntimeEnvironment(runtime);
        var mes = configuration.GetSection("Mes").Get<MesOptions>() ?? new MesOptions();
        var opcUa = configuration.GetSection("OpcUa").Get<OpcUaOptions>() ?? new OpcUaOptions();
        var dataDirectory = runtime.DataDirectory;
        var recipePath = ResolveFilePath(runtime.RecipePath);
        var recipeStore = new JsonRecipeStore(recipePath);
        var recipe = recipeStore.LoadActiveAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

        services.AddSingleton<RuntimeMetrics>();
        services.AddSingleton<MachineStateStore>();
        services.AddSingleton<MachineCommandBus>();
        services.AddSingleton<StatePublicationQueue>();
        services.AddSingleton<IntegrationStatusStore>(provider =>
        {
            var status = new IntegrationStatusStore();
            status.ConfigureOpcUa(opcUa.Enabled, opcUa.Endpoint);
            return status;
        });
        services.AddSingleton(recipe);
        services.AddSingleton<IRecipeStore>(recipeStore);

        services.AddSingleton<IMotionSystem>(_ =>
        {
            IMotionSystem selected;
            if (string.Equals(runtime.MotionAdapter, "replay", StringComparison.OrdinalIgnoreCase))
            {
                var replayPath = ResolveFilePath(runtime.ReplayPath);
                var frames = JsonSerializer.Deserialize<MotionReplayFrame[]>(File.ReadAllText(replayPath), JsonDefaults.Options)
                    ?? throw new InvalidDataException($"Replay file '{replayPath}' did not contain any frames.");
                selected = new ReplayMotionSystem(frames);
            }
            else if (string.Equals(runtime.MotionAdapter, "simulated", StringComparison.OrdinalIgnoreCase))
            {
                selected = new SimulatedMotionSystem();
            }
            else
            {
                throw new InvalidDataException($"Unknown motion adapter '{runtime.MotionAdapter}'. Use simulated or replay.");
            }
            return new FaultInjectingMotionSystem(selected);
        });
        services.AddSingleton<IInspectionAdapter, SimulatedInspectionAdapter>();
        services.AddSingleton<MachineCoordinator>();

        services.AddSingleton<IMachineEventStore>(_ => new JsonlMachineEventStore(dataDirectory));
        services.AddSingleton<ICheckpointStore>(_ => new FileCheckpointStore(dataDirectory));
        services.AddSingleton<IOutboxStore>(_ => new FileOutboxStore(dataDirectory));
        services.AddSingleton<IProductionRepository>(_ => new FileProductionRepository(dataDirectory));
        services.AddSingleton<IAlarmHistoryStore>(_ => new FileAlarmHistoryStore(dataDirectory));

        services.AddHttpClient("mes", client =>
        {
            client.BaseAddress = new Uri(mes.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VirtualSmartMotionCell/1.0");
        });
        services.AddSingleton<IManufacturingGateway>(provider =>
        {
            if (string.Equals(mes.Mode, "File", StringComparison.OrdinalIgnoreCase))
                return new FileManufacturingGateway(dataDirectory);
            return new HttpManufacturingGateway(provider.GetRequiredService<IHttpClientFactory>().CreateClient("mes"), mes.MachineId);
        });

        services.AddHostedService<MachineCommandProcessorService>();
        services.AddHostedService<MachineRuntimeService>();
        services.AddHostedService<MachineStatePublicationService>();
        services.AddHostedService<OutboxDeliveryService>();
        services.AddHostedService<MesOrderPollingService>();
        return services;
    }

    private static void ApplyRuntimeEnvironment(RuntimeOptions configuredOptions)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("VSMC_SIMULATION_PERIOD_MS"), out var simulationPeriod))
            configuredOptions.SimulationPeriodMilliseconds = simulationPeriod;
        if (int.TryParse(Environment.GetEnvironmentVariable("VSMC_STATE_PUBLISH_PERIOD_MS"), out var publishPeriod))
            configuredOptions.StatePublishPeriodMilliseconds = publishPeriod;
        if (int.TryParse(Environment.GetEnvironmentVariable("VSMC_CHECKPOINT_PERIOD_MS"), out var checkpointPeriod))
            configuredOptions.CheckpointPeriodMilliseconds = checkpointPeriod;
        if (bool.TryParse(Environment.GetEnvironmentVariable("VSMC_ALLOW_REMOTE_COMMANDS"), out var allowRemote))
            configuredOptions.AllowRemoteCommands = allowRemote;
        configuredOptions.DataDirectory = Environment.GetEnvironmentVariable("VSMC_DATA_DIR") ?? configuredOptions.DataDirectory;
        configuredOptions.MotionAdapter = Environment.GetEnvironmentVariable("VSMC_MOTION_ADAPTER") ?? configuredOptions.MotionAdapter;
        configuredOptions.ReplayPath = Environment.GetEnvironmentVariable("VSMC_REPLAY_PATH") ?? configuredOptions.ReplayPath;
    }

    private static string ResolveFilePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var currentDirectoryPath = Path.GetFullPath(path);
        if (File.Exists(currentDirectoryPath)) return currentDirectoryPath;
        return Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));
    }
}
