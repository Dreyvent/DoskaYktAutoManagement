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
        private Timer _nightSleepTimer; // Глобальный таймер для sleep at night
        private bool _nightSleepActive = false;

        public event Action<Ad> AdPublished;
        public event Action<Ad> AdUnpublished;
        public event Action<Ad> AdRepostRequested;
        public event Action<Ad> AdPublishRequested;
        public event Action<Ad> AdUnpublishRequested;
        
        private static readonly Lazy<AdScheduler> _instance = new(() => new AdScheduler());
        public static AdScheduler Instance => _instance.Value;

        private AdScheduler()
        {
        }

        public void Initialize()
        {
            SetupNightSleepTimer();
        }

        private void SetupNightSleepTimer()
        {
            if (_nightSleepTimer != null)
            {
                _nightSleepTimer.Stop();
                _nightSleepTimer.Dispose();
            }
            _nightSleepTimer = new Timer();
            _nightSleepTimer.AutoReset = false;
            _nightSleepTimer.Elapsed += (s, e) => NightSleepTimerTick();
            ScheduleNextNightSleepEvent();
        }

        private void ScheduleNextNightSleepEvent()
        {
            bool sleepAtNight = DoskaYkt_AutoManagement.Properties.Settings.Default.SleepAtNight;
            int nightStart = DoskaYkt_AutoManagement.Properties.Settings.Default.NightPauseStartHour;
            int nightEnd = DoskaYkt_AutoManagement.Properties.Settings.Default.NightPauseEndHour;
            if (!sleepAtNight)
            {
                _nightSleepTimer.Stop();
                _nightSleepActive = false;
                return;
            }
            DateTime now = DateTime.Now;
            DateTime todayStart = new DateTime(now.Year, now.Month, now.Day, nightStart, 0, 0);
            DateTime todayEnd = new DateTime(now.Year, now.Month, now.Day, nightEnd, 0, 0);
            if (nightEnd <= nightStart)
                todayEnd = todayEnd.AddDays(1); // интервал через полночь
            DateTime nextEvent;
            if (!_nightSleepActive && now >= todayStart && now < todayEnd)
            {
                // Уже ночь, sleep режим должен быть активен
                _nightSleepActive = true;
                RemoveAllTimers();
                TerminalLogger.Instance.Log($"[Scheduler] Ночная пауза активирована с {nightStart:00}:00 до {nightEnd:00}:00 (таймеры удалены)");
                nextEvent = todayEnd;
            }
            else if (!_nightSleepActive)
            {
                // День, ждём до начала ночи
                if (now < todayStart)
                    nextEvent = todayStart;
                else
                    nextEvent = todayStart.AddDays(1);
            }
            else if (_nightSleepActive && now < todayEnd)
            {
                // Уже ночь, ждём до конца ночи
                nextEvent = todayEnd;
            }
            else
            {
                // Сейчас день, sleep не активен, ждём до следующей ночи
                nextEvent = todayStart.AddDays(1);
            }
            double delay = (nextEvent - now).TotalMilliseconds;
            if (delay < 1000) delay = 1000;
            _nightSleepTimer.Interval = delay;
            _nightSleepTimer.Start();
        }

        private void NightSleepTimerTick()
        {
            bool sleepAtNight = DoskaYkt_AutoManagement.Properties.Settings.Default.SleepAtNight;
            int nightStart = DoskaYkt_AutoManagement.Properties.Settings.Default.NightPauseStartHour;
            int nightEnd = DoskaYkt_AutoManagement.Properties.Settings.Default.NightPauseEndHour;
            if (!sleepAtNight)
            {
                _nightSleepActive = false;
                return;
            }
            DateTime now = DateTime.Now;
            DateTime todayStart = new DateTime(now.Year, now.Month, now.Day, nightStart, 0, 0);
            DateTime todayEnd = new DateTime(now.Year, now.Month, now.Day, nightEnd, 0, 0);
            if (nightEnd <= nightStart)
                todayEnd = todayEnd.AddDays(1);
            if (!_nightSleepActive && now >= todayStart && now < todayEnd)
            {
                _nightSleepActive = true;
                RemoveAllTimers();
                TerminalLogger.Instance.Log($"[Scheduler] Ночная пауза активирована с {nightStart:00}:00 до {nightEnd:00}:00 (таймеры удалены)");
            }
            else if (_nightSleepActive && now >= todayEnd)
            {
                _nightSleepActive = false;
                ApplyAllTimers();
                TerminalLogger.Instance.Log($"[Scheduler] Ночная пауза завершена (таймеры применены)");
            }
            ScheduleNextNightSleepEvent();
        }

        private void RemoveAllTimers()
        {
            TerminalLogger.Instance.Log("[Scheduler] Удаление всех таймеров (ночная пауза)");
            foreach (var ad in AdManager.Instance.Ads)
            {
                ad.IsAutoRaiseEnabled = false;
                ad.NextUnpublishAt = null;
                ad.NextRepublishAt = null;
            }
            StopAll();
        }

        private void ApplyAllTimers()
        {
            TerminalLogger.Instance.Log("[Scheduler] Применение таймеров после ночной паузы");
            foreach (var ad in AdManager.Instance.Ads)
            {
                if (ad.IsAutoRaiseEnabled)
                {
                    StartForAd(ad);
                }
            }
        }

        public void StartForAd(Ad ad)
        {
            StopForAd(ad.Id);

            // Если sleep at night активен, не запускать таймеры ночью
            if (DoskaYkt_AutoManagement.Properties.Settings.Default.SleepAtNight && _nightSleepActive)
            {
                TerminalLogger.Instance.Log($"[Scheduler] Ночной режим: '{ad.Title}' не будет запущено до окончания паузы.");
                return;
            }

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
                DateTime targetTime;
                if (ad.NextUnpublishAt != null && ad.NextUnpublishAt.Value > DateTime.Now)
                {
                    targetTime = ad.NextUnpublishAt.Value;
                }
                else
                {
                    targetTime = DateTime.Now.AddMinutes(ad.UnpublishMinutes);
                    ad.NextUnpublishAt = targetTime;
                }
                delay = (targetTime - DateTime.Now).TotalMilliseconds;
                if (delay < 1000) delay = 1000;

                var timer = new Timer(delay);
                timer.AutoReset = false;
                timer.Elapsed += async (s, e) =>
                {
                    // Не запускать действия, если активна ночная пауза
                    if (DoskaYkt_AutoManagement.Properties.Settings.Default.SleepAtNight && _nightSleepActive)
                    {
                        TerminalLogger.Instance.Log($"[Scheduler] [{ad.Title}] Событие снятия пропущено из-за ночной паузы.");
                        return;
                    }
                    timer.Stop();

                    TerminalLogger.Instance.Log($"[Scheduler] Таймер истёк для '{ad.Title}'. Ставим снятие в очередь...");

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

                    await AdManager.Instance.UpdateAdAsync(ad);
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
                DateTime targetTime;
                if (ad.NextRepublishAt != null && ad.NextRepublishAt.Value > DateTime.Now)
                {
                    targetTime = ad.NextRepublishAt.Value;
                }
                else
                {
                    targetTime = DateTime.Now.AddMinutes(ad.RepublishMinutes);
                    ad.NextRepublishAt = targetTime;
                }
                delay = (targetTime - DateTime.Now).TotalMilliseconds;
                if (delay < 1000) delay = 1000;

                var timer = new Timer(delay);
                timer.AutoReset = false;
                timer.Elapsed += async (s, e) =>
                {
                    // Не запускать действия, если активна ночная пауза
                    if (DoskaYkt_AutoManagement.Properties.Settings.Default.SleepAtNight && _nightSleepActive)
                    {
                        TerminalLogger.Instance.Log($"[Scheduler] [{ad.Title}] Событие публикации пропущено из-за ночной паузы.");
                        return;
                    }
                    timer.Stop();

                    TerminalLogger.Instance.Log($"[Scheduler] Таймер истёк для '{ad.Title}'. Ставим публикацию в очередь...");

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

                    await AdManager.Instance.UpdateAdAsync(ad);
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
