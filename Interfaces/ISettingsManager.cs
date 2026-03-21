using TelegramProxy.Models;

namespace TelegramProxy.Interfaces
{
    public interface ISettingsManager
    {
        AppSettings Current { get; }
        void Load();
        void Save();
    }
}
