using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.MVVM.Model;
using System.ComponentModel;

namespace DoskaYkt_AutoManagement.Core
{
    public class DoskaYktService
    {
        private static DoskaYktService _instance;
        private IWebDriver _driver;
        private WebDriverWait _wait;
        private string _lastDriverKind; // "chrome" | "firefox"

        public static DoskaYktService Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new DoskaYktService();
                return _instance;
            }
        }

        private DoskaYktService()
        {
            _driver = null;
            _wait = null;
            _lastDriverKind = null;
            _isLoggedIn = false;
            _currentLogin = null;
        }

        public bool IsSessionActive => _driver != null;

        public bool IsLoggedIn => _isLoggedIn && _driver != null;

        public string CurrentLogin => _currentLogin;

        private bool _isLoggedIn = false;
        private string? _currentLogin;

        private const string BaseUrl = "https://doska.ykt.ru";

        public void CloseSession()
        {
            // Сбрасываем состояние немедленно, закрытие выполняем в фоне, чтобы не блокировать UI
            var driverToClose = _driver;
            _driver = null;
            _wait = null;
            _isLoggedIn = false;
            _currentLogin = null;

            Task.Run(() =>
            {
                try
                {
                    try { driverToClose?.Quit(); }
                    catch (Exception ex)
                    {
                        TerminalLogger.Instance.Log($"[Session] Ошибка при закрытии браузера: {ex.Message}");
                    }

                    try { driverToClose?.Dispose(); } catch { }
                    TryKillLeftoverDrivers();
                    TerminalLogger.Instance.Log("[Session] Сессия браузера закрыта.");
                }
                catch { }
            });
        }

        private void TryKillLeftoverDrivers()
        {
            try
            {
                // Убиваем только процессы драйверов, не браузеры
                var names = new List<string>();
                if (string.Equals(_lastDriverKind, "chrome", StringComparison.OrdinalIgnoreCase)) names.Add("chromedriver");
                if (string.Equals(_lastDriverKind, "firefox", StringComparison.OrdinalIgnoreCase)) names.Add("geckodriver");
                foreach (var name in names)
                {
                    Process[] procs;
                    try { procs = Process.GetProcessesByName(name); }
                    catch { procs = Array.Empty<Process>(); }

                    foreach (var p in procs)
                    {
                        try
                        {
                            if (!p.HasExited)
                            {
                                // Не ждём завершения процесса, чтобы не зависнуть
                                p.Kill();
                            }
                        }
                        catch (Win32Exception) { }
                        catch (InvalidOperationException) { }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void EnsureSession(bool showChrome)
        {
            if (_driver != null) return;

            var options = BuildChromeOptions(showChrome);
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            
            // Дополнительные опции для стабильности
            if (Properties.Settings.Default.HideDriverWindow)
            {
                options.AddArgument("--disable-gpu");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
            }

            try
            {
                // Создаём сервис драйвера с правильным скрытием CMD окна
                var service = ChromeDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = Properties.Settings.Default.HideDriverWindow;
                
                _driver = new ChromeDriver(service, options);
                _lastDriverKind = "chrome";
                _wait = new WebDriverWait(new SystemClock(), _driver, TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(500));
                
                // Устанавливаем таймауты для стабильности
                _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
                _driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
                _driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(30);
                
                TerminalLogger.Instance.Log($"[Session] ChromeDriver создан (HideCMD={Properties.Settings.Default.HideDriverWindow})");
            }
            catch (Exception ex)
            {
                TerminalLogger.Instance.Log($"[Session] Ошибка создания драйвера: {ex.Message}");
                throw;
            }
        }


        private void EnsureLoggedIn(string login, string password)
        {
            if (!IsLoggedIn || !string.Equals(CurrentLogin, login, StringComparison.OrdinalIgnoreCase))
            {
                _driver.Navigate().GoToUrl(BaseUrl);
                PerformLogin(_driver, _wait, login, password);
            }
        }

        // Авторизация на сайте
        public Task<bool> LoginAsync(string login, string password, bool showChrome, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    EnsureSession(showChrome);
                    EnsureLoggedIn(login, password);
                    // Убедимся, что логин действительно выполнен
                    _wait.Until(d => d.FindElements(By.CssSelector(".ygm-userpanel_username")).Any());
                    return true;
                }
                catch (Exception ex)
                {
                    Core.TerminalLogger.Instance.Log("[Login] Ошибка → " + ex.Message);
                    return false;
                }
            }, cancellationToken);
        }

        // Получить список объявлений пользователя
        // зачем оно мне непонятно, если есть CheckAds, стоит призадуматься
        public Task<List<AdData>> GetMyAdsAsync(string login, string password, bool showChrome, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var options = BuildChromeOptions(showChrome);
                var ads = new List<AdData>();
                try
                {
                    var wait = new WebDriverWait(new SystemClock(), _driver, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(250));
                    _driver.Navigate().GoToUrl("https://doska.ykt.ru/profile/posts");
                    wait.Until(d => d.FindElements(By.CssSelector("div.d-post_desc")).Any());
                    foreach (var el in _driver.FindElements(By.CssSelector("div.d-post_desc")))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        ads.Add(new AdData { Title = el.Text.Trim() });
                    }
                }
                catch (Exception ex)
                {
                    Core.TerminalLogger.Instance.Log("[GetMyAds] Ошибка → " + ex.Message);
                }
                return ads;
            }, cancellationToken);
        }

        public (bool success, string message, List<AdData> ads) CheckAds(string login, string password, bool showChrome, CancellationToken cancellationToken = default)
        {
            var ads = new List<AdData>();
            try
            {
                EnsureSession(showChrome);
                EnsureLoggedIn(login, password);
                // Проверяем, залогинен ли кто-то (ищем имя пользователя в верхнем меню)
                var userPanel = _driver.FindElements(By.CssSelector(".ygm-userpanel_username")).FirstOrDefault();
                if (userPanel != null)
                {
                    string currentUser = userPanel.Text.Trim();
                    Core.TerminalLogger.Instance.Log($"[CheckAds] Уже авторизованы как '{currentUser}'");
                    if (!string.Equals(currentUser, login, StringComparison.OrdinalIgnoreCase))
                    {
                        Core.TerminalLogger.Instance.Log($"[CheckAds] Но логин не совпадает с ожидаемым '{login}' → выходим и логинимся заново");
                        // Кликаем «Выйти»
                        var logoutLink = _driver.FindElements(By.CssSelector("a[href*='logout']")).FirstOrDefault();
                        if (logoutLink != null)
                        {
                            logoutLink.Click(); _wait.Until(d => d.FindElement(By.LinkText("Войти")));
                            _driver.Manage().Cookies.DeleteAllCookies(); _driver.Navigate().GoToUrl("https://doska.ykt.ru/");
                        } // Теперь авторизация заново
                        PerformLogin(_driver, _wait, login, password);
                    }
                }
                else
                {
                    // Нет userpanel → показывается кнопка "Войти"
                    Core.TerminalLogger.Instance.Log("[CheckAds] Пользователь не авторизован → выполняем вход");
                    cancellationToken.ThrowIfCancellationRequested(); PerformLogin(_driver, _wait, login, password);
                }
                _wait.Until(d => d.Url.Contains("doska.ykt.ru"));
                var usernameEl = _wait.Until(d =>
                {
                    var el = d.FindElements(By.CssSelector(".ygm-userpanel_username")).FirstOrDefault();
                    return el != null && !string.IsNullOrWhiteSpace(el.Text) ? el : null;
                });
                Core.TerminalLogger.Instance.Log($"[CheckAds] Авторизовались как '{usernameEl.Text}'");


                // Теперь переходим в "Мои объявления" они уже опубликованные
                _driver.Navigate().GoToUrl("https://doska.ykt.ru/profile/posts");
                Core.TerminalLogger.Instance.Log("[CheckAds] Открыли профиль /profile/posts, ждём загрузку списка объявлений...");
                // Устойчивое ожидание: несколько возможных селекторов и увеличенный таймаут
                var profileWait = new WebDriverWait(new SystemClock(), _driver, TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(300));

                bool listReady = false;
                bool isEmpty = false;
                try
                {
                    // Ждём готовность документа
                    profileWait.Until(d =>
                    {
                        try
                        {
                            return ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete";
                        }
                        catch { return false; }
                    });
                    
                    // Проверяем, есть ли сообщение о пустом списке
                    var emptyMessage = _driver.FindElements(By.CssSelector("div.d-pc_no_posts_title"));
                    if (emptyMessage.Any())
                    {
                        Core.TerminalLogger.Instance.Log("[CheckAds] Список объявлений пуст - найдено сообщение 'У вас пока нет объявлений'");
                        isEmpty = true;
                        listReady = true;
                    }
                    else
                    {
                        // Ждём любой из селекторов списка объявлений
                        profileWait.Until(d =>
                        {
                            return d.FindElements(By.CssSelector("div.d-post_desc, a.d-post_link, div.d-post")).Any();
                        });
                        listReady = true;
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    Core.TerminalLogger.Instance.Log("[CheckAds] Не дождались списка объявлений за 60 сек. Сохраняю заголовок и URL страницы для диагностики.");
                    try
                    {
                        Core.TerminalLogger.Instance.Log("[CheckAds] PageTitle=" + _driver.Title);
                        Core.TerminalLogger.Instance.Log("[CheckAds] PageUrl=" + _driver.Url);
                    }
                    catch { }
                }
                
                if (!listReady)
                {
                    return (false, "Не удалось загрузить список объявлений (timeout).", ads);
                }
                
                if (isEmpty)
                {
                    Core.TerminalLogger.Instance.Log("[CheckAds] Список объявлений пуст, возвращаем пустой результат");
                    return (true, "Список объявлений пуст", ads);
                }
                // Собираем объявления только из профиля
                var adElements = _driver.FindElements(By.CssSelector("div.d-post_desc"));
                if (!adElements.Any())
                {
                    // Фоллбек: пробуем другие контейнеры
                    adElements = _driver.FindElements(By.CssSelector("div.d-post, a.d-post_link"));
                }

                foreach (var descEl in adElements)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var title = string.Empty;
                    try
                    {
                        title = (descEl.Text ?? string.Empty).Trim();
                    }
                    catch { }

                    var isPublished = true; // по умолчанию, так как doska.ykt.ru/profile/posts а не doska.ykt.ru/profile/posts/finished

                    string adId = null;
                    try
                    {
                        // Попытка 1: взять ID из ссылки объявления (оканчивается на /{digits})
                        IWebElement linkEl = null;
                        try
                        {
                            linkEl = descEl.FindElement(By.XPath("ancestor::a[contains(@class,'d-post_link')][1]"));
                        }
                        catch { }
                        if (linkEl != null)
                        {
                            var href = linkEl.GetAttribute("href") ?? string.Empty;
                            var mHref = Regex.Match(href, @"/(\d+)$");
                            if (mHref.Success) adId = mHref.Groups[1].Value;
                        }
                        // Попытка 2: взять ID из блока сервисной информации (текст со встроенными цифрами)
                        if (string.IsNullOrEmpty(adId))
                        {
                            IWebElement idSpan = null;
                            try
                            {
                                idSpan = descEl.FindElement(By.XPath("ancestor::div[contains(@class,'d-post')][1]//div[contains(@class,'d-post_info-service')]/span"));
                            }
                            catch { }
                            var text = idSpan?.Text?.Trim() ?? string.Empty;
                            var m = Regex.Match(text, @"\d+");
                            if (m.Success) adId = m.Value;
                        }
                    }
                    catch { }
                    ads.Add(new AdData { Title = title, Id = adId, IsPublished = isPublished });
                }
                Core.TerminalLogger.Instance.Log($"[CheckAds] Найдено {ads.Count} объявлений в профиле");
                return (true, $"Найдено {ads.Count} объявлений", ads);
            }
            catch (Exception ex)
            {
                Core.TerminalLogger.Instance.Log("[CheckAds] Ошибка → " + ex.Message);
                return (false, $"Ошибка при проверке объявлений: {ex.Message}", ads);
            }
        }

        // универсальный парсер карточки
        // честно мало понимаю зачем оно мне, но по идее должно переносить объявления из AdData в Ad или типа того
        private AdData ParsePost(IWebElement post)
        {
            try
            {
                var titleEl = post.FindElements(By.CssSelector(".d-post_desc")).FirstOrDefault();
                var title = titleEl?.Text?.Trim();

                string id = null;
                var link = post.FindElements(By.CssSelector("a.d-post_link")).FirstOrDefault();
                var href = link?.GetAttribute("href") ?? string.Empty;
                var mHref = Regex.Match(href, @"/(\d+)$");
                if (mHref.Success) id = mHref.Groups[1].Value;

                if (string.IsNullOrEmpty(id))
                {
                    var idSpan = post.FindElements(By.CssSelector(".d-post_info-service span")).FirstOrDefault();
                    var mText = Regex.Match(idSpan?.Text ?? string.Empty, @"\d+");
                    if (mText.Success) id = mText.Value;
                }

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title)) return null;

                return new AdData { SiteId = id, Title = title };
            }
            catch { return null; }
        }

        private bool PerformLogin(IWebDriver driver, WebDriverWait wait, string login, string password)
        {
            try
            {
                // Проверка: уже авторизованы в ЭТОМ driver?
                try
                {
                    var existingUser = driver.FindElements(By.CssSelector(".ygm-userpanel_username")).FirstOrDefault();
                    var currentUserText = existingUser?.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(currentUserText))
                    {
                        Core.TerminalLogger.Instance.Log($"[Account] Уже авторизованы как '{currentUserText}' в текущем окне.");
                        if (string.Equals(currentUserText, login, StringComparison.OrdinalIgnoreCase))
                        {
                            _isLoggedIn = true;
                            _currentLogin = login;
                            return true;
                        }
                        // Другой пользователь → выходим
                        var logoutLink = driver.FindElements(By.CssSelector("a[href*='logout']")).FirstOrDefault();
                        if (logoutLink != null)
                        {
                            logoutLink.Click();
                            wait.Until(d => d.FindElement(By.LinkText("Войти")));
                            driver.Manage().Cookies.DeleteAllCookies();
                            driver.Navigate().GoToUrl("https://doska.ykt.ru/");
                        }
                    }
                }
                catch { }

                // Нажимаем кнопку "Войти"
                Core.TerminalLogger.Instance.Log("[Account] Открытие формы входа");
                var loginLink = wait.Until(d => d.FindElement(By.LinkText("Войти")));
                loginLink.Click();

                // Логин
                var loginBox = wait.Until(d => d.FindElement(By.Name("login")));
                loginBox.Clear();
                loginBox.SendKeys(login);

                var nextBtn = wait.Until(d => d.FindElement(By.CssSelector("button.id-fe-button")));
                wait.Until(_ => nextBtn.Enabled);
                nextBtn.Click();

                // Пароль
                var passBox = wait.Until(d => d.FindElement(By.Name("password")));
                passBox.Clear();
                passBox.SendKeys(password);

                var loginBtn = wait.Until(d => d.FindElements(By.CssSelector("button.id-fe-button")).First(b => b.Text.Contains("Войти")));
                wait.Until(_ => loginBtn.Enabled);
                loginBtn.Click();

                // Подтверждаем, что вошли
                wait.Until(d => d.FindElements(By.CssSelector(".ygm-userpanel_username")).Any());

                Core.TerminalLogger.Instance.Log($"[Account] Вход выполнен для '{login}'");
                _isLoggedIn = true;
                _currentLogin = login;
                return true;
            }
            catch (Exception ex)
            {
                Core.TerminalLogger.Instance.Log($"[Account] Ошибка входа: {ex.Message}");
                _isLoggedIn = false;
                _currentLogin = null;
                return false;
            }
        }

        // ================== WebDriver helpers ==================
        private bool JsClickWithRetries(Func<IWebElement> elementSupplier, string actionLog, int attempts = 3)
        {
            for (int i = 1; i <= attempts; i++)
            {
                try
                {
                    var el = elementSupplier();
                    if (el == null) throw new NoSuchElementException("Element supplier returned null");
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].scrollIntoView({block:'center'});", el);
                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", el);
                    return true;
                }
                catch (StaleElementReferenceException)
                {
                    if (i == attempts) throw;
                    Thread.Sleep(300 * i);
                }
                catch (ElementNotInteractableException)
                {
                    if (i == attempts) throw;
                    Thread.Sleep(300 * i);
                }
                catch (WebDriverException)
                {
                    if (i == attempts) throw;
                    Thread.Sleep(300 * i);
                }
                catch
                {
                    if (i == attempts) throw;
                    Thread.Sleep(200 * i);
                }
            }
            return false;
        }

        private static ChromeOptions BuildChromeOptions(bool showChrome)
        {
            var options = new ChromeOptions();
            if (!showChrome)
            {
                options.AddArgument("--headless=new");
                options.AddArgument("--disable-gpu");
                options.AddArgument("--window-size=1920,1080");
            }
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--enable-unsafe-swiftshader");
            return options;
        }

        private static string ResolveSiteAdIdOnPage(IWebDriver driver, string adTitle)
        {
            try
            {
                // Ищем карточку по точному совпадению заголовка
                var cards = driver.FindElements(By.CssSelector(".d-post"));
                foreach (var card in cards)
                {
                    try
                    {
                        var titleEl = card.FindElements(By.CssSelector(".d-post_desc")).FirstOrDefault();
                        var title = (titleEl?.Text ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(title) && string.Equals(title, adTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            // Пытаемся вытащить ID из ссылки
                            var link = card.FindElements(By.CssSelector("a.d-post_link")).FirstOrDefault();
                            var href = link?.GetAttribute("href") ?? string.Empty;
                            var mHref = Regex.Match(href, @"/(\d+)$");
                            if (mHref.Success) return mHref.Groups[1].Value;

                            // или из сервисного блока
                            var idSpan = card.FindElements(By.CssSelector(".d-post_info-service span")).FirstOrDefault();
                            var mText = Regex.Match(idSpan?.Text ?? string.Empty, @"\d+");
                            if (mText.Success) return mText.Value;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public Task<bool> ExistsAdOnSiteAsync(string login, string password, string adId, bool showChrome, string adTitle = null, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        EnsureSession(showChrome);
                        EnsureLoggedIn(login, password);
                        Core.TerminalLogger.Instance.Log("[ExistsAd] Открываем профиль /profile/posts");
                    _driver.Navigate().GoToUrl("https://doska.ykt.ru/profile/posts");
                    // Устойчивое ожидание: до 90 сек и проверка пустого списка
                    var profileWait = new WebDriverWait(new SystemClock(), _driver, TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(300));
                    profileWait.Until(d => { try { return ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete"; } catch { return false; } });
                    var emptyMessage = _driver.FindElements(By.CssSelector("div.d-pc_no_posts_title"));
                    if (emptyMessage.Any())
                    {
                        Core.TerminalLogger.Instance.Log("[ExistsAd] Список объявлений пуст, объявление не найдено");
                        return false;
                    }
                    profileWait.Until(d => d.FindElements(By.CssSelector(".d-post"))?.Any() == true);

                        // Если adId не цифры — пытаемся найти по заголовку
                        var idToCheck = Regex.IsMatch(adId ?? string.Empty, @"^\d+$") ? adId : null;
                        if (idToCheck == null && !string.IsNullOrWhiteSpace(adTitle))
                        {
                            idToCheck = ResolveSiteAdIdOnPage(_driver, adTitle);
                        }
                        if (idToCheck == null) return false;

                        var found = _driver.FindElements(By.CssSelector($".d-post_info-service span"))
                                            .Any(s => (s.Text ?? string.Empty).Contains(idToCheck));
                        return found;
                    }
                    catch (Exception ex)
                    {
                        attempts++;
                        Core.TerminalLogger.Instance.Log("[ExistsAd] Ошибка → " + ex.Message + (attempts < 3 ? " → retry" : ""));
                        if (attempts >= 3) return false;
                        Thread.Sleep(TimeSpan.FromSeconds(2 * attempts));
                    }
                }
            }, cancellationToken);
        }

        public Task<bool> UnpublishAdAsync(string login, string password, string adId, bool showChrome, string adTitle = null, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        EnsureSession(showChrome);
                        EnsureLoggedIn(login, password);

                        Core.TerminalLogger.Instance.Log("[Unpublish] Открываем профиль /profile/posts");
                    _driver.Navigate().GoToUrl("https://doska.ykt.ru/profile/posts");
                    var profileWait = new WebDriverWait(new SystemClock(), _driver, TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(300));
                    profileWait.Until(d => { try { return ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete"; } catch { return false; } });
                        
                        // Проверяем, есть ли сообщение о пустом списке
                    var emptyMessage = _driver.FindElements(By.CssSelector("div.d-pc_no_posts_title"));
                        if (emptyMessage.Any())
                        {
                            Core.TerminalLogger.Instance.Log("[Unpublish] Список объявлений пуст, нечего снимать с публикации");
                            return false;
                        }
                    profileWait.Until(d => d.FindElements(By.CssSelector(".d-post"))?.Any() == true);

                        string idToUse = Regex.IsMatch(adId ?? string.Empty, @"^\d+$") ? adId : null;
                        if (idToUse == null && !string.IsNullOrWhiteSpace(adTitle))
                        {
                            idToUse = ResolveSiteAdIdOnPage(_driver, adTitle);
                            if (idToUse == null) return false;
                        }

                        // Находим карточку по номеру
                        var posts = _driver.FindElements(By.CssSelector(".d-post"));
                        foreach (var post in posts)
                        {
                            try
                            {
                                var idSpan = post.FindElements(By.CssSelector(".d-post_info-service span")).FirstOrDefault();
                                if (idSpan == null || !idSpan.Text.Contains(idToUse)) continue;

                                // Кнопка "Снять с публикации"
                                var btn = post.FindElements(By.CssSelector($"a.d-post_info-control-btn[data-micromodal-trigger='js-d-cancel_reason-modal-{idToUse}']")).FirstOrDefault();
                                if (btn == null)
                                {
                                    btn = post.FindElements(By.CssSelector("a.d-post_info-control-btn[data-micromodal-trigger*='js-d-cancel_reason-modal-']")).FirstOrDefault();
                                }
                                if (btn == null) continue;

                            if (!JsClickWithRetries(() => {
                                var fresh = post.FindElements(By.CssSelector($"a.d-post_info-control-btn[data-micromodal-trigger='js-d-cancel_reason-modal-{idToUse}']")).FirstOrDefault()
                                            ?? post.FindElements(By.CssSelector("a.d-post_info-control-btn[data-micromodal-trigger*='js-d-cancel_reason-modal-']")).FirstOrDefault();
                                return fresh;
                            }, $"Open unpublish modal {idToUse}")) return false;

                                // Ждем появления модального окна
                                bool reasonsSelected = false;
                                for (int rtry = 1; rtry <= 3 && !reasonsSelected; rtry++)
                                {
                                    try
                                    {
                                        _wait.Until(d => d.FindElements(By.CssSelector("input[name='reason_code']")).Any());
                                        // Проверяем, есть ли радиокнопки причин
                                        var reasons = _driver.FindElements(By.CssSelector("input[name='reason_code']"));
                                        if (reasons.Any())
                                        {
                                            // ищем "Пропустить вопрос" по значению 310
                                            var skipReason = reasons.FirstOrDefault(r => r.GetAttribute("value") == "310");
                                            if (skipReason != null)
                                            {
                                                TerminalLogger.Instance.Log($"[Unpublish] Найдена опция 'Пропустить вопрос' для объявления {idToUse}");
                                                ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", skipReason);
                                            }
                                            else
                                            {
                                                // если "пропустить" нет, ищем по тексту
                                                var skipByText = _driver.FindElements(By.XPath("//label[contains(text(), 'Пропустить') or contains(text(), 'пропустить')]//input[@name='reason_code']")).FirstOrDefault();
                                                if (skipByText != null)
                                                {
                                                    TerminalLogger.Instance.Log($"[Unpublish] Найдена опция 'Пропустить' по тексту для объявления {idToUse}");
                                                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", skipByText);
                                                }
                                                else
                                                {
                                                    // если ничего не найдено, просто берём первый вариант
                                                    TerminalLogger.Instance.Log($"[Unpublish] Опция 'Пропустить' не найдена, выбираем первый вариант для объявления {idToUse}");
                                                    ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", reasons.First());
                                                }
                                            }
                                            reasonsSelected = true;
                                        }
                                    }
                                    catch (WebDriverTimeoutException)
                                    {
                                        if (rtry == 3)
                                        {
                                            TerminalLogger.Instance.Log($"[Unpublish] Модальное окно не появилось для объявления {idToUse}");
                                            throw;
                                        }
                                        Thread.Sleep(300 * rtry);
                                    }
                                    catch (StaleElementReferenceException)
                                    {
                                        if (rtry == 3) throw;
                                        Thread.Sleep(200 * rtry);
                                    }
                                    catch (ElementNotInteractableException)
                                    {
                                        if (rtry == 3) throw;
                                        Thread.Sleep(200 * rtry);
                                    }
                                }

                                // Подтверждение в модалке
                                if (!JsClickWithRetries(() => _wait.Until(d => d.FindElements(By.CssSelector("button.d-cancel_reason-modal-delete")).FirstOrDefault()), $"Confirm unpublish {idToUse}"))
                                    return false;

                                // Ждём, пока карточка пропадёт из активных
                                _wait.Until(d => { try { var stillThere = d.FindElements(By.CssSelector(".d-post .d-post_info-service span")).Any(s => (s.Text ?? string.Empty).Contains(idToUse)); return !stillThere; } catch { return true; } });
                                return true;
                            }
                            catch { }
                        }
                        // Не нашли карточку — вероятно страница не успела прогрузиться правильно. Считаем транзиентной ошибкой.
                        throw new NoSuchElementException("Ad card not found to unpublish");
                    }
                    catch (Exception ex)
                    {
                        attempts++;
                        Core.TerminalLogger.Instance.Log("[Unpublish] Ошибка → " + ex.Message + (attempts < 3 ? " → retry" : ""));
                        if (attempts >= 3) return false;
                        try { _driver.Navigate().Refresh(); } catch { }
                        Thread.Sleep(TimeSpan.FromSeconds(2 * attempts));
                    }
                }
            }, cancellationToken);
        }

        public Task<bool> RepublishAdAsync(string login, string password, string adId, bool showChrome, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        EnsureSession(showChrome);
                        EnsureLoggedIn(login, password);

                        // Переходим в Неопубликованные
                        _driver.Navigate().GoToUrl("https://doska.ykt.ru/profile/posts/finished");
                        _wait.Until(d => d.FindElements(By.CssSelector(".d-post")).Any());

                        // Найдём карточку с нужным id
                        var posts = _driver.FindElements(By.CssSelector(".d-post"));
                        foreach (var post in posts)
                        {
                            try
                            {
                                // Ищем id в ссылке .d-post_link или в сервисном блоке
                                string idOnCard = null;
                                var link = post.FindElements(By.CssSelector("a.d-post_link")).FirstOrDefault();
                                var href = link?.GetAttribute("href") ?? string.Empty;
                                var mHref = Regex.Match(href, @"/(\d+)$");
                                if (mHref.Success) idOnCard = mHref.Groups[1].Value;

                                if (string.IsNullOrEmpty(idOnCard))
                                {
                                    var idSpan = post.FindElements(By.CssSelector(".d-post_info-service span")).FirstOrDefault();
                                    var mText = Regex.Match(idSpan?.Text ?? string.Empty, @"\d+");
                                    if (mText.Success) idOnCard = mText.Value;
                                }

                                if (!string.Equals(idOnCard, adId, StringComparison.Ordinal)) continue;

                                // Нажимаем "Активировать"
                                if (!JsClickWithRetries(() => post.FindElements(By.CssSelector("a.d-post_info-sell")).FirstOrDefault(), $"Activate {adId}")) return false;

                                // Ожидаем страницу выбора пакета
                                _wait.Until(d => d.FindElements(By.CssSelector(".d-servicePacks_tw")).Any());

                                // Убедимся, что вкладка "Бесплатно" активна, иначе активируем
                                var freeTab = _driver.FindElements(By.CssSelector(".d-sp_tab.d-sp_tab--default.d-servicePacks_tw"))
                                    .FirstOrDefault(el => (el.GetAttribute("class") ?? string.Empty).Contains("active"));
                                if (freeTab == null)
                                {
                                    // клик по label внутри блока с id="free"
                                    var freeInput = _driver.FindElements(By.CssSelector("input#free.d-sp_input.d-servicePacks_input")).FirstOrDefault();
                                    if (freeInput != null)
                                    {
                                        ((IJavaScriptExecutor)_driver).ExecuteScript("arguments[0].click();", freeInput);
                                    }
                                }

                                // Нажимаем зелёную кнопку подтверждения
                            if (!JsClickWithRetries(() => _wait.Until(d => d.FindElements(By.CssSelector("button.yui-btn.yui-btn--green.d-post-add-submit.d-servicePacks_submit_btn.free")).FirstOrDefault()), $"Submit free plan {adId}")) return false;

                                // Ждём редирект на страницу объявления /{id}
                                _wait.Until(d => { try { var url = d.Url ?? string.Empty; return Regex.IsMatch(url, @"https://doska\.ykt\.ru/\d+$"); } catch { return false; } });
                                return true;
                            }
                            catch { }
                        }
                        return false;
                    }
                    catch (Exception ex)
                    {
                        attempts++;
                        Core.TerminalLogger.Instance.Log("[RepublishAd] Ошибка → " + ex.Message + (attempts < 3 ? " → retry" : ""));
                        if (attempts >= 3) return false;
                        Thread.Sleep(TimeSpan.FromSeconds(2 * attempts));
                    }
                }
            }, cancellationToken);
        }

        public async Task<bool> RepostAdWithDelay(string login, string password, string siteId, string title)
        {
            var unpub = await UnpublishAdAsync(login, password, siteId, true, title);
            if (!unpub) return false;

            await Task.Delay(TimeSpan.FromSeconds(new Random().Next(3, 8)));

            return await RepublishAdAsync(login, password, siteId, true);
        }

        // Получить объявления по ID
        public Task<(bool success, string message, AdData ad)> FetchAdFromByIdAsync(string login, string password, string adId, bool showChrome, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    EnsureSession(showChrome);
                    EnsureLoggedIn(login, password);
                    _driver.Navigate().GoToUrl($"https://doska.ykt.ru/profile/posts");
                    var posts = _driver.FindElements(By.CssSelector(".d-post"));
                    foreach (var post in posts)
                    {
                        try
                        {
                            var idSpan = post.FindElements(By.CssSelector(".d-post_info-service span")).FirstOrDefault();
                            if (idSpan == null || !(idSpan.Text ?? string.Empty).Contains(adId)) continue;

                            var titleEl = post.FindElements(By.CssSelector(".d-post_desc")).FirstOrDefault();
                            var title = titleEl?.Text?.Trim() ?? string.Empty;

                            var data = new AdData { Id = adId, Title = title };
                            return (true, "OK", data);
                        }
                        catch { }
                    }
                    return (false, "Объявление с указанным ID не найдено в списке.", null);
                }
                catch (Exception ex)
                {
                    Core.TerminalLogger.Instance.Log($"[FetchById] Ошибка → {ex.Message}");
                    return (false, ex.Message, null);
                }
            }, cancellationToken);
        }

        // Получить объявления по названию
        public Task<(bool success, string message, AdData ad)> FetchAdFromTitleAsync(string login, string password, string adTitle, bool showChrome, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    EnsureSession(showChrome);
                    EnsureLoggedIn(login, password);
                    _driver.Navigate().GoToUrl("https://doska.ykt.ru/profile/posts");
                    var finishedWait = new WebDriverWait(new SystemClock(), _driver, TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(300));
                    finishedWait.Until(d => d.FindElements(By.CssSelector(".d-post")).Any());

                    var posts = _driver.FindElements(By.CssSelector(".d-post"));
                    foreach (var post in posts)
                    {
                        try
                        {
                            var titleEl = post.FindElements(By.CssSelector(".d-post_desc")).FirstOrDefault();
                            var title = titleEl?.Text?.Trim() ?? string.Empty;
                            if (!string.Equals(title, adTitle, StringComparison.OrdinalIgnoreCase)) continue;

                            string id = null;
                            var link = post.FindElements(By.CssSelector("a.d-post_link")).FirstOrDefault();
                            var href = link?.GetAttribute("href") ?? string.Empty;
                            var mHref = System.Text.RegularExpressions.Regex.Match(href, @"/(\d+)$");
                            if (mHref.Success) id = mHref.Groups[1].Value;

                            if (string.IsNullOrEmpty(id))
                            {
                                var idSpan = post.FindElements(By.CssSelector(".d-post_info-service span")).FirstOrDefault();
                                var mText = System.Text.RegularExpressions.Regex.Match(idSpan?.Text ?? string.Empty, @"\d+");
                                if (mText.Success) id = mText.Value;
                            }

                            var data = new AdData { Id = id, Title = title };
                            return (true, "OK", data);
                        }
                        catch { }
                    }
                    return (false, "Объявление с указанным заголовком не найдено.", null);
                }
                catch (Exception ex)
                {
                    Core.TerminalLogger.Instance.Log($"[FetchByTitle] Ошибка → {ex.Message}");
                    return (false, ex.Message, null);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Проверяет неопубликованные объявления на сайте
        /// </summary>
        public (bool success, string message, List<AdData> ads) CheckUnpublishedAds(string login, string password, bool showChrome, CancellationToken cancellationToken = default)
        {
            var ads = new List<AdData>();
            try
            {
                EnsureSession(showChrome);
                EnsureLoggedIn(login, password);

                TerminalLogger.Instance.Log("[CheckUnpublished] Открываем страницу неопубликованных объявлений...");
                _driver.Navigate().GoToUrl("https://doska.ykt.ru/profile/posts/finished");
                
                var wait = new WebDriverWait(new SystemClock(), _driver, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
                
                // Проверяем, есть ли сообщение о пустом списке
                var emptyMessage = _driver.FindElements(By.CssSelector("div.d-pc_no_posts_title"));
                if (emptyMessage.Any())
                {
                    TerminalLogger.Instance.Log("[CheckUnpublished] Список неопубликованных объявлений пуст");
                    return (true, "Список неопубликованных объявлений пуст", ads);
                }
                
                wait.Until(d => d.FindElements(By.CssSelector(".d-post")).Any());

                var adElements = _driver.FindElements(By.CssSelector(".d-post"));

                foreach (var descEl in adElements)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    try
                    {
                        var titleEl = descEl.FindElements(By.CssSelector(".d-post_desc")).FirstOrDefault();
                        var title = titleEl?.Text?.Trim() ?? string.Empty;

                        string adId = null;
                        var linkEl = descEl.FindElements(By.CssSelector("a.d-post_link")).FirstOrDefault();
                        if (linkEl != null)
                        {
                            var href = linkEl.GetAttribute("href") ?? string.Empty;
                            var mHref = Regex.Match(href, @"/(\d+)$");
                            if (mHref.Success) adId = mHref.Groups[1].Value;
                        }

                        if (!string.IsNullOrEmpty(adId) && !string.IsNullOrEmpty(title))
                        {
                            ads.Add(new AdData { Id = adId, Title = title, IsPublished = false });
                        }
                    }
                    catch { }
                }

                TerminalLogger.Instance.Log($"[CheckUnpublished] Найдено {ads.Count} неопубликованных объявлений");
                return (true, $"Найдено {ads.Count} неопубликованных объявлений", ads);
            }
            catch (Exception ex)
            {
                TerminalLogger.Instance.Log($"[CheckUnpublished] Ошибка → {ex.Message}");
                return (false, $"Ошибка при проверке неопубликованных объявлений: {ex.Message}", ads);
            }
        }
    }
}

