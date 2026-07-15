using Microsoft.Extensions.DependencyInjection;

namespace VirtualSmartMotionCell.OpcUa;

public static class OpcUaServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualSmartMotionCellOpcUa(this IServiceCollection services)
    {
        services.AddHostedService<MachineOpcUaHostedService>();
        return services;
    }
}
