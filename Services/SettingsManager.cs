using System;
using System.IO;
using System.Text.Json;
using TelegramProxy.Interfaces;
using TelegramProxy.Models;

namespace TelegramProxy.Services
{
    public class SettingsManager : ISettingsManager
    {
        private readonly string _configDir;
        private readonly string _configPath;

        public AppSettings Current { get; private set; } = new AppSettings();

        public SettingsManager()
        {
            _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TgProxy");
            _configPath = Path.Combine(_configDir, "config.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_configPath)) ?? new AppSettings();
            }
            catch { /* Return defaults */ }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_configDir);
                var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }
    }
}
