using System;
using System.Windows;
using TelegramProxy.Interfaces;
using TelegramProxy.Services;
using TelegramProxy.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows.Controls;

namespace TelegramProxy
{
    public partial class App : Application
    {
        private ISettingsManager? _settingsManager;
        private IProxyEngine? _proxyEngine;
        private MainViewModel? _mainViewModel;
        private TaskbarIcon? _notifyIcon;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _settingsManager = new SettingsManager();
            _proxyEngine = new ProxyEngine(_settingsManager);

            _mainViewModel = new MainViewModel(_proxyEngine, _settingsManager);

            var trayMenu = new ContextMenu();
            var showMenuItem = new MenuItem { Header = "Show Dashboard" };
            showMenuItem.Click += (s, ev) => MainWindow?.Show();
            
            var exitMenuItem = new MenuItem { Header = "Exit App" };
            exitMenuItem.Click += (s, ev) => Shutdown();

            trayMenu.Items.Add(showMenuItem);
            trayMenu.Items.Add(new Separator());
            trayMenu.Items.Add(exitMenuItem);

            _notifyIcon = new TaskbarIcon
            {
                ToolTipText = "Telegram SOCKS5 Proxy Active",
                ContextMenu = trayMenu
            };
            _notifyIcon.TrayMouseDoubleClick += (s, ev) => MainWindow?.Show();

            var mainWindow = new Views.MainWindow
            {
                DataContext = _mainViewModel
            };

            mainWindow.Closing += (s, ev) =>
            {
                ev.Cancel = true;
                mainWindow.Hide();
            };

            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _notifyIcon?.Dispose();

            if (_mainViewModel != null && _mainViewModel.IsRunning)
                await _mainViewModel.StopAsync();
            
            base.OnExit(e);
        }
    }
}
