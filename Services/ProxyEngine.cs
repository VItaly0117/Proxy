using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TelegramProxy.Interfaces;
using TelegramProxy.Models;

namespace TelegramProxy.Services
{
    public class ProxyEngine : IProxyEngine
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _rootCts;
        private readonly ConcurrentBag<Task> _clientTasks = new();
        private readonly Channel<TrafficEvent> _trafficChannel;

        public ChannelReader<TrafficEvent> TrafficReader => _trafficChannel.Reader;
        public event Action<string>? LogMessage;

        public ProxyEngine()
        {
            _trafficChannel = Channel.CreateBounded<TrafficEvent>(new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
        }

        public void Start(int localPort, string targetIp, int targetPort)
        {
            _rootCts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{localPort}/");
            _listener.Start();
            LogMessage?.Invoke($"Engine listening on ws://127.0.0.1:{localPort}");

            _ = AcceptLoopAsync(targetIp, targetPort, _rootCts.Token);
        }

        private async Task AcceptLoopAsync(string targetIp, int targetPort, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var context = await _listener!.GetContextAsync().ConfigureAwait(false);
                    if (context.Request.IsWebSocketRequest)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                        _clientTasks.Add(HandleClientAsync(wsContext.WebSocket, targetIp, targetPort, token));
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
            }
            catch (Exception) { }
        }

        private async Task HandleClientAsync(WebSocket ws, string targetIp, int targetPort, CancellationToken token)
        {
            using var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(targetIp, targetPort, token).ConfigureAwait(false);
                using var networkStream = tcpClient.GetStream();
                using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(token);

                var wsToTcp = RelayWsToTcpAsync(ws, networkStream, clientCts.Token);
                var tcpToWs = RelayTcpToWsAsync(networkStream, ws, clientCts.Token);

                await Task.WhenAny(wsToTcp, tcpToWs).ConfigureAwait(false);
                await clientCts.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { LogMessage?.Invoke($"Bridge error: {ex.Message}"); }
            finally
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None).ConfigureAwait(false); } catch {}
                ws.Dispose();
            }
        }

        private async Task RelayWsToTcpAsync(WebSocket ws, NetworkStream tcpStream, CancellationToken token)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new Memory<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    await tcpStream.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, result.Count), token).ConfigureAwait(false);
                    _trafficChannel.Writer.TryWrite(new TrafficEvent(result.Count, 0));
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private async Task RelayTcpToWsAsync(NetworkStream tcpStream, WebSocket ws, CancellationToken token)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = await tcpStream.ReadAsync(new Memory<byte>(buffer), token).ConfigureAwait(false);
                    if (read == 0) break;

                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.SendAsync(new ReadOnlyMemory<byte>(buffer, 0, read), WebSocketMessageType.Binary, true, token).ConfigureAwait(false);
                        _trafficChannel.Writer.TryWrite(new TrafficEvent(0, read));
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            if (_rootCts == null) return;
            await _rootCts.CancelAsync().ConfigureAwait(false);
            _listener?.Stop();
            _listener?.Close();

            var activeTasks = _clientTasks.Where(t => !t.IsCompleted).ToArray();
            if (activeTasks.Length > 0)
                await Task.WhenAny(Task.WhenAll(activeTasks), Task.Delay(timeout)).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            _rootCts?.Dispose();
        }
    }
}
