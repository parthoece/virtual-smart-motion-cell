using Opc.Ua;
using Opc.Ua.Server;
using VirtualSmartMotionCell.Application;

namespace VirtualSmartMotionCell.OpcUa;

public sealed class MachineOpcUaServer(MachineStateStore stateStore) : StandardServer
{
    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        var nodeManagers = new INodeManager[] { new MachineNodeManager(server, configuration, stateStore) };
        return new MasterNodeManager(server, configuration, null, nodeManagers);
    }

    protected override ServerProperties LoadServerProperties() => new()
    {
        ManufacturerName = "Virtual Smart Motion Cell contributors",
        ProductName = "Virtual Smart Motion Cell OPC UA simulation server",
        ProductUri = "https://github.com/YOUR_GITHUB_HANDLE/virtual-smart-motion-cell",
        SoftwareVersion = "0.5.0",
        BuildNumber = "3",
        BuildDate = DateTime.UtcNow
    };
}
