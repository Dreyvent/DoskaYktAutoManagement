using System;
using DoskaYkt_AutoManagement.Properties;

namespace DoskaYkt_AutoManagement.Core
{
    public class SleepAtNightManager
    {
        private static readonly Lazy<SleepAtNightManager> _instance = new(() => new SleepAtNightManager());
        public static SleepAtNightManager Instance => _instance.Value;

        private SleepAtNightManager() { }

        public event Action SleepStarted;
        public event Action SleepEnded;

        private System.Timers.Timer _checkTimer;
        private bool _isSleeping;

        public bool IsEnabled => Settings.Default.SleepAtNight;

        public bool IsActiveNow
        {
            get
            {
                if (!IsEnabled) return false;
                var now = DateTime.Now;
                var start = GetTodayStart();
                var end = GetTodayEnd();
                return now >= start && now < end;
            }
        }

        public int StartHour => Settings.Default.NightPauseStartHour;
        public int EndHour => Settings.Default.NightPauseEndHour;

        private DateTime GetTodayStart()
        {
            var now = DateTime.Now;
            return new DateTime(now.Year, now.Month, now.Day, StartHour, 0, 0);
        }

        private DateTime GetTodayEnd()
        {
            var now = DateTime.Now;
            var end = new DateTime(now.Year, now.Month, now.Day, EndHour, 0, 0);
            if (EndHour <= StartHour)
                end = end.AddDays(1);
            return end;
        }

        public void Initialize()
        {
            _checkTimer = new System.Timers.Timer(60_000); // проверка каждую минуту
            _checkTimer.Elapsed += (s, e) => CheckState();
            _checkTimer.AutoReset = true;
            _checkTimer.Start();

            // начальная проверка
            CheckState();
        }

        private void CheckState()
        {
            if (!IsEnabled)
            {
                if (_isSleeping)
                {
                    _isSleeping = false;
                    SleepEnded?.Invoke();
                }
                return;
            }

            var active = IsActiveNow;

            if (active && !_isSleeping)
            {
                _isSleeping = true;
                SleepStarted?.Invoke();
                TerminalLogger.Instance.Log($"[SleepAtNight] Активирован ночной режим {StartHour:00}:00–{EndHour:00}:00");
            }
            else if (!active && _isSleeping)
            {
                _isSleeping = false;
                SleepEnded?.Invoke();
                TerminalLogger.Instance.Log("[SleepAtNight] Ночной режим завершён");
            }
        }
    }
}
