using DoskaYkt_AutoManagement.Core;
using System;
using System.Diagnostics;
using System.Reflection;
using System.ComponentModel;



namespace DoskaYkt_AutoManagement.MVVM.ViewModel
{
    public class MainViewModel : ObservableObject
    {

        public AsyncRelayCommand HomeViewCommand { get; set; } = null!;
        public AsyncRelayCommand MyADsViewCommand { get; set; } = null!;
        public AsyncRelayCommand MyAccountsViewCommand { get; set; } = null!;
        public AccountManager AccountManager => AccountManager.Instance;

        public HomeViewModel HomeVM { get; set; }

        public MyADsViewModel MyADsVM { get; set; }

        public MyAccountsViewModel MyAccountsVM { get; set; }

        public string AppVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                    .Split('+')[0] ?? "Unknown";
                return $"Version {version} Beta";
            }
        }

        private object? _currentView;

        public object? CurrentView
        {
            get { return _currentView; }
            set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        public MainViewModel()
        {
            HomeVM = new HomeViewModel();
            MyADsVM = new MyADsViewModel();
            MyAccountsVM = new MyAccountsViewModel();

            // Навигация из HomeVM после создания объявления
            HomeVM.NavigateToMyAds = () =>
            {
                CurrentView = MyADsVM;
            };

            CurrentView = HomeVM;

            HomeViewCommand = new AsyncRelayCommand(() =>
            {
                CurrentView = HomeVM;
                return Task.CompletedTask;
            });

            MyADsViewCommand = new AsyncRelayCommand(() =>
            {
                CurrentView = MyADsVM;
                return Task.CompletedTask;
            });

            MyAccountsViewCommand = new AsyncRelayCommand(() =>
            {
                CurrentView = MyAccountsVM;
                return Task.CompletedTask;
            });

            if (DoskaYkt_AutoManagement.Core.AdManager.Instance != null)
            {
                DoskaYkt_AutoManagement.Core.AdManager.Instance.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Core.AdManager.SelectedAd))
                    {
                        CurrentView = HomeVM;
                    }
                };
            }
        }
    }
}
