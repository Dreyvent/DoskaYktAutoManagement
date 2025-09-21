using DoskaYkt_AutoManagement.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core
{
    public class AdManager : ObservableObject
    {
        private static AdManager? _instance;
        public static AdManager Instance => _instance ?? (_instance = new AdManager());

        public ObservableCollection<Ad> Ads { get; } = new ObservableCollection<Ad>();
        public ObservableCollection<AdData> MyAds { get; set; } = new();

        public void RefreshAds(List<AdData> ads)
        {
            MyAds.Clear();
            foreach (var ad in ads)
                MyAds.Add(ad);
        }

        private AdManager()
        {
            // сразу асинхронная загрузка (не блокирует UI)
            _ = LoadFromDatabaseAsync();
        }

        private Ad _selectedAd;
        public Ad SelectedAd
        {
            get => _selectedAd;
            set
            {
                _selectedAd = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Загружает все объявления из базы в память
        /// </summary>
        private readonly AdScheduler _scheduler = AdScheduler.Instance;
        public async Task LoadFromDatabaseAsync()
        {
            Ads.Clear();
            var rows = await DatabaseHelper.GetAnnouncementsAsync().ConfigureAwait(false);
            foreach (var (id, title, cycle, isAuto, accountId, accountLogin, siteId, isPublishedOnSite, nextUnpublishAt, nextRepublishAt) in rows)
            {
                var ad = new Ad
                {
                    Id = id,
                    Title = title,
                    AutoRaiseMinutes = cycle,
                    UnpublishMinutes = cycle, // Используем cycle как UnpublishMinutes для совместимости
                    RepublishMinutes = Math.Max(1, cycle / 3), // RepublishMinutes = 1/3 от UnpublishMinutes
                    IsAutoRaiseEnabled = isAuto == 1,
                    AccountId = accountId,
                    AccountLogin = accountLogin,
                    SiteId = siteId,
                    IsPublished = isPublishedOnSite, // Синхронизируем с состоянием на сайте
                    IsPublishedOnSite = isPublishedOnSite,
                    NextUnpublishAt = string.IsNullOrEmpty(nextUnpublishAt) ? null : DateTime.TryParse(nextUnpublishAt, out var unpublishDate) ? unpublishDate : null,
                    NextRepublishAt = string.IsNullOrEmpty(nextRepublishAt) ? null : DateTime.TryParse(nextRepublishAt, out var republishDate) ? republishDate : null
                };
                Ads.Add(ad);
                
                // НЕ запускаем таймеры автоматически при загрузке из БД
                // Пользователь должен нажать "Запустить всё" для активации
                TerminalLogger.Instance.Log($"[AdManager] Загружено объявление '{ad.Title}' (Published={ad.IsPublished}, AutoEnabled={ad.IsAutoRaiseEnabled})");
            }
            TerminalLogger.Instance.Log($"[AdManager] Загружено {Ads.Count} объявлений из БД");
        }

        /// <summary>
        /// Добавить объявление (только в БД)
        /// </summary>
        public async Task AddAdAsync(Ad ad)
        {
            ad.Id = await DatabaseHelper.AddAnnouncementAsync(
                ad.Title,
                ad.AutoRaiseMinutes,
                ad.IsAutoRaiseEnabled,
                ad.AccountId,
                ad.AccountLogin,
                ad.SiteId ?? "",
                ad.IsPublishedOnSite,
                ad.NextUnpublishAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                ad.NextRepublishAt?.ToString("yyyy-MM-dd HH:mm:ss")
            ).ConfigureAwait(false);

            Ads.Add(ad);
            if (ad.IsAutoRaiseEnabled)
            {
                AdScheduler.Instance.StartForAd(ad);
            }
        }

        /// <summary>
        /// Удалить объявление (только из БД)
        /// </summary>
        public async Task RemoveAdAsync(Ad ad)
        {
            if (ad.Id != 0)
                await DatabaseHelper.DeleteAnnouncementAsync(ad.Id).ConfigureAwait(false);
            Ads.Remove(ad);
            AdScheduler.Instance.RemoveForAd(ad);
        }

        /// <summary>
        /// Обновить объявление (только в БД)
        /// </summary>
        public async Task UpdateAdAsync(Ad ad)
        {
            if (ad.Id != 0)
                await DatabaseHelper.UpdateAnnouncementAsync(
                    ad.Id,
                    ad.Title,
                    ad.AutoRaiseMinutes,
                    ad.IsAutoRaiseEnabled,
                    ad.AccountId,
                    ad.AccountLogin,
                    ad.SiteId ?? "",
                    ad.IsPublishedOnSite,
                    ad.NextUnpublishAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    ad.NextRepublishAt?.ToString("yyyy-MM-dd HH:mm:ss")
                ).ConfigureAwait(false);

            AdScheduler.Instance.RemoveForAd(ad);
            if (ad.IsAutoRaiseEnabled)
                AdScheduler.Instance.StartForAd(ad);
        }

        /// <summary>
        /// Массовое добавление объявлений в БД
        /// </summary>
        public async Task AddAdsAsync(IEnumerable<AdData> ads)
        {
            foreach (var ad in ads)
            {
                var newAd = new Ad
                {
                    Title = ad.Title,
                    AutoRaiseMinutes = ad.AutoRaiseMinutes,
                    IsAutoRaiseEnabled = ad.IsAutoRaiseEnabled,
                    AccountId = ad.AccountId,
                    AccountLogin = ad.AccountLogin,
                };
                await AddAdAsync(newAd);
            }
        }
        /// <summary>
        /// Синхронизирует статус объявлений с сайтом
        /// </summary>
        public async Task SyncWithSiteAsync()
        {
            var acc = AccountManager.Instance.SelectedAccount;
            if (acc == null)
            {
                TerminalLogger.Instance.Log("[Sync] Нет выбранного аккаунта для синхронизации");
                return;
            }

            TerminalLogger.Instance.Log("[Sync] Начинаем синхронизацию с сайтом...");
            
            try
            {
                var (success, message, siteAds) = DoskaYktService.Instance.CheckAds(acc.Login, acc.Password, false);
                
                if (!success)
                {
                    TerminalLogger.Instance.Log($"[Sync] Ошибка синхронизации: {message}");
                    return;
                }

                // Обновляем статус объявлений на основе данных с сайта
                foreach (var ad in Ads)
                {
                    if (string.IsNullOrEmpty(ad.SiteId)) continue;
                    
                    var siteAd = siteAds?.FirstOrDefault(sa => sa.Id == ad.SiteId || sa.SiteId == ad.SiteId);
                    if (siteAd != null)
                    {
                        bool wasPublished = ad.IsPublished;
                        ad.IsPublished = siteAd.IsPublished;
                        ad.IsPublishedOnSite = siteAd.IsPublished;
                        
                        if (wasPublished != ad.IsPublished)
                        {
                            TerminalLogger.Instance.Log($"[Sync] Статус '{ad.Title}' изменен: {wasPublished} -> {ad.IsPublished}");
                            await UpdateAdAsync(ad);
                        }
                    }
                }
                
                TerminalLogger.Instance.Log("[Sync] Синхронизация завершена");
            }
            catch (Exception ex)
            {
                TerminalLogger.Instance.Log($"[Sync] Ошибка при синхронизации: {ex.Message}");
            }
        }

        // Для работы с сайтом используйте DoskaYktService через ViewModel
    }
}
