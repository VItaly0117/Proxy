using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TelegramProxy.Infrastructure;
using TelegramProxy.Interfaces;

namespace TelegramProxy.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IProxyEngine _proxyEngine;
        private readonly ICloudflareService _cloudflareService;
        private readonly ISettingsManager _settingsManager;
        private Timer? _uiTimer;
        
        private long _accBytesSent;
        private long _accBytesReceived;
        
        public SettingsViewModel SettingsVM { get; }

        public MainViewModel(IProxyEngine proxyEngine, ICloudflareService cloudflareService, ISettingsManager settingsManager)
        {
            _proxyEngine = proxyEngine;
            _cloudflareService = cloudflareService;
            _settingsManager = settingsManager;
            SettingsVM = new SettingsViewModel(settingsManager);

            StartCommand = new RelayCommand(async _ => await StartAsync(), _ => !IsRunning);
            StopCommand = new RelayCommand(async _ => await StopAsync(), _ => IsRunning);

            UploadSeriesList = new ObservableCollection<double>(Enumerable.Repeat(0.0, 60));
            DownloadSeriesList = new ObservableCollection<double>(Enumerable.Repeat(0.0, 60));

            Series = new ISeries[]
            {
                new LineSeries<double> { Values = UploadSeriesList, Name = "Up (KB/s)", Stroke = new SolidColorPaint(new SKColor(0, 122, 204)) { StrokeThickness = 3 }, GeometrySize = 0, Fill = null },
                new LineSeries<double> { Values = DownloadSeriesList, Name = "Down (KB/s)", Stroke = new SolidColorPaint(new SKColor(46, 204, 113)) { StrokeThickness = 3 }, GeometrySize = 0, Fill = null }
            };

            ConnectionLink = $"ws://127.0.0.1:{_settingsManager.Current.LocalPort}";
        }

        public ObservableCollection<double> UploadSeriesList { get; }
        public ObservableCollection<double> DownloadSeriesList { get; }
        public ISeries[] Series { get; }
        public ObservableCollection<string> Logs { get; } = new();

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); } }
        public string StatusColor => IsRunning ? "Green" : "Red";

        private string _connectionLink = "";
        public string ConnectionLink { get => _connectionLink; set { _connectionLink = value; OnPropertyChanged(); } }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        private async Task StartAsync()
        {
            IsRunning = true;
            Logs.Clear();
            SettingsVM.Save();
            
            var targetDc = SettingsVM.SelectedDc;
            Log($"Starting proxy on ws://127.0.0.1:{SettingsVM.LocalPort} -> {targetDc.Name} ({targetDc.Ip}:{targetDc.Port})");

            _proxyEngine.LogMessage += Log;
            _proxyEngine.Start(SettingsVM.LocalPort, targetDc.Ip, targetDc.Port);
            
            _ = MonitorTrafficAsync();
            _uiTimer = new Timer(UpdateChartTimerCallback, null, 1000, 1000);

            ConnectionLink = $"ws://127.0.0.1:{SettingsVM.LocalPort}";

            if (SettingsVM.UseCloudflare)
            {
                _cloudflareService.LogMessage += Log;
                _cloudflareService.UrlObtained += url =>
                {
                    Application.Current?.Dispatcher.InvokeAsync(() => {
                        ConnectionLink = url.Replace("https://", "wss://");
                        Log($"Cloudflare Active: {ConnectionLink}");
                    });
                };
                await _cloudflareService.StartAsync(SettingsVM.LocalPort, CancellationToken.None);
            }
        }

        private async Task MonitorTrafficAsync()
        {
            try
            {
                await foreach (var evt in _proxyEngine.TrafficReader.ReadAllAsync())
                {
                    Interlocked.Add(ref _accBytesSent, evt.BytesSent);
                    Interlocked.Add(ref _accBytesReceived, evt.BytesReceived);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void UpdateChartTimerCallback(object? state)
        {
            if (!IsRunning) return;
            var sent = Interlocked.Exchange(ref _accBytesSent, 0);
            var rect = Interlocked.Exchange(ref _accBytesReceived, 0);

            var kbSent = Math.Round(sent / 1024.0, 2);
            var kbReceived = Math.Round(rect / 1024.0, 2);

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                UploadSeriesList.Add(kbSent);
                if (UploadSeriesList.Count > 60) UploadSeriesList.RemoveAt(0);

                DownloadSeriesList.Add(kbReceived);
                if (DownloadSeriesList.Count > 60) DownloadSeriesList.RemoveAt(0);
            });
        }

        public async Task StopAsync()
        {
            IsRunning = false;
            _uiTimer?.Dispose();
            
            Log("Stopping systems... 5s Max Timeout");
            await _proxyEngine.StopAsync(TimeSpan.FromSeconds(5));
            _proxyEngine.LogMessage -= Log;
            
            await _cloudflareService.StopAsync();
            _cloudflareService.LogMessage -= Log;
            Log("Complete halting applied on all services safely.");
        }

        private void Log(string msg)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
                if (Logs.Count > 200) Logs.RemoveAt(0);
            });
        }
    }
}
