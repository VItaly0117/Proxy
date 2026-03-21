using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TelegramProxy.Interfaces;

namespace TelegramProxy.Services
{
    public class CloudflareService : ICloudflareService
    {
        private Process? _process;
        public event Action<string>? LogMessage;
        public event Action<string>? UrlObtained;

        private async Task ExtractCloudflaredAsync(string targetPath, CancellationToken token)
        {
            if (File.Exists(targetPath)) return;
            LogMessage?.Invoke("Extracting Embedded cloudflared.exe...");
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TelegramProxy.Resources.cloudflared.exe");
            if (stream == null) throw new FileNotFoundException("Embedded cloudflared.exe not found. Did you add it to Resources and set Build Action to Embedded Resource?");
            
            using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fs, token).ConfigureAwait(false);
        }

        public async Task StartAsync(int localPort, CancellationToken token)
        {
            var exePath = Path.Combine(Path.GetTempPath(), "cloudflared.exe");
            try
            {
                await ExtractCloudflaredAsync(exePath, token).ConfigureAwait(false);

                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"tunnel --url http://127.0.0.1:{localPort}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                _process.OutputDataReceived += HandleOutput;
                _process.ErrorDataReceived += HandleOutput;

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Cloudflare start failed: {ex.Message}");
            }
        }

        private void HandleOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var match = Regex.Match(e.Data, @"https://[a-zA-Z0-9-]+\.trycloudflare\.com");
            if (match.Success) UrlObtained?.Invoke(match.Value);
        }

        public Task StopAsync()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(true);
                    _process.WaitForExit(2000);
                    _process.Dispose();
                }
            }
            catch { }
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }
    }
}
