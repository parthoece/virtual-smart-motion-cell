using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Hmi;

public sealed class MachineClient(string endpoint) : IAsyncDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
    private readonly JsonSerializerOptions _json = CreateOptions();
    private ClientWebSocket? _socket;

    public async Task<CommandResult?> SendAsync(MachineCommandRequest command, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/v1/commands", command, _json, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<CommandResult>(_json, cancellationToken).ConfigureAwait(false);
    }

    public async Task StreamAsync(Func<MachineStateSnapshot, Task> onSnapshot, CancellationToken cancellationToken)
    {
        var wsUri = new UriBuilder(_http.BaseAddress!) { Scheme = _http.BaseAddress!.Scheme == "https" ? "wss" : "ws", Path = "/ws/state" }.Uri;
        while (!cancellationToken.IsCancellationRequested)
        {
            _socket?.Dispose();
            _socket = new ClientWebSocket();
            try
            {
                await _socket.ConnectAsync(wsUri, cancellationToken).ConfigureAwait(false);
                var buffer = new byte[128 * 1024];
                while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    var snapshot = JsonSerializer.Deserialize<MachineStateSnapshot>(Encoding.UTF8.GetString(message.ToArray()), _json);
                    if (snapshot is not null) await onSnapshot(snapshot).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { await Task.Delay(1500, cancellationToken).ConfigureAwait(false); }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket?.Dispose();
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
