using System;
using System.Collections.Generic;
using System.Timers;
using DoskaYkt_AutoManagement.MVVM.Model;
using Timer = System.Timers.Timer;
using System.Threading.Tasks;
using System.Linq;

namespace DoskaYkt_AutoManagement.Core
{
    public class AdScheduler
    {
        private readonly Dictionary<int, Timer> _timers = new();
        // Legacy single-op lock replaced by centralized queue. Keeping minimal guard for timer drift.
        private readonly object _operationLock = new object();
        private bool _isOperationInProgress = false;

        public event Action<Ad> AdPublished;
        public event Action<Ad> AdUnpublished;
        public event Action<Ad> AdRepostRequested;
        public event Action<Ad> AdPublishRequested;
        public event Action<Ad> AdUnpublishRequested;
        
        private static readonly Lazy<AdScheduler> _instance = new(() => new AdScheduler());
        public static AdScheduler Instance => _instance.Value;


        private AdScheduler() { }

        public void StartForAd(Ad ad)
        {
            StopForAd(ad.Id);

            if (!ad.IsAutoRaiseEnabled)
            {
                TerminalLogger.Instance.Log($"[Scheduler] Автоматический режим отключен для '{ad.Title}'. Таймер не запущен.");
                return;
            }

            double delay;

            if (ad.IsPublished)
            {
                if (ad.UnpublishMinutes <= 0)
                {
                    TerminalLogger.Instance.Log($"[Scheduler] Для '{ad.Title}' не задан таймер снятия. Цикл остановлен.");
                    return;
                }
                // Таймер снятия
                if (ad.NextUnpublishAt != null && ad.NextUnpublishAt.Value > DateTime.Now)
                {
                    delay = (ad.NextUnpublishAt.Value - DateTime.Now).TotalMilliseconds;
                }
                else
                {
                    delay = ad.UnpublishMinutes * 60 * 1000;
                    ad.NextUnpublishAt = DateTime.Now.AddMinutes(ad.UnpublishMinutes);
                }

                if (delay < 1000) delay = 1000; // защита от слишком маленького или отрицательного значения

                var timer = new Timer(delay);
                timer.AutoReset = false;
                timer.Elapsed += async (s, e) =>
                {
                    timer.Stop();

                    // Минимальная защита от лавины таймеров: небольшая де-буфферизация
                    lock (_operationLock)
                    {
                        if (_isOperationInProgress)
                        {
                            var retryDelay = new Random().Next(5000, 15000);
                            TerminalLogger.Instance.Log($"[Scheduler] Таймер снятия совпал с другой операцией, отложим '{ad.Title}' на {retryDelay/1000}s");
                            var delayTimer = new Timer(retryDelay) { AutoReset = false };
                            delayTimer.Elapsed += (ds, de) => { delayTimer.Dispose(); StartForAd(ad); };
                            delayTimer.Start();
                            return;
                        }
                        _isOperationInProgress = true;
                    }

                    try
                    {
                        // Небольшой джиттер перед постановкой в очередь
                        var randomDelay = new Random().Next(2000, 7000);
                        TerminalLogger.Instance.Log($"[Scheduler] Джиттер {randomDelay/1000}s перед снятием '{ad.Title}'");
                        await Task.Delay(randomDelay);

                        TerminalLogger.Instance.Log($"[Scheduler] Таймер истёк для '{ad.Title}'. Начинаем снятие с публикации...");

                        // Дальше реальную работу делает ViewModel через очередь
                        var acc = AccountManager.Instance.SelectedAccount;
                        if (acc != null && !string.IsNullOrEmpty(ad.SiteId))
                        {
                            AdUnpublishRequested?.Invoke(ad);
                        }
                        else
                        {
                            ad.IsPublished = false;
                            ad.IsPublishedOnSite = false;
                        }
                    }
                    finally
                    {
                        lock (_operationLock)
                        {
                            _isOperationInProgress = false;
                        }
                    }

                    // Настраиваем дату следующей публикации
                    if (ad.RepublishMinutes > 0)
                    {
                        ad.NextRepublishAt = DateTime.Now.AddMinutes(ad.RepublishMinutes);
                        ad.NextUnpublishAt = null;
                    }
                    else
                    {
                        ad.NextRepublishAt = null;
                        ad.NextUnpublishAt = null;
                    }

                    // Обновляем в БД
                    await AdManager.Instance.UpdateAdAsync(ad);

                    // Таймер будет перезапущен из ViewModel после выполнения действия
                };
                timer.Start();
                _timers[ad.Id] = timer;
            }
            else
            {
                if (ad.RepublishMinutes <= 0)
                {
                    TerminalLogger.Instance.Log($"[Scheduler] Для '{ad.Title}' не задан таймер публикации. Цикл остановлен.");
                    return;
                }
                // Таймер публикации
                if (ad.NextRepublishAt != null && ad.NextRepublishAt.Value > DateTime.Now)
                {
                    delay = (ad.NextRepublishAt.Value - DateTime.Now).TotalMilliseconds;
                }
                else
                {
                    delay = ad.RepublishMinutes * 60 * 1000;
                    ad.NextRepublishAt = DateTime.Now.AddMinutes(ad.RepublishMinutes);
                }

                if (delay < 1000) delay = 1000; // защита

                var timer = new Timer(delay);
                timer.AutoReset = false;
                timer.Elapsed += async (s, e) =>
                {
                    timer.Stop();

                    // Минимальная защита от лавины таймеров: небольшая де-буфферизация
                    lock (_operationLock)
                    {
                        if (_isOperationInProgress)
                        {
                            var retryDelay = new Random().Next(2000, 7000);
                            TerminalLogger.Instance.Log($"[Scheduler] Таймер публикации совпал с другой операцией, отложим '{ad.Title}' на {retryDelay/1000}s");
                            var delayTimer = new Timer(retryDelay) { AutoReset = false };
                            delayTimer.Elapsed += (ds, de) => { delayTimer.Dispose(); StartForAd(ad); };
                            delayTimer.Start();
                            return;
                        }
                        _isOperationInProgress = true;
                    }

                    try
                    {
                        // Небольшой джиттер перед постановкой в очередь
                        var randomDelay = new Random().Next(2000, 7000);
                        TerminalLogger.Instance.Log($"[Scheduler] Джиттер {randomDelay/1000}s перед публикацией '{ad.Title}'");
                        await Task.Delay(randomDelay);

                        TerminalLogger.Instance.Log($"[Scheduler] Таймер истёк для '{ad.Title}'. Начинаем публикацию...");

                        // Дальше реальную работу делает ViewModel через очередь
                        var acc = AccountManager.Instance.SelectedAccount;
                        if (acc != null && !string.IsNullOrEmpty(ad.SiteId))
                        {
                            AdPublishRequested?.Invoke(ad);
                        }
                        else
                        {
                            ad.IsPublished = true;
                            ad.IsPublishedOnSite = true;
                        }
                    }
                    finally
                    {
                        lock (_operationLock)
                        {
                            _isOperationInProgress = false;
                        }
                    }

                    // Настраиваем дату следующего снятия
                    if (ad.UnpublishMinutes > 0)
                    {
                        ad.NextUnpublishAt = DateTime.Now.AddMinutes(ad.UnpublishMinutes);
                        ad.NextRepublishAt = null;
                    }
                    else
                    {
                        ad.NextUnpublishAt = null;
                        ad.NextRepublishAt = null;
                    }

                    // Обновляем в БД
                    await AdManager.Instance.UpdateAdAsync(ad);

                    // Таймер будет перезапущен из ViewModel после выполнения действия
                };
                timer.Start();
                _timers[ad.Id] = timer;
            }
        }

        public void StopForAd(int adId)
        {
            if (_timers.TryGetValue(adId, out var timer))
            {
                timer.Stop();
                timer.Dispose();
                _timers.Remove(adId);
            }
        }

        public void RemoveForAd(Ad ad)
        {
            StopForAd(ad.Id);
        }

        public void StartRepostForAd(Ad ad)
        {
            AdRepostRequested?.Invoke(ad);
        }

        public void UnpublishAdOnSite(Ad ad)
        {
            AdUnpublishRequested?.Invoke(ad);
        }

        public void PublishAdOnSite(Ad ad)
        {
            AdPublishRequested?.Invoke(ad);
        }

        public void StopAll()
        {
            foreach (var kvp in _timers)
            {
                try
                {
                    kvp.Value.Stop();
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    TerminalLogger.Instance.Log($"[Scheduler] Ошибка при остановке таймера {kvp.Key}: {ex.Message}");
                }
            }
            _timers.Clear();
            TerminalLogger.Instance.Log("[Scheduler] Все таймеры остановлены.");
        }

        public void RestartAllTimers()
        {
            TerminalLogger.Instance.Log("[Scheduler] Перезапуск всех таймеров...");
            var ads = AdManager.Instance.Ads.Where(ad => ad.IsAutoRaiseEnabled).ToList();
            foreach (var ad in ads)
            {
                StartForAd(ad);
            }
            TerminalLogger.Instance.Log($"[Scheduler] Перезапущено {ads.Count} таймеров.");
        }
    }
}
