using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TelegramProxy.Models;

namespace TelegramProxy.Interfaces
{
    public interface IProxyEngine : IAsyncDisposable
    {
        ChannelReader<TrafficEvent> TrafficReader { get; }
        event Action<string>? LogMessage;
        void Start(int localPort, string targetIp, int targetPort);
        Task StopAsync(TimeSpan timeout);
    }
}
