using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Contracts;
using VirtualSmartMotionCell.Infrastructure;

namespace VirtualSmartMotionCell.Api;

public sealed class WebSocketStatePublisher(RuntimeMetrics metrics, ILogger<WebSocketStatePublisher> logger) : IMachineStatePublisher
{
    private sealed class ClientConnection(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public Channel<byte[]> Outbound { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();

    public async Task AcceptAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var connection = new ClientConnection(socket);
        _clients[id] = connection;
        metrics.SetConnectedClients(_clients.Count);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var receiveTask = ReceiveUntilClosedAsync(connection, linkedCancellation.Token);
            var sendTask = SendSnapshotsAsync(connection, linkedCancellation.Token);
            await Task.WhenAny(receiveTask, sendTask).ConfigureAwait(false);
            linkedCancellation.Cancel();
            await IgnoreCancellationAsync(receiveTask).ConfigureAwait(false);
            await IgnoreCancellationAsync(sendTask).ConfigureAwait(false);
        }
        catch (WebSocketException exception)
        {
            logger.LogDebug(exception, "WebSocket client disconnected");
        }
        finally
        {
            _clients.TryRemove(id, out _);
            connection.Outbound.Writer.TryComplete();
            metrics.SetConnectedClients(_clients.Count);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException) { }
            }
            socket.Dispose();
        }
    }

    public ValueTask PublishAsync(MachineStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_clients.IsEmpty) return ValueTask.CompletedTask;

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonDefaults.Options));
        foreach (var connection in _clients.Values)
            connection.Outbound.Writer.TryWrite(payload);

        metrics.SetConnectedClients(_clients.Count);
        return ValueTask.CompletedTask;
    }

    private static async Task ReceiveUntilClosedAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (connection.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await connection.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return;
        }
    }

    private static async Task SendSnapshotsAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        await foreach (var payload in connection.Outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (connection.Socket.State != WebSocketState.Open) return;
            await connection.Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
    }
}
