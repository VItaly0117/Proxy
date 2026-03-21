using System.Collections.ObjectModel;
using System.Linq;
using TelegramProxy.Infrastructure;
using TelegramProxy.Interfaces;
using TelegramProxy.Models;

namespace TelegramProxy.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsManager _settingsManager;

        public SettingsViewModel(ISettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            Datacenters = new ObservableCollection<TelegramDc>(_settingsManager.Current.Datacenters);
            SaveCommand = new RelayCommand(_ => Save());
        }

        public ObservableCollection<TelegramDc> Datacenters { get; }
        public RelayCommand SaveCommand { get; }

        public int LocalPort
        {
            get => _settingsManager.Current.LocalPort;
            set { _settingsManager.Current.LocalPort = value; OnPropertyChanged(); }
        }

        public bool UseCloudflare
        {
            get => _settingsManager.Current.UseCloudflare;
            set { _settingsManager.Current.UseCloudflare = value; OnPropertyChanged(); }
        }

        public TelegramDc SelectedDc
        {
            get => Datacenters.FirstOrDefault(d => d.Name == _settingsManager.Current.ActiveDcName) ?? Datacenters[1];
            set { if (value != null) _settingsManager.Current.ActiveDcName = value.Name; OnPropertyChanged(); }
        }

        public void Save() => _settingsManager.Save();
    }
}
