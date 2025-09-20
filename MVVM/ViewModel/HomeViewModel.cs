using DoskaYkt_AutoManagement.Core;
using DoskaYkt_AutoManagement.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.MVVM.ViewModel
{
    public class HomeViewModel : INotifyPropertyChanged
    {
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private ObservableCollection<AdData> _foundAds = new();
        public ObservableCollection<AdData> FoundAds
        {
            get => _foundAds;
            set { _foundAds = value; OnPropertyChanged(); }
        }

        // Для множественного выбора
        public ObservableCollection<AdData> SelectedFoundAds => new(FoundAds.Where(a => a.IsSelected));

        // Количество объявлений в БД
        public int AdsCount => AdManager.Instance.Ads.Count;

        // Переключатель видимости Chrome (сохраняется в Settings)
        private bool _isBrowserVisible = Properties.Settings.Default.BrowserVisible;
        public bool IsBrowserVisible
        {
            get => _isBrowserVisible;
            set
            {
                if (_isBrowserVisible != value)
                {
                    _isBrowserVisible = value;
                    Properties.Settings.Default.BrowserVisible = value;
                    Properties.Settings.Default.Save();
                    OnPropertyChanged();
                }
            }
        }

        // Команды
        public AsyncRelayCommand CheckAdsCommand { get; }
        public AsyncRelayCommand SaveSelectedCommand { get; }
        public RelayCommand SelectAllFoundCommand { get; }
        public RelayCommand ClearFoundSelectionCommand { get; }
        public RelayCommand EndSessionCommand { get; }

        private readonly DoskaYktService _service = DoskaYktService.Instance;

        private readonly AdScheduler _scheduler = AdScheduler.Instance;

        public HomeViewModel()
        {
            CheckAdsCommand = new AsyncRelayCommand(CheckAdsAsync);
            SaveSelectedCommand = new AsyncRelayCommand(SaveSelectedAdsAsync);
            SelectAllFoundCommand = new RelayCommand(() =>
            {
                foreach (var ad in FoundAds) ad.IsSelected = true;
                OnPropertyChanged(nameof(SelectedFoundAds));
            });
            ClearFoundSelectionCommand = new RelayCommand(() =>
            {
                foreach (var ad in FoundAds) ad.IsSelected = false;
                OnPropertyChanged(nameof(SelectedFoundAds));
            });
            EndSessionCommand = new RelayCommand(() =>
            {
                StatusMessage = "Сеанс браузера завершён.";
                _service.CloseSession();
            });

            // подписки
            _scheduler.AdPublished += ad =>
            {
                StatusMessage = $"Объявление '{ad.Title}' опубликовано";
                Core.TerminalLogger.Instance.Log($"[Scheduler] Опубликовано: {ad.Title}");
            };

            _scheduler.AdUnpublished += ad =>
            {
                StatusMessage = $"Объявление '{ad.Title}' снято с публикации";
                Core.TerminalLogger.Instance.Log($"[Scheduler] Снято: {ad.Title}");
            };
        }

        // Навигация: будет установлена из MainViewModel
        public Action NavigateToMyAds { get; set; }

        private async Task CheckAdsAsync()
        {
            StatusMessage = "Запуск проверки объявлений...";
            FoundAds.Clear();

            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null)
            {
                var msg = "[CheckAds] Нет выбранного аккаунта. Выберите аккаунт и повторите.";
                Core.TerminalLogger.Instance.Log(msg);
                StatusMessage = "Нет выбранного аккаунта.";
                return;
            }

            string login = acc.Login;
            string password = acc.Password;

            Core.TerminalLogger.Instance.Log($"[CheckAds] Начинаем проверку для аккаунта '{login}' (showChrome={IsBrowserVisible})");
            // Если сессия уже активна, не создаём новый браузер
            if (!_service.IsSessionActive)
            {
                Core.TerminalLogger.Instance.Log("[CheckAds] Сессии нет, будет создан новый браузер");
            }
            else
            {
                Core.TerminalLogger.Instance.Log("[CheckAds] Используем уже открытую сессию браузера");
            }
            StatusMessage = "Проверяем аккаунт...";

            // Вызов сервиса, который вернёт List<AdData>
            var (success, message, ads) = _service.CheckAds(login, password, IsBrowserVisible);

            StatusMessage = message ?? (success ? "Готово" : "Ошибка");
            Core.TerminalLogger.Instance.Log("[CheckAds] " + message);

            if (ads != null)
            {
                foreach (var ad in ads)
                {
                    ad.IsSelected = false;
                    FoundAds.Add(ad);
                    // НЕ запускаем таймер для найденных объявлений - только сканируем!
                    Core.TerminalLogger.Instance.Log(" - " + ad.Title);
                }
            }

            OnPropertyChanged(nameof(FoundAds));
            OnPropertyChanged(nameof(AdsCount));

            if (!success)
            {
                Core.TerminalLogger.Instance.Log("[CheckAds] Проверьте логин/пароль или режим запуска (headless).");
            }
        }

        private async Task SaveSelectedAdsAsync()
        {
            var selected = FoundAds.Where(a => a.IsSelected).ToList();
            if (!selected.Any())
            {
                StatusMessage = "Не выбрано ни одного объявления для сохранения.";
                return;
            }
            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null)
            {
                StatusMessage = "Нет выбранного аккаунта.";
                return;
            }

            int saved = 0;
            foreach (var found in selected)
            {
                var newAd = new Ad
                {
                    Title = found.Title,
                    AccountId = acc.Id,
                    AccountLogin = acc.Login,
                    SiteId = found.Id,         // если у тебя поле такое есть
                    IsPublished = found.IsPublished
                };

                await AdManager.Instance.AddAdAsync(newAd);
                Core.TerminalLogger.Instance.Log($"[SaveSelected] Сохранено в БД: '{newAd.Title}' (Id={newAd.Id})");
                saved++;
            }

            StatusMessage = saved > 0 ? $"Сохранено {saved} объявлений в БД." : "Не удалось сохранить объявления.";
            OnPropertyChanged(nameof(AdsCount));
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
