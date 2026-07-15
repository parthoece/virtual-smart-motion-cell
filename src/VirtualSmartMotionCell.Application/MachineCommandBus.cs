using System.Diagnostics;
using System.Threading.Channels;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Application;

public sealed record QueuedMachineCommand(string CommandId, MachineCommandRequest Request, TaskCompletionSource<CommandResult> Completion, ActivityContext ParentContext);

public sealed class MachineCommandBus
{
    private readonly Channel<QueuedMachineCommand> _channel;
    private readonly RuntimeMetrics _metrics;
    private int _depth;

    public MachineCommandBus(RuntimeMetrics metrics, int capacity = 256)
    {
        _metrics = metrics;
        _channel = Channel.CreateBounded<QueuedMachineCommand>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<QueuedMachineCommand> Reader => _channel.Reader;

    public async ValueTask<CommandResult> SendAsync(MachineCommandRequest request, CancellationToken cancellationToken)
    {
        var id = $"CMD-{Guid.NewGuid():N}";
        var completion = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new QueuedMachineCommand(id, request, completion, Activity.Current?.Context ?? default);
        if (!_channel.Writer.TryWrite(queued))
            return CommandResult.Rejected(id, 0, request.CorrelationId ?? id, "COMMAND_QUEUE_FULL", "The bounded machine command queue is full.");

        var depth = Interlocked.Increment(ref _depth);
        _metrics.SetQueueDepth(depth);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task.ConfigureAwait(false);
    }

    public void MarkRead()
    {
        var depth = Math.Max(0, Interlocked.Decrement(ref _depth));
        _metrics.SetQueueDepth(depth);
    }
}
