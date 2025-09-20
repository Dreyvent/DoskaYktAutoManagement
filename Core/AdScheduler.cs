using System;
using System.Collections.Generic;
using System.Timers;
using DoskaYkt_AutoManagement.MVVM.Model;
using Timer = System.Timers.Timer;

namespace DoskaYkt_AutoManagement.Core
{
    public class AdScheduler
    {
        private readonly Dictionary<int, Timer> _timers = new();

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
            // НЕ запускаем перепубликацию сразу - только планируем таймеры

            if (ad.IsPublished)
            {
                if (ad.NextUnpublishAt == null)
                    ad.NextUnpublishAt = DateTime.Now.AddMinutes(30); // дефолт

                var delay = (ad.NextUnpublishAt.Value - DateTime.Now).TotalMilliseconds;
                if (delay < 0) delay = 1000; // защита

                var timer = new Timer(delay);
                timer.Elapsed += (s, e) =>
                {
                    timer.Stop();
                    ad.IsPublished = false;
                    ad.NextUnpublishAt = null;
                    ad.NextRepublishAt = DateTime.Now.AddMinutes(10);

                    AdUnpublished?.Invoke(ad);
                    // НЕ вызываем AdUnpublishRequested - это снимает объявление с сайта!
                    StartForAd(ad); // перезапуск на следующий цикл
                };
                timer.AutoReset = false;
                timer.Start();

                _timers[ad.Id] = timer;
            }
            else
            {
                if (ad.NextRepublishAt == null)
                    ad.NextRepublishAt = DateTime.Now.AddMinutes(10); // дефолт

                var delay = (ad.NextRepublishAt.Value - DateTime.Now).TotalMilliseconds;
                if (delay < 0) delay = 1000; // защита

                var timer = new Timer(delay);
                timer.Elapsed += (s, e) =>
                {
                    timer.Stop();
                    ad.IsPublished = true;
                    ad.NextRepublishAt = null;
                    ad.NextUnpublishAt = DateTime.Now.AddMinutes(30);

                    AdPublished?.Invoke(ad);
                    // НЕ вызываем AdPublishRequested - это публикует объявление на сайте!
                    StartForAd(ad); // перезапуск
                };
                timer.AutoReset = false;
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

        /// <summary>
        /// Запустить перепубликацию объявления (только когда это действительно нужно)
        /// </summary>
        public void StartRepostForAd(Ad ad)
        {
            AdRepostRequested?.Invoke(ad);
        }

        /// <summary>
        /// БЕЗОПАСНО снять объявление с публикации на сайте (только когда явно запрошено)
        /// </summary>
        public void UnpublishAdOnSite(Ad ad)
        {
            AdUnpublishRequested?.Invoke(ad);
        }

        /// <summary>
        /// БЕЗОПАСНО опубликовать объявление на сайте (только когда явно запрошено)
        /// </summary>
        public void PublishAdOnSite(Ad ad)
        {
            AdPublishRequested?.Invoke(ad);
        }

        public void StopAll()
        {
            foreach (var kvp in _timers)
            {
                kvp.Value.Stop();
                kvp.Value.Dispose();
            }
            _timers.Clear();
        }
    }
}
