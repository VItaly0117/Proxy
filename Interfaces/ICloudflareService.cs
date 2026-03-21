using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramProxy.Interfaces
{
    public interface ICloudflareService : IAsyncDisposable
    {
        event Action<string>? LogMessage;
        event Action<string>? UrlObtained;
        Task StartAsync(int localPort, CancellationToken token);
        Task StopAsync();
    }
}
