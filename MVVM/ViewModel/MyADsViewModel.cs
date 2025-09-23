using DoskaYkt_AutoManagement.Core;
using DoskaYkt_AutoManagement.MVVM.Model;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;

namespace DoskaYkt_AutoManagement.MVVM.ViewModel
{
    public class MyADsViewModel : ObservableObject
    {
        public ObservableCollection<Ad> MyAds => AdManager.Instance.Ads;

        private Ad _selectedAd;
        public Ad SelectedAd
        {
            get => _selectedAd;
            set
            {
                if (SetProperty(ref _selectedAd, value))
                    RefreshCommands();
            }
        }

        // ✅ Свойство для множественного выбора
        public bool HasSelection => SelectedAd != null || MyAds.Any(a => a.IsSelected);

        // Команды
        public RelayCommand DeleteAdCommand { get; }
        public RelayCommand BumpAdCommand { get; }
        public RelayCommand DeleteOnSiteCommand { get; }
        public RelayCommand ActivateAdOnSiteCommand { get; }

        public RelayCommand SelectAllCommand { get; }
        public RelayCommand ClearSelectionCommand { get; }

        public RelayCommand ApplyUnpublishTimerCommand { get; }
        public RelayCommand RemoveTimersCommand { get; }
        public RelayCommand ApplyPublishTimerCommand { get; }
        public RelayCommand RestartTimersCommand { get; }
        public AsyncRelayCommand SyncWithSiteCommand { get; }
        public AsyncRelayCommand StartAllCommand { get; }
        public RelayCommand StopAllCommand { get; }
        public AsyncRelayCommand CheckBrowserSessionCommand { get; }
        public AsyncRelayCommand StartBrowserSessionCommand { get; }
        public RelayCommand<Ad> SyncAdStatusCommand { get; }

        private string _sessionStatusMessage;
        public string SessionStatusMessage
        {
            get => _sessionStatusMessage;
            set => SetProperty(ref _sessionStatusMessage, value);
        }

        private int _unpublishMinutesInput;
        public int UnpublishMinutesInput
        {
            get => _unpublishMinutesInput;
            set => SetProperty(ref _unpublishMinutesInput, value);
        }

        private int _publishMinutesInput;
        public int PublishMinutesInput
        {
            get => _publishMinutesInput;
            set => SetProperty(ref _publishMinutesInput, value);
        }

        private readonly DoskaYktService _siteService = DoskaYktService.Instance;
        
        private bool _isOperationInProgress;
        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            set => SetProperty(ref _isOperationInProgress, value);
        }

        // UI timer to refresh time-left labels while staying on the view
        private readonly DispatcherTimer _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private int _nowTick;
        public int NowTick
        {
            get => _nowTick;
            private set => SetProperty(ref _nowTick, value);
        }

        public MyADsViewModel()
        {
            MyAdsView = CollectionViewSource.GetDefaultView(MyAds);
            try
            {
                SortBy = string.IsNullOrWhiteSpace(Properties.Settings.Default.MyAdsSortBy) ? "Title" : Properties.Settings.Default.MyAdsSortBy;
                SortAscending = Properties.Settings.Default.MyAdsSortAsc;
            }
            catch { SortBy = "Title"; SortAscending = true; }
            ApplySort();
            AdScheduler.Instance.AdRepostRequested += OnAdRepostRequested;
            DeleteAdCommand = new RelayCommand(DeleteAd, () => HasSelection);
            BumpAdCommand = new RelayCommand(BumpAd, () => HasSelection);
            DeleteOnSiteCommand = new RelayCommand(DeleteOnSite, () => HasSelection);
            ActivateAdOnSiteCommand = new RelayCommand(ActivateOnSite, () => HasSelection);

            AdScheduler.Instance.AdUnpublishRequested += async ad =>
            {
                try
                {
                    var acc = AccountManager.Instance.SelectedAccount;
                    if (acc == null) 
                    {
                        TerminalLogger.Instance.Log($"[Scheduler] Нет выбранного аккаунта для снятия '{ad.Title}'");
                        return;
                    }

                    await TaskQueue.Instance.EnqueueOnce($"unpub:{ad.SiteId}", async () =>
                    {
                        TerminalLogger.Instance.Log($"[Scheduler] Снятие '{ad.Title}'...");

                        var ok = await _siteService.UnpublishAdAsync(
                            acc.Login, acc.Password, ad.SiteId, Properties.Settings.Default.BrowserVisible, ad.Title);
                        if (ok)
                        {
                            ad.IsPublished = false;
                            ad.IsPublishedOnSite = false;
                            TerminalLogger.Instance.Log($"[Scheduler] '{ad.Title}' снято.");
                            await AdManager.Instance.UpdateAdAsync(ad);
                        }
                        else
                        {
                            TerminalLogger.Instance.Log($"[Scheduler] Не удалось снять '{ad.Title}' с сайта");
                        }

                        await Task.Delay(2000);
                        AdScheduler.Instance.StartForAd(ad);
                    }, $"Unpublish: {ad.Title} ({ad.SiteId})");
                }
                catch (Exception ex)
                {
                    TerminalLogger.Instance.Log($"[Scheduler] Ошибка при снятии '{ad.Title}': {ex.Message}");
                    // При исключении перезапускаем таймер для повторной попытки
                    AdScheduler.Instance.StartForAd(ad);
                }
            };

            AdScheduler.Instance.AdPublishRequested += async ad =>
            {
                try
                {
                    var acc = AccountManager.Instance.SelectedAccount;
                    if (acc == null) 
                    {
                        TerminalLogger.Instance.Log($"[Scheduler] Нет выбранного аккаунта для публикации '{ad.Title}'");
                        return;
                    }

                    await TaskQueue.Instance.EnqueueOnce($"pub:{ad.SiteId}", async () =>
                    {
                        TerminalLogger.Instance.Log($"[Scheduler] Публикация '{ad.Title}'...");

                        var ok = await _siteService.RepublishAdAsync(
                            acc.Login, acc.Password, ad.SiteId, Properties.Settings.Default.BrowserVisible);
                        if (ok)
                        {
                            ad.IsPublished = true;
                            ad.IsPublishedOnSite = true;
                            TerminalLogger.Instance.Log($"[Scheduler] '{ad.Title}' опубликовано.");
                            await AdManager.Instance.UpdateAdAsync(ad);
                        }
                        else
                        {
                            TerminalLogger.Instance.Log($"[Scheduler] Не удалось опубликовать '{ad.Title}' на сайте");
                        }

                        await Task.Delay(2000);
                        AdScheduler.Instance.StartForAd(ad);
                    }, $"Publish: {ad.Title} ({ad.SiteId})");
                }
                catch (Exception ex)
                {
                    TerminalLogger.Instance.Log($"[Scheduler] Ошибка при публикации '{ad.Title}': {ex.Message}");
                    // При исключении перезапускаем таймер для повторной попытки
                    AdScheduler.Instance.StartForAd(ad);
                }
            };

            SelectAllCommand = new RelayCommand(() =>
            {
                foreach (var ad in MyAds) ad.IsSelected = true;
                RefreshCommands();
            });
            ClearSelectionCommand = new RelayCommand(() =>
            {
                foreach (var ad in MyAds) ad.IsSelected = false;
                RefreshCommands();
            });

            _siteService = DoskaYktService.Instance;

            // Инициализация команд
            ApplyUnpublishTimerCommand = new RelayCommand(ApplyUnpublishTimer, () => HasSelection);
            ApplyPublishTimerCommand = new RelayCommand(ApplyPublishTimer, () => HasSelection);
            RemoveTimersCommand = new RelayCommand(RemoveTimers, () => HasSelection);
            RestartTimersCommand = new RelayCommand(RestartTimers);
            SyncWithSiteCommand = new AsyncRelayCommand(SyncWithSiteAsync);
            StartAllCommand = new AsyncRelayCommand(StartAllAsync);
            StopAllCommand = new RelayCommand(StopAll);

            CheckBrowserSessionCommand = new AsyncRelayCommand(CheckBrowserSessionAsync);
            StartBrowserSessionCommand = new AsyncRelayCommand(StartBrowserSessionAsync);
            SyncAdStatusCommand = new RelayCommand<Ad>(ad => _ = SyncAdStatusAsync(ad));

            UnpublishMinutesInput = 30; // дефолт
            PublishMinutesInput = 10;   // дефолт

            // Periodic UI tick for time-dependent labels
            try
            {
                _uiTimer.Tick += (s, e) => { NowTick++; };
                _uiTimer.Start();
            }
            catch { }
        }

        // ===== Sorting =====
        public ICollectionView MyAdsView { get; }
        private string _sortBy;
        public string SortBy
        {
            get => _sortBy; set { _sortBy = value; OnPropertyChanged(nameof(SortBy)); ApplySort(); SaveSortPreferences(); }
        }
        private bool _sortAscending;
        public bool SortAscending
        {
            get => _sortAscending; set { _sortAscending = value; OnPropertyChanged(nameof(SortAscending)); ApplySort(); SaveSortPreferences(); }
        }

        public void ApplySort()
        {
            using (MyAdsView.DeferRefresh())
            {
                MyAdsView.SortDescriptions.Clear();
                var dir = SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                if (string.Equals(SortBy, "Title", StringComparison.OrdinalIgnoreCase))
                    MyAdsView.SortDescriptions.Add(new SortDescription(nameof(Ad.Title), dir));
                else if (string.Equals(SortBy, "SiteId", StringComparison.OrdinalIgnoreCase))
                    MyAdsView.SortDescriptions.Add(new SortDescription(nameof(Ad.SiteId), dir));
                else if (string.Equals(SortBy, "Id", StringComparison.OrdinalIgnoreCase))
                    MyAdsView.SortDescriptions.Add(new SortDescription(nameof(Ad.Id), dir));
                else
                    MyAdsView.SortDescriptions.Add(new SortDescription(nameof(Ad.Title), dir));
            }
        }

        private void SaveSortPreferences()
        {
            try
            {
                Properties.Settings.Default.MyAdsSortBy = SortBy;
                Properties.Settings.Default.MyAdsSortAsc = SortAscending;
                Properties.Settings.Default.Save();
            }
            catch { }
        }

        private void ApplyUnpublishTimer()
        {
            if (UnpublishMinutesInput <= 0 || UnpublishMinutesInput > 1440) // Максимум 24 часа
            {
                TerminalLogger.Instance.Log("[Timers] Неверный интервал снятия (должен быть от 1 до 1440 минут). Таймер не будет установлен.");
                return;
            }

            var selected = GetSelectedAds();
            if (selected.Count == 0) return;

            // Распределяем старты: шаг в минутах + небольшой случайный джиттер
            var stepSec = Math.Max(0, Properties.Settings.Default.SchedulerStepSeconds);
            var useJitter = Properties.Settings.Default.SchedulerUseJitter;
            var jitterMin = Math.Max(0, Properties.Settings.Default.SchedulerJitterMinSec);
            var jitterMax = Math.Max(jitterMin, Properties.Settings.Default.SchedulerJitterMaxSec);
            var rnd = new Random();

            for (int i = 0; i < selected.Count; i++)
            {
                var ad = selected[i];
                ad.UnpublishMinutes = UnpublishMinutesInput;
                ad.RepublishMinutes = PublishMinutesInput;
                ad.IsAutoRaiseEnabled = true;

                // Настраиваем даты таймеров в зависимости от текущего состояния
                if (ad.IsPublished)
                {
                    var baseOffset = TimeSpan.FromSeconds(i * stepSec);
                    var jitter = useJitter ? TimeSpan.FromSeconds(rnd.Next(jitterMin, jitterMax + 1)) : TimeSpan.Zero;
                    ad.NextUnpublishAt = DateTime.Now.AddMinutes(UnpublishMinutesInput).Add(baseOffset).Add(jitter);
                    ad.NextRepublishAt = null;
                    TerminalLogger.Instance.Log($"[Timers] Для '{ad.Title}' установлен таймер снятия через {UnpublishMinutesInput} мин.");
                }
                else
                {
                    var baseOffset = TimeSpan.FromSeconds(i * stepSec);
                    var jitter = useJitter ? TimeSpan.FromSeconds(rnd.Next(jitterMin, jitterMax + 1)) : TimeSpan.Zero;
                    ad.NextRepublishAt = DateTime.Now.AddMinutes(PublishMinutesInput).Add(baseOffset).Add(jitter);
                    ad.NextUnpublishAt = null;
                    TerminalLogger.Instance.Log($"[Timers] Для '{ad.Title}' установлен таймер публикации через {PublishMinutesInput} мин.");
                }

                AdScheduler.Instance.StartForAd(ad);
                _ = AdManager.Instance.UpdateAdAsync(ad);
            }

            TerminalLogger.Instance.Log($"[Timers] Применены таймеры для {selected.Count} объявлений.");
        }

        private void ApplyPublishTimer()
        {
            if (PublishMinutesInput <= 0 || PublishMinutesInput > 1440) // Максимум 24 часа
            {
                TerminalLogger.Instance.Log("[Timers] Неверный интервал публикации (должен быть от 1 до 1440 минут). Таймер не будет установлен.");
                return;
            }

            var selected = GetSelectedAds();
            if (selected.Count == 0) return;

            var stepSec = Math.Max(0, Properties.Settings.Default.SchedulerStepSeconds);
            var useJitter = Properties.Settings.Default.SchedulerUseJitter;
            var jitterMin = Math.Max(0, Properties.Settings.Default.SchedulerJitterMinSec);
            var jitterMax = Math.Max(jitterMin, Properties.Settings.Default.SchedulerJitterMaxSec);
            var rnd = new Random();

            for (int i = 0; i < selected.Count; i++)
            {
                var ad = selected[i];
                ad.RepublishMinutes = PublishMinutesInput;
                ad.UnpublishMinutes = UnpublishMinutesInput;
                ad.IsAutoRaiseEnabled = true;

                // Настраиваем даты таймеров
                if (ad.IsPublished)
                {
                    var baseOffset = TimeSpan.FromSeconds(i * stepSec);
                    var jitter = useJitter ? TimeSpan.FromSeconds(rnd.Next(jitterMin, jitterMax + 1)) : TimeSpan.Zero;
                    ad.NextUnpublishAt = DateTime.Now.AddMinutes(UnpublishMinutesInput).Add(baseOffset).Add(jitter);
                    ad.NextRepublishAt = null;
                }
                else
                {
                    var baseOffset = TimeSpan.FromSeconds(i * stepSec);
                    var jitter = useJitter ? TimeSpan.FromSeconds(rnd.Next(jitterMin, jitterMax + 1)) : TimeSpan.Zero;
                    ad.NextRepublishAt = DateTime.Now.AddMinutes(PublishMinutesInput).Add(baseOffset).Add(jitter);
                    ad.NextUnpublishAt = null;
                }

                AdScheduler.Instance.StartForAd(ad);
                _ = AdManager.Instance.UpdateAdAsync(ad);
            }

            TerminalLogger.Instance.Log($"[Timers] Применён таймер публикации: {PublishMinutesInput} мин.");
        }

        private void RemoveTimers()
        {
            var selected = GetSelectedAds();
            foreach (var ad in selected)
            {
                ad.IsAutoRaiseEnabled = false;
                ad.NextUnpublishAt = null;
                ad.NextRepublishAt = null;
                AdScheduler.Instance.StopForAd(ad.Id);
                _ = AdManager.Instance.UpdateAdAsync(ad);
            }

            TerminalLogger.Instance.Log("[Timers] Все таймеры остановлены.");
        }

        private void RestartTimers()
        {
            AdScheduler.Instance.RestartAllTimers();
        }

        private void StopAll()
        {
            try
            {
                var res = System.Windows.MessageBox.Show(
                    "Остановить все таймеры?", "Подтверждение",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (res != System.Windows.MessageBoxResult.Yes) return;
            }
            catch { }
            AdScheduler.Instance.StopAll();
            TerminalLogger.Instance.Log("[Scheduler] Все таймеры остановлены пользователем.");
        }

        private async Task SyncWithSiteAsync()
        {
            TerminalLogger.Instance.Log("[Sync] Начинаем синхронизацию с сайтом...");
            await AdManager.Instance.SyncWithSiteAsync();
            TerminalLogger.Instance.Log("[Sync] Синхронизация завершена");
        }

        private async Task StartAllAsync()
        {
            TerminalLogger.Instance.Log("[StartAll] Запуск всех активных таймеров...");
            
            // Сначала синхронизируемся с сайтом
            await SyncWithSiteAsync();
            
            // Затем запускаем все таймеры
            AdScheduler.Instance.RestartAllTimers();
            
            TerminalLogger.Instance.Log("[StartAll] Все таймеры запущены");
        }

        private Task CheckBrowserSessionAsync()
        {
            return Task.Run(() =>
            {
                var service = DoskaYktService.Instance;
                var active = service.IsSessionActive;
                var logged = service.IsLoggedIn;
                SessionStatusMessage = active
                    ? (logged ? "Сессия активна и авторизована." : "Сессия активна, но не авторизована.")
                    : "Сессия не активна.";
                TerminalLogger.Instance.Log($"[Session] {SessionStatusMessage}");
            });
        }

        private async Task StartBrowserSessionAsync()
        {
            try
            {
                var acc = AccountManager.Instance.SelectedAccount;
                if (acc == null)
                {
                    SessionStatusMessage = "Нет выбранного аккаунта.";
                    TerminalLogger.Instance.Log("[Session] Нет выбранного аккаунта.");
                    return;
                }

                await TaskQueue.Instance.Enqueue(async () =>
                {
                    TerminalLogger.Instance.Log("[Session] Запуск сеанса браузера и вход...");
                    var ok = await _siteService.LoginAsync(
                        acc.Login, acc.Password, Properties.Settings.Default.BrowserVisible);
                    SessionStatusMessage = ok ? "Сеанс запущен и выполнен вход." : "Не удалось запустить сеанс/войти.";
                }, "Start Browser Session");
            }
            catch (Exception ex)
            {
                TerminalLogger.Instance.LogError("[Session] Ошибка запуска сеанса", ex);
                SessionStatusMessage = "Ошибка запуска сеанса.";
            }
        }

        /// <summary>
        /// Принудительно синхронизирует статус конкретного объявления
        /// </summary>
        public async Task SyncAdStatusAsync(Ad ad)
        {
            if (ad == null || string.IsNullOrEmpty(ad.SiteId)) return;

            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null) return;

            try
            {
                TerminalLogger.Instance.Log($"[SyncAd] Синхронизация статуса '{ad.Title}'...");
                
                var existsOnSite = await _siteService.ExistsAdOnSiteAsync(acc.Login, acc.Password, ad.SiteId, false, ad.Title);
                bool wasPublished = ad.IsPublished;
                ad.IsPublished = existsOnSite;
                ad.IsPublishedOnSite = existsOnSite;
                
                if (wasPublished != ad.IsPublished)
                {
                    TerminalLogger.Instance.Log($"[SyncAd] Статус '{ad.Title}' изменен: {wasPublished} -> {ad.IsPublished}");
                    await AdManager.Instance.UpdateAdAsync(ad);
                }
                else
                {
                    TerminalLogger.Instance.Log($"[SyncAd] Статус '{ad.Title}' не изменился: {ad.IsPublished}");
                }
            }
            catch (Exception ex)
            {
                TerminalLogger.Instance.Log($"[SyncAd] Ошибка синхронизации '{ad.Title}': {ex.Message}");
            }
        }


        private async void OnAdRepostRequested(Ad ad)
        {
            // тут твоя логика перепубликации
            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null) return;

            var ok = await _siteService.RepostAdWithDelay(acc.Login, acc.Password, ad.SiteId, ad.Title);
            if (ok)
                TerminalLogger.Instance.Log($"[Scheduler] '{ad.Title}' перепубликовано таймером.");
        }

        // ================= Вспомогательные методы =================

        /// <summary>Получить список выбранных объявлений (SelectedAd + чекбоксы)</summary>
        private System.Collections.Generic.List<Ad> GetSelectedAds()
        {
            // Order by current view order for deterministic queueing
            var selectedSet = MyAds.Where(a => a.IsSelected).ToHashSet();
            if (SelectedAd != null) selectedSet.Add(SelectedAd);
            var ordered = new System.Collections.Generic.List<Ad>();
            foreach (var item in MyAdsView)
            {
                if (item is Ad ad && selectedSet.Contains(ad)) ordered.Add(ad);
            }
            return ordered;
        }

        /// <summary>Обновить доступность всех команд</summary>
        private void RefreshCommands()
        {
            DeleteAdCommand.RaiseCanExecuteChanged();
            BumpAdCommand.RaiseCanExecuteChanged();
            DeleteOnSiteCommand.RaiseCanExecuteChanged();
            ActivateAdOnSiteCommand.RaiseCanExecuteChanged();
            ApplyUnpublishTimerCommand.RaiseCanExecuteChanged();
            RemoveTimersCommand.RaiseCanExecuteChanged();
            ApplyPublishTimerCommand.RaiseCanExecuteChanged();
        }

        // ================= Реализация команд =================

        private async void DeleteAd()
        {
            var selected = GetSelectedAds();
            if (!selected.Any()) return;

            foreach (var ad in selected.ToList())
            {
                TerminalLogger.Instance.Log($"[DB] Удаление объявления из БД: '{ad.Title}' (Id={ad.Id})");
                await AdManager.Instance.RemoveAdAsync(ad);
            }
        }

        private async void BumpAd()
        {
            var selected = GetSelectedAds();
            if (!selected.Any()) return;

            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null)
            {
                TerminalLogger.Instance.Log("[Bump] Нет выбранного аккаунта.");
                return;
            }

            IsOperationInProgress = true;
            try
            {

            foreach (var ad in selected)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(ad.SiteId))
                    {
                        TerminalLogger.Instance.Log($"[Bump] У объявления '{ad.Title}' нет siteId, пропуск.");
                        continue;
                    }

                    var unpub = await _siteService.UnpublishAdAsync(acc.Login, acc.Password, ad.SiteId, true, ad.Title);
                    if (!unpub)
                    {
                        TerminalLogger.Instance.Log($"[Bump] Не удалось снять '{ad.Title}' с публикации.");
                        continue;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(new Random().Next(3, 8)));

                    var repub = await _siteService.RepublishAdAsync(acc.Login, acc.Password, ad.SiteId, true);
                    if (repub)
                        TerminalLogger.Instance.Log($"[Bump] '{ad.Title}' опубликовано снова.");
                    else
                        TerminalLogger.Instance.Log($"[Bump] Не удалось снова опубликовать '{ad.Title}'.");
                }
                catch (Exception ex)
                {
                    TerminalLogger.Instance.Log($"[Bump] Ошибка: {ex.Message}");
                }
            }
            }
            finally
            {
                IsOperationInProgress = false;
            }
        }

        private async void DeleteOnSite()
        {
            var selected = GetSelectedAds();
            if (!selected.Any()) return;

            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null)
            {
                TerminalLogger.Instance.Log("[SiteDelete] Нет выбранного аккаунта.");
                return;
            }

            foreach (var ad in selected)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(ad.SiteId))
                    {
                        TerminalLogger.Instance.Log($"[SiteDelete] У объявления '{ad.Title}' нет SiteId. Пропуск.");
                        continue;
                    }

                    TerminalLogger.Instance.Log($"[SiteDelete] Снятие с публикации '{ad.Title}'...");
                    var unpublished = await _siteService.UnpublishAdAsync(acc.Login, acc.Password, ad.SiteId, true, ad.Title);

                    if (unpublished)
                    {
                        ad.IsPublished = false;
                        ad.IsPublishedOnSite = false;
                        await AdManager.Instance.UpdateAdAsync(ad);
                        TerminalLogger.Instance.Log($"[SiteDelete] Снято с публикации: '{ad.Title}'.");
                    }
                    else
                        TerminalLogger.Instance.Log($"[SiteDelete] Не удалось снять с публикации: '{ad.Title}'.");
                }
                catch (Exception ex)
                {
                    TerminalLogger.Instance.Log($"[SiteDelete] Ошибка: {ex.Message}");
                }
            }
        }

        private async void ActivateOnSite()
        {
            var selected = GetSelectedAds();
            if (!selected.Any()) return;

            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null)
            {
                TerminalLogger.Instance.Log("[SiteActivate] Нет выбранного аккаунта.");
                return;
            }

            foreach (var ad in selected)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(ad.SiteId))
                    {
                        TerminalLogger.Instance.Log($"[SiteActivate] У объявления '{ad.Title}' нет SiteId. Пропуск.");
                        continue;
                    }

                    TerminalLogger.Instance.Log($"[SiteActivate] Активация '{ad.Title}'...");
                    var ok = await _siteService.RepublishAdAsync(acc.Login, acc.Password, ad.SiteId, true);
                    if (ok)
                    {
                        ad.IsPublished = true;
                        ad.IsPublishedOnSite = true;
                        await AdManager.Instance.UpdateAdAsync(ad);
                        TerminalLogger.Instance.Log($"[SiteActivate] '{ad.Title}' опубликовано снова.");
                    }
                    else
                        TerminalLogger.Instance.Log($"[SiteActivate] Не удалось опубликовать '{ad.Title}'.");
                }
                catch (Exception ex)
                {
                    TerminalLogger.Instance.Log($"[SiteActivate] Ошибка: {ex.Message}");
                }
            }
        }
    }
}
