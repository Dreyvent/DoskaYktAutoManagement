using DoskaYkt_AutoManagement.Core;
using DoskaYkt_AutoManagement.MVVM.Model;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

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
        public RelayCommand RemoveUnpublishTimerCommand { get; }
        public RelayCommand ApplyPublishTimerCommand { get; }
        public RelayCommand RemovePublishTimerCommand { get; }

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

        private readonly DoskaYktService _siteService;

        public MyADsViewModel()
        {
            _siteService = new DoskaYktService();
            AdScheduler.Instance.AdRepostRequested += OnAdRepostRequested;
            DeleteAdCommand = new RelayCommand(DeleteAd, () => HasSelection);
            BumpAdCommand = new RelayCommand(BumpAd, () => HasSelection);
            DeleteOnSiteCommand = new RelayCommand(DeleteOnSite, () => HasSelection);
            ActivateAdOnSiteCommand = new RelayCommand(ActivateOnSite, () => HasSelection);

            AdScheduler.Instance.AdUnpublishRequested += async ad =>
            {
                var acc = AccountManager.Instance.SelectedAccount;
                if (acc == null) return;

                TerminalLogger.Instance.Log($"[Scheduler] Снятие '{ad.Title}'...");
                var ok = await _siteService.UnpublishAdAsync(acc.Login, acc.Password, ad.SiteId, true, ad.Title);
                if (ok)
                {
                    ad.IsPublished = false;
                    TerminalLogger.Instance.Log($"[Scheduler] '{ad.Title}' снято.");
                    AdScheduler.Instance.StartForAd(ad); // ставим следующий (публикацию)
                }
            };

            AdScheduler.Instance.AdPublishRequested += async ad =>
            {
                var acc = AccountManager.Instance.SelectedAccount;
                if (acc == null) return;

                TerminalLogger.Instance.Log($"[Scheduler] Публикация '{ad.Title}'...");
                var ok = await _siteService.RepublishAdAsync(acc.Login, acc.Password, ad.SiteId, true);
                if (ok)
                {
                    ad.IsPublished = true;
                    TerminalLogger.Instance.Log($"[Scheduler] '{ad.Title}' опубликовано.");
                    AdScheduler.Instance.StartForAd(ad); // ставим следующий (снятие)
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

            ApplyUnpublishTimerCommand = new RelayCommand(() =>
            {
                var selected = GetSelectedAds();
                foreach (var ad in selected)
                {
                    ad.AutoRaiseMinutes = UnpublishMinutesInput > 0 ? UnpublishMinutesInput : ad.AutoRaiseMinutes;
                    ad.IsAutoRaiseEnabled = true;
                    _ = AdManager.Instance.UpdateAdAsync(ad);
                }
                TerminalLogger.Instance.Log($"[Timers] Применён таймер снятия: {UnpublishMinutesInput} мин.");
            }, () => HasSelection);

            RemoveUnpublishTimerCommand = new RelayCommand(() =>
            {
                var selected = GetSelectedAds();
                foreach (var ad in selected)
                {
                    ad.IsAutoRaiseEnabled = false;
                    _ = AdManager.Instance.UpdateAdAsync(ad);
                }
                TerminalLogger.Instance.Log("[Timers] Убран таймер снятия.");
            }, () => HasSelection);

            ApplyPublishTimerCommand = new RelayCommand(() =>
            {
                TerminalLogger.Instance.Log($"[Timers] Задан отложенный интервал публикации: {PublishMinutesInput} мин (будет учтён планировщиком).");
            }, () => HasSelection);

            RemovePublishTimerCommand = new RelayCommand(() =>
            {
                PublishMinutesInput = 0;
                TerminalLogger.Instance.Log("[Timers] Убран отложенный интервал публикации.");
            }, () => HasSelection);

            UnpublishMinutesInput = 30;
            PublishMinutesInput = 10;
        }

        private async void OnAdRepostRequested(Ad ad)
        {
            // тут твоя логика перепубликации
            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null) return;

            try
            {
                if (!string.IsNullOrWhiteSpace(ad.SiteId))
                {
                    var unpub = await _siteService.UnpublishAdAsync(acc.Login, acc.Password, ad.SiteId, true, ad.Title);
                    if (unpub)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(new Random().Next(3, 8)));
                        var repub = await _siteService.RepublishAdAsync(acc.Login, acc.Password, ad.SiteId, true);
                        if (repub)
                            TerminalLogger.Instance.Log($"[Scheduler] '{ad.Title}' перепубликовано таймером.");
                    }
                }
            }
            catch (Exception ex)
            {
                TerminalLogger.Instance.Log($"[Scheduler] Ошибка перепубликации '{ad.Title}': {ex.Message}");
            }
        }

        // ================= Вспомогательные методы =================

        /// <summary>Получить список выбранных объявлений (SelectedAd + чекбоксы)</summary>
        private System.Collections.Generic.List<Ad> GetSelectedAds()
        {
            var selected = MyAds.Where(a => a.IsSelected).ToList();
            if (SelectedAd != null && !selected.Contains(SelectedAd))
                selected.Add(SelectedAd);
            return selected;
        }

        /// <summary>Обновить доступность всех команд</summary>
        private void RefreshCommands()
        {
            DeleteAdCommand.RaiseCanExecuteChanged();
            BumpAdCommand.RaiseCanExecuteChanged();
            DeleteOnSiteCommand.RaiseCanExecuteChanged();
            ActivateAdOnSiteCommand.RaiseCanExecuteChanged();
            ApplyUnpublishTimerCommand.RaiseCanExecuteChanged();
            RemoveUnpublishTimerCommand.RaiseCanExecuteChanged();
            ApplyPublishTimerCommand.RaiseCanExecuteChanged();
            RemovePublishTimerCommand.RaiseCanExecuteChanged();
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
                        TerminalLogger.Instance.Log($"[SiteDelete] Снято с публикации: '{ad.Title}'.");
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
                        TerminalLogger.Instance.Log($"[SiteActivate] '{ad.Title}' опубликовано снова.");
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
