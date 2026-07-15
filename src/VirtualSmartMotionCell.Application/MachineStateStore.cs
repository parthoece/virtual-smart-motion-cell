using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Application;

public sealed class MachineStateStore
{
    private MachineStateSnapshot? _current;
    public MachineStateSnapshot? Current => Volatile.Read(ref _current);
    public void Publish(MachineStateSnapshot snapshot) => Volatile.Write(ref _current, snapshot);
}
