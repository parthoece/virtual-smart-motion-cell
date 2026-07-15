using System.Threading.Channels;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Runtime;

public sealed class StatePublicationQueue
{
    private readonly Channel<MachineStateSnapshot> _channel = Channel.CreateBounded<MachineStateSnapshot>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true
    });

    public ChannelReader<MachineStateSnapshot> Reader => _channel.Reader;
    public void Publish(MachineStateSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);
}
