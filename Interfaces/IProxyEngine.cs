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
        void Start();
        Task StopAsync(TimeSpan timeout);
    }
}
