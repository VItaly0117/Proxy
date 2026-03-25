using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TelegramProxy.Interfaces;
using TelegramProxy.Models;

namespace TelegramProxy.Services
{
    public class ProxyEngine : IProxyEngine
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _rootCts;
        private readonly ConcurrentBag<Task> _clientTasks = new();
        private readonly Channel<TrafficEvent> _trafficChannel;
        private readonly ISettingsManager _settingsManager;

        public ChannelReader<TrafficEvent> TrafficReader => _trafficChannel.Reader;
        public event Action<string>? LogMessage;

        public ProxyEngine(ISettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _trafficChannel = Channel.CreateBounded<TrafficEvent>(new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
        }

        public void Start()
        {
            _rootCts = new CancellationTokenSource();
            int localPort = _settingsManager.Current.LocalPort;
            _listener = new TcpListener(IPAddress.Any, localPort);
            _listener.Start();
            LogMessage?.Invoke($"SOCKS5 Engine listening on 0.0.0.0:{localPort}");

            _ = AcceptLoopAsync(_rootCts.Token);
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    _clientTasks.Add(HandleClientAsync(client, token));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogMessage?.Invoke($"Listener error: {ex.Message}"); }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(token);

                    if (!await HandleSocks5HandshakeAsync(stream, clientCts.Token).ConfigureAwait(false))
                        return;

                    if (!await HandleSocks5ConnectAsync(stream, out string? targetHost, out int targetPort, clientCts.Token).ConfigureAwait(false))
                        return;

                    using var targetClient = new TcpClient();
                    await targetClient.ConnectAsync(targetHost!, targetPort, clientCts.Token).ConfigureAwait(false);
                    using var targetStream = targetClient.GetStream();

                    byte[] successReply = new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
                    await stream.WriteAsync(successReply.AsMemory(), clientCts.Token).ConfigureAwait(false);

                    var clientToTarget = RelayAsync(stream, targetStream, 1, clientCts.Token); 
                    var targetToClient = RelayAsync(targetStream, stream, 0, clientCts.Token); 

                    await Task.WhenAny(clientToTarget, targetToClient).ConfigureAwait(false);
                    await clientCts.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is not OperationCanceledException) 
                { 
                    LogMessage?.Invoke($"Bridge error: {ex.Message}");
                }
            }
        }

        private async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken token)
        {
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(new Memory<byte>(buffer, offset, length - offset), token).ConfigureAwait(false);
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        private async Task<bool> HandleSocks5HandshakeAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] header = new byte[2];
            if (!await ReadExactAsync(stream, header, 2, token).ConfigureAwait(false)) return false;
            if (header[0] != 0x05) return false;

            int numMethods = header[1];
            byte[] methods = new byte[numMethods];
            if (!await ReadExactAsync(stream, methods, numMethods, token).ConfigureAwait(false)) return false;

            if (!methods.Contains((byte)0x02))
            {
                await stream.WriteAsync(new byte[] { 0x05, 0xFF }.AsMemory(), token).ConfigureAwait(false);
                return false;
            }

            await stream.WriteAsync(new byte[] { 0x05, 0x02 }.AsMemory(), token).ConfigureAwait(false);

            byte[] authHeader = new byte[2];
            if (!await ReadExactAsync(stream, authHeader, 2, token).ConfigureAwait(false)) return false;
            if (authHeader[0] != 0x01) return false; 

            int ulen = authHeader[1];
            byte[] uname = new byte[ulen];
            if (!await ReadExactAsync(stream, uname, ulen, token).ConfigureAwait(false)) return false;

            byte[] plenBuf = new byte[1];
            if (!await ReadExactAsync(stream, plenBuf, 1, token).ConfigureAwait(false)) return false;

            int plen = plenBuf[0];
            byte[] passwd = new byte[plen];
            if (!await ReadExactAsync(stream, passwd, plen, token).ConfigureAwait(false)) return false;

            string clientUser = Encoding.UTF8.GetString(uname);
            string clientPass = Encoding.UTF8.GetString(passwd);

            if (clientUser != _settingsManager.Current.SocksUsername || clientPass != _settingsManager.Current.SocksPassword)
            {
                await stream.WriteAsync(new byte[] { 0x01, 0xFF }.AsMemory(), token).ConfigureAwait(false);
                return false;
            }

            await stream.WriteAsync(new byte[] { 0x01, 0x00 }.AsMemory(), token).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> HandleSocks5ConnectAsync(NetworkStream stream, out string? targetHost, out int targetPort, CancellationToken token)
        {
            targetHost = null;
            targetPort = 0;

            byte[] req = new byte[4];
            if (!await ReadExactAsync(stream, req, 4, token).ConfigureAwait(false)) return false;

            if (req[0] != 0x05 || req[1] != 0x01) return false; 

            byte atyp = req[3];
            if (atyp == 0x01) 
            {
                byte[] ipBytes = new byte[4];
                if (!await ReadExactAsync(stream, ipBytes, 4, token).ConfigureAwait(false)) return false;
                targetHost = new IPAddress(ipBytes).ToString();
            }
            else if (atyp == 0x03) 
            {
                byte[] lenBuf = new byte[1];
                if (!await ReadExactAsync(stream, lenBuf, 1, token).ConfigureAwait(false)) return false;
                int len = lenBuf[0];
                byte[] domainBytes = new byte[len];
                if (!await ReadExactAsync(stream, domainBytes, len, token).ConfigureAwait(false)) return false;
                targetHost = Encoding.UTF8.GetString(domainBytes);
            }
            else if (atyp == 0x04) 
            {
                byte[] ipBytes = new byte[16];
                if (!await ReadExactAsync(stream, ipBytes, 16, token).ConfigureAwait(false)) return false;
                targetHost = new IPAddress(ipBytes).ToString();
            }
            else
            {
                return false; 
            }

            byte[] portBytes = new byte[2];
            if (!await ReadExactAsync(stream, portBytes, 2, token).ConfigureAwait(false)) return false;
            targetPort = (portBytes[0] << 8) | portBytes[1];

            return true;
        }

        private async Task RelayAsync(NetworkStream source, NetworkStream destination, int direction, CancellationToken token)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = await source.ReadAsync(new Memory<byte>(buffer), token).ConfigureAwait(false);
                    if (read == 0) break;

                    await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, read), token).ConfigureAwait(false);
                    
                    if (direction == 1)
                        _trafficChannel.Writer.TryWrite(new TrafficEvent(read, 0));
                    else
                        _trafficChannel.Writer.TryWrite(new TrafficEvent(0, read));
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            if (_rootCts == null) return;
            await _rootCts.CancelAsync().ConfigureAwait(false);
            _listener?.Stop();

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
