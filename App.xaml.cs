using System.Configuration;
using System.Data;
using System.Net.Http;
using System.Windows;
using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;
using DoskaYkt_AutoManagement.Core;

namespace DoskaYkt_AutoManagement
{
    public partial class App : Application
    {
        public static readonly HttpClient SharedHttpClient = new HttpClient();
        private readonly DoskaYktService _service = DoskaYktService.Instance;
        private Forms.NotifyIcon? _notifyIcon;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Обработка необработанных исключений
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            MainWindow = new MainWindow();
            MainWindow.Closing += (_, __) =>
            {
                Current.Shutdown(); // гарантированно убивает процесс
            };

            // Логи пробрасываются непосредственно в MainWindow (см. MainWindow.xaml.cs)

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon("Images/DoskaYkt-Favicon3.ico"),
                Text = "DoskaYkt AutoManagement",
                Visible = false
            };

            // Контекстное меню трея
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Показать", null, (_, __) => RestoreFromTray());
            menu.Items.Add("Выход", null, (_, __) => ExitApp());
            _notifyIcon.ContextMenuStrip = menu;

            // Двойной клик по иконке — показать окно
            _notifyIcon.DoubleClick += (_, __) => RestoreFromTray();

            // 3) Подписываемся на смену состояния окна — ловим Minimized
            MainWindow.StateChanged += (_, __) =>
            {
                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    Dispatcher.BeginInvoke(new Action(MinimizeToTray));
                }
            };

            // 4) Показываем окно
            MainWindow.Show();

            // Загрузим данные из БД в менеджеры
            await Core.AccountManager.Instance.LoadAccountsAsync();
            await Core.AdManager.Instance.LoadFromDatabaseAsync();

            Core.TerminalLogger.Instance.Log("Данные загружены из базы данных.");

            // Подписываемся на события планировщика
            Core.AdScheduler.Instance.AdRepostRequested += ad =>
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        Core.TerminalLogger.Instance.Log($"[Repost] Старт цикла: снять → подождать → опубликовать. AdId={ad.Id}, Title='{ad.Title}', SiteId='{ad.SiteId}'");

                        var acc = Core.AccountManager.Instance.Accounts.FirstOrDefault(a => a.Id == ad.AccountId)
                                  ?? Core.AccountManager.Instance.SelectedAccount;
                        if (acc == null)
                        {
                            Core.TerminalLogger.Instance.Log("[Repost] Нет доступного аккаунта для операции.");
                            return;
                        }

                        var service = Core.DoskaYktService.Instance;
                        string siteId = string.IsNullOrWhiteSpace(ad.SiteId) ? null : ad.SiteId;

                        // 1) Снять с публикации
                        if (!string.IsNullOrWhiteSpace(siteId))
                        {
                            Core.TerminalLogger.Instance.Log($"[Repost] Снимаем с публикации SiteId={siteId}...");
                            var removed = await service.UnpublishAdAsync(acc.Login, acc.Password, siteId, true, ad.Title);
                            if (!removed)
                            {
                                Core.TerminalLogger.Instance.Log("[Repost] Не удалось снять объявление. Цикл прерван.");
                                return;
                            }
                        }
                        else
                        {
                            Core.TerminalLogger.Instance.Log("[Repost] У объявления нет SiteId — пропуск снятия.");
                        }

                        // 2) Подождать (RepublishDelayMinutes)
                        var delayMinutes = DoskaYkt_AutoManagement.Properties.Settings.Default.RepublishDelayMinutes;
                        if (delayMinutes < 0) delayMinutes = 0;
                        Core.TerminalLogger.Instance.Log($"[Repost] Ожидание перед публикацией: {delayMinutes} мин...");
                        await System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(delayMinutes));

                        // 3) Опубликовать снова
                        if (!string.IsNullOrWhiteSpace(siteId))
                        {
                            Core.TerminalLogger.Instance.Log($"[Repost] Публикуем снова SiteId={siteId}...");
                            var ok = await service.RepublishAdAsync(acc.Login, acc.Password, siteId, true);
                            if (ok)
                                Core.TerminalLogger.Instance.Log("[Repost] Объявление опубликовано снова.");
                            else
                                Core.TerminalLogger.Instance.Log("[Repost] Не удалось опубликовать объявление.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Core.TerminalLogger.Instance.Log("[Repost] Ошибка: " + ex.Message);
                    }
                });
            };
        }

        private void MinimizeToTray()
        {
            _notifyIcon.Visible = true;
            MainWindow.Hide();
        }

        private void RestoreFromTray()
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
            _notifyIcon.Visible = false;
        }

        private void ExitApp()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Shutdown(); // т.к. ShutdownMode=OnExplicitShutdown
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Core.TerminalLogger.Instance.LogError("Необработанное исключение", exception);
            
            // Логируем в файл для отладки
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "DoskaYktAutoManagement", "error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {exception}\n");
            }
            catch { }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Core.TerminalLogger.Instance.LogError("Исключение в UI потоке", e.Exception);
            e.Handled = true; // Предотвращаем краш приложения
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _service.CloseSession();
                _notifyIcon?.Dispose();
                SharedHttpClient?.Dispose();
            }
            catch (Exception ex)
            {
                Core.TerminalLogger.Instance.LogError("Ошибка при завершении приложения", ex);
            }
            base.OnExit(e);
        }
        public DoskaYktService Service => _service;
    }
}
