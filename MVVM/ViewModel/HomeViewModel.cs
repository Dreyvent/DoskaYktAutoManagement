using DoskaYkt_AutoManagement.Core;
using DoskaYkt_AutoManagement.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;

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

        public ICollectionView FoundAdsView { get; }
        private string _foundSortBy = "Title";
        public string FoundSortBy
        {
            get => _foundSortBy; set { _foundSortBy = value; OnPropertyChanged(); ApplyFoundSort(); }
        }
        private bool _foundSortAscending = true;
        public bool FoundSortAscending
        {
            get => _foundSortAscending; set { _foundSortAscending = value; OnPropertyChanged(); ApplyFoundSort(); }
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

        // Removed legacy HideDriverWindow (Selenium)

        // Удалены устаревшие настройки шедулера (шаг/джиттер)

        public int RepostDelayMinSec
        {
            get => Properties.Settings.Default.RepostDelayMinSec;
            set
            {
                var min = Math.Max(0, value);
                if (Properties.Settings.Default.RepostDelayMinSec != min)
                {
                    Properties.Settings.Default.RepostDelayMinSec = min;
                    if (Properties.Settings.Default.RepostDelayMaxSec < min)
                        Properties.Settings.Default.RepostDelayMaxSec = min;
                    Properties.Settings.Default.Save();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RepostDelayMaxSec));
                }
            }
        }

        public int RepostDelayMaxSec
        {
            get => Properties.Settings.Default.RepostDelayMaxSec;
            set
            {
                var max = Math.Max(RepostDelayMinSec, value);
                if (Properties.Settings.Default.RepostDelayMaxSec != max)
                {
                    Properties.Settings.Default.RepostDelayMaxSec = max;
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
        public AsyncRelayCommand EndSessionCommand { get; }

        private readonly ISiteAutomation _service = SiteAutomationProvider.Current;

        private readonly AdScheduler _scheduler = AdScheduler.Instance;

        public HomeViewModel()
        {
            FoundAdsView = CollectionViewSource.GetDefaultView(FoundAds);
            ApplyFoundSort();
            CheckAdsCommand = new AsyncRelayCommand(CheckAdsAsync);
            SaveSelectedCommand = new AsyncRelayCommand(SaveSelectedAdsAsync);
            SelectAllFoundCommand = new RelayCommand(() =>
            {
                foreach (var item in FoundAdsView)
                {
                    if (item is AdData ad) ad.IsSelected = true;
                }
                OnPropertyChanged(nameof(SelectedFoundAds));
            });
            ClearFoundSelectionCommand = new RelayCommand(() =>
            {
                foreach (var item in FoundAdsView)
                {
                    if (item is AdData ad) ad.IsSelected = false;
                }
                OnPropertyChanged(nameof(SelectedFoundAds));
            });
            EndSessionCommand = new AsyncRelayCommand(async () =>
            {
                StatusMessage = "Завершаем сеанс браузера...";
                if (_service is DoskaYktService)
                {
                    try { await ((DoskaYktService)_service).CloseSessionAsync(); }
                    catch { }
                }
                else
                {
                    // Fallback to fire-and-forget if other implementation
                    _service.CloseSession();
                }
                StatusMessage = "Сеанс браузера завершён.";
                Core.TerminalLogger.Instance.Log("[Session] Сеанс браузера закрыт пользователем.");
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

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(); }
        }

        private async Task CheckAdsAsync()
        {
            if (IsScanning) return;
            IsScanning = true;
            StatusMessage = "Запуск проверки объявлений...";
            FoundAds.Clear();
            FoundAdsView.Refresh();

            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null)
            {
                var msg = "[CheckAds] Нет выбранного аккаунта. Выберите аккаунт и повторите.";
                Core.TerminalLogger.Instance.Log(msg);
                StatusMessage = "Нет выбранного аккаунта.";
                IsScanning = false;
                return;
            }

            string login = acc.Login;
            string password = acc.Password;

            Core.TerminalLogger.Instance.Log($"[CheckAds] Начинаем проверку для аккаунта '{login}' (showChrome={IsBrowserVisible})");
            if (!_service.IsSessionActive)
            {
                Core.TerminalLogger.Instance.Log("[CheckAds] Сессии нет, будет создан новый браузер");
            }
            else
            {
                Core.TerminalLogger.Instance.Log("[CheckAds] Используем уже открытую сессию браузера");
            }
            StatusMessage = "Проверяем аккаунт...";

            try
            {
                // Выполняем последовательно, чтобы не было конфликтов навигации в одном браузере
                StatusMessage = "Сканируем опубликованные объявления...";
                var (success, message, publishedAds) = await Task.Run(() => _service.CheckAds(login, password, IsBrowserVisible));
                Core.TerminalLogger.Instance.Log("[CheckAds] Опубликованные: " + message);

                StatusMessage = "Сканируем неопубликованные объявления...";
                var (unpubSuccess, unpubMessage, unpublishedAds) = await Task.Run(() => _service.CheckUnpublishedAds(login, password, IsBrowserVisible));
                Core.TerminalLogger.Instance.Log("[CheckAds] Неопубликованные: " + unpubMessage);

                var allAds = new List<AdData>();
                if (publishedAds != null) allAds.AddRange(publishedAds);
                if (unpublishedAds != null) allAds.AddRange(unpublishedAds);

                StatusMessage = $"Найдено {allAds.Count} объявлений ({publishedAds?.Count ?? 0} опубликованных, {unpublishedAds?.Count ?? 0} неопубликованных)";
                Core.TerminalLogger.Instance.Log($"[CheckAds] Всего найдено: {allAds.Count} объявлений");

                if (allAds.Any())
                {
                    foreach (var ad in allAds)
                    {
                        ad.IsSelected = false;
                        FoundAds.Add(ad);
                        var status = ad.IsPublished ? "опубликовано" : "неопубликовано";
                        Core.TerminalLogger.Instance.Log($" - {ad.Title} ({status})");
                    }
                }

                OnPropertyChanged(nameof(FoundAds));
                OnPropertyChanged(nameof(AdsCount));

                if (!success && !unpubSuccess)
                {
                    Core.TerminalLogger.Instance.Log("[CheckAds] Ошибка при сканировании объявлений. Проверьте логин/пароль или режим запуска (headless).");
                    StatusMessage = "Ошибка при сканировании объявлений";
                }
                else if (!success)
                {
                    Core.TerminalLogger.Instance.Log("[CheckAds] Ошибка при сканировании опубликованных объявлений, но неопубликованные найдены");
                }
                else if (!unpubSuccess)
                {
                    Core.TerminalLogger.Instance.Log("[CheckAds] Ошибка при сканировании неопубликованных объявлений, но опубликованные найдены");
                }
            }
            catch (Exception ex)
            {
                Core.TerminalLogger.Instance.Log($"[CheckAds] Исключение: {ex}");
                StatusMessage = "Ошибка при сканировании (см. лог)";
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void ApplyFoundSort()
        {
            using (FoundAdsView.DeferRefresh())
            {
                FoundAdsView.SortDescriptions.Clear();
                var dir = FoundSortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                if (string.Equals(FoundSortBy, "Title", StringComparison.OrdinalIgnoreCase))
                    FoundAdsView.SortDescriptions.Add(new SortDescription(nameof(AdData.Title), dir));
                else if (string.Equals(FoundSortBy, "Id", StringComparison.OrdinalIgnoreCase))
                    FoundAdsView.SortDescriptions.Add(new SortDescription(nameof(AdData.Id), dir));
                else
                    FoundAdsView.SortDescriptions.Add(new SortDescription(nameof(AdData.Title), dir));
            }
            ApplyFoundFilter();
        }

        private string _foundFilterMode = "Все"; // "Все" | "Опубликованные" | "Снятые"
        public string FoundFilterMode
        {
            get => _foundFilterMode;
            set
            {
                if (_foundFilterMode != value)
                {
                    _foundFilterMode = value;
                    OnPropertyChanged(nameof(FoundFilterMode));
                    ApplyFoundFilter();
                }
            }
        }

        private void ApplyFoundFilter()
        {
            if (FoundAdsView == null) return;
            FoundAdsView.Filter = obj =>
            {
                if (obj is not AdData ad) return false;
                var mode = (FoundFilterMode ?? "Все").Trim();
                if (string.Equals(mode, "Все", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "All", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(mode, "Опубликованные", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Published", StringComparison.OrdinalIgnoreCase))
                    return ad.IsPublished;
                if (string.Equals(mode, "Снятые", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Unpublished", StringComparison.OrdinalIgnoreCase))
                    return !ad.IsPublished;
                return true;
            };
            try { FoundAdsView.Refresh(); } catch { }
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
                    SiteId = found.Id ?? found.SiteId ?? "",
                    IsPublished = found.IsPublished,
                    IsPublishedOnSite = found.IsPublished,
                    IsAutoRaiseEnabled = false, // По умолчанию таймеры отключены
                    UnpublishMinutes = 30,      // Дефолтные значения
                    RepublishMinutes = 10
                };
                
                var status = found.IsPublished ? "опубликовано" : "неопубликовано";
                Core.TerminalLogger.Instance.Log($"[SaveSelected] Сохраняем объявление '{found.Title}' со статусом: {status}");

                await AdManager.Instance.AddAdAsync(newAd);
                Core.TerminalLogger.Instance.Log($"[SaveSelected] Сохранено в БД: '{newAd.Title}' (Id={newAd.Id}, SiteId={newAd.SiteId}, Published={newAd.IsPublished})");
                saved++;
            }

            StatusMessage = saved > 0 ? $"Сохранено {saved} объявлений в БД." : "Не удалось сохранить объявления.";
            OnPropertyChanged(nameof(AdsCount));
            
            NavigateToMyAds?.Invoke();
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
