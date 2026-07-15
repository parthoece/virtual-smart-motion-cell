using Opc.Ua;
using Opc.Ua.Server;
using VirtualSmartMotionCell.Application;

namespace VirtualSmartMotionCell.OpcUa;

public sealed class MachineNodeManager : CustomNodeManager2
{
    private readonly MachineStateStore _stateStore;
    private readonly object _updateGate = new();
    private BaseDataVariableState<string>? _mode;
    private BaseDataVariableState<string>? _executionState;
    private BaseDataVariableState<string>? _productionStep;
    private BaseDataVariableState<double>? _xPosition;
    private BaseDataVariableState<double>? _yPosition;
    private BaseDataVariableState<double>? _xFollowingError;
    private BaseDataVariableState<double>? _yFollowingError;
    private BaseDataVariableState<bool>? _motionPermitted;
    private BaseDataVariableState<long>? _cycleCount;
    private BaseDataVariableState<double>? _oee;
    private BaseDataVariableState<string>? _activeAlarm;
    private BaseDataVariableState<string>? _activeOrder;
    private BaseDataVariableState<long>? _completedQuantity;
    private BaseDataVariableState<long>? _targetQuantity;
    private BaseDataVariableState<string>? _activeRecipe;
    private BaseDataVariableState<string>? _mesHealth;
    private BaseDataVariableState<string>? _motionAdapter;
    private Timer? _timer;

    public MachineNodeManager(IServerInternal server, ApplicationConfiguration configuration, MachineStateStore stateStore)
        : base(server, configuration, "urn:virtual-smart-motion-cell:machine")
    {
        _stateStore = stateStore;
        SystemContext.NodeIdFactory = this;
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
        {
            references = new List<IReference>();
            externalReferences[ObjectIds.ObjectsFolder] = references;
        }

        var machine = CreateFolder(null, "Machine", "VirtualSmartMotionCell");
        machine.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
        references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, machine.NodeId));

        var state = CreateFolder(machine, "Machine/State", "State");
        _mode = CreateVariable<string>(state, "Machine/State/Mode", "Mode", DataTypeIds.String, string.Empty);
        _executionState = CreateVariable<string>(state, "Machine/State/ExecutionState", "ExecutionState", DataTypeIds.String, string.Empty);
        _productionStep = CreateVariable<string>(state, "Machine/State/ProductionStep", "ProductionStep", DataTypeIds.String, string.Empty);
        _motionPermitted = CreateVariable(state, "Machine/State/MotionPermitted", "MotionPermitted", DataTypeIds.Boolean, false);
        _activeAlarm = CreateVariable<string>(state, "Machine/State/ActiveAlarm", "ActiveAlarm", DataTypeIds.String, string.Empty);
        _motionAdapter = CreateVariable<string>(state, "Machine/State/MotionAdapter", "MotionAdapter", DataTypeIds.String, string.Empty);

        var axes = CreateFolder(machine, "Machine/Axes", "Axes");
        var x = CreateFolder(axes, "Machine/Axes/X", "X");
        _xPosition = CreateVariable(x, "Machine/Axes/X/ActualPosition", "ActualPosition", DataTypeIds.Double, 0.0);
        _xFollowingError = CreateVariable(x, "Machine/Axes/X/FollowingError", "FollowingError", DataTypeIds.Double, 0.0);
        var y = CreateFolder(axes, "Machine/Axes/Y", "Y");
        _yPosition = CreateVariable(y, "Machine/Axes/Y/ActualPosition", "ActualPosition", DataTypeIds.Double, 0.0);
        _yFollowingError = CreateVariable(y, "Machine/Axes/Y/FollowingError", "FollowingError", DataTypeIds.Double, 0.0);

        var production = CreateFolder(machine, "Machine/Production", "Production");
        _cycleCount = CreateVariable(production, "Machine/Production/CycleCount", "CycleCount", DataTypeIds.Int64, 0L);
        _oee = CreateVariable(production, "Machine/Production/Oee", "Oee", DataTypeIds.Double, 0.0);
        _activeOrder = CreateVariable<string>(production, "Machine/Production/ActiveOrder", "ActiveOrder", DataTypeIds.String, string.Empty);
        _completedQuantity = CreateVariable(production, "Machine/Production/CompletedQuantity", "CompletedQuantity", DataTypeIds.Int64, 0L);
        _targetQuantity = CreateVariable(production, "Machine/Production/TargetQuantity", "TargetQuantity", DataTypeIds.Int64, 0L);
        _activeRecipe = CreateVariable<string>(production, "Machine/Production/ActiveRecipe", "ActiveRecipe", DataTypeIds.String, string.Empty);

        var integration = CreateFolder(machine, "Machine/Integration", "Integration");
        _mesHealth = CreateVariable<string>(integration, "Machine/Integration/MesHealth", "MesHealth", DataTypeIds.String, string.Empty);

        AddPredefinedNode(SystemContext, machine);
        _timer = new Timer(_ => UpdateValues(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
    }

    private FolderState CreateFolder(NodeState? parent, string path, string name)
    {
        var folder = new FolderState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            NodeId = new NodeId(path, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = name,
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            EventNotifier = EventNotifiers.None
        };
        parent?.AddChild(folder);
        return folder;
    }

    private BaseDataVariableState<T> CreateVariable<T>(NodeState parent, string path, string name, NodeId dataType, T value)
    {
        var variable = new BaseDataVariableState<T>(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId(path, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = name,
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Historizing = false,
            Value = value,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };
        parent.AddChild(variable);
        return variable;
    }

    private void UpdateValues()
    {
        if (_timer is null) return;
        var snapshot = _stateStore.Current;
        if (snapshot is null) return;
        lock (_updateGate)
        {
            Update(_mode, snapshot.Mode.ToString());
            Update(_executionState, snapshot.ExecutionState.ToString());
            Update(_productionStep, snapshot.ProductionStep.ToString());
            Update(_xPosition, snapshot.XAxis.ActualPosition);
            Update(_yPosition, snapshot.YAxis.ActualPosition);
            Update(_xFollowingError, snapshot.XAxis.FollowingError);
            Update(_yFollowingError, snapshot.YAxis.FollowingError);
            Update(_motionPermitted, snapshot.Interlocks.MotionPermitted);
            Update(_cycleCount, snapshot.Production.CycleCount);
            Update(_oee, snapshot.Production.Oee);
            Update(_activeAlarm, snapshot.ActiveAlarms.FirstOrDefault()?.Code ?? string.Empty);
            Update(_motionAdapter, $"{snapshot.XAxis.Name}/{snapshot.YAxis.Name}");
            Update(_activeOrder, snapshot.Production.ActiveOrder?.OrderId ?? string.Empty);
            Update(_completedQuantity, snapshot.Production.ActiveOrder?.CompletedQuantity ?? 0L);
            Update(_targetQuantity, (long)(snapshot.Production.ActiveOrder?.TargetQuantity ?? 0));
            Update(_activeRecipe, $"{snapshot.ActiveRecipe.RecipeId}:v{snapshot.ActiveRecipe.Revision}");
            Update(_mesHealth, snapshot.Integration.MesHealth.ToString());
        }
    }

    private void Update<T>(BaseDataVariableState<T>? variable, T value)
    {
        if (variable is null) return;
        variable.Value = value;
        variable.Timestamp = DateTime.UtcNow;
        variable.StatusCode = StatusCodes.Good;
        variable.ClearChangeMasks(SystemContext, false);
    }
}
