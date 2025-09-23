using System;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.Core.Browser;

namespace DoskaYkt_AutoManagement.Core.Auth
{
    public class AuthService : IAuthService
    {
        private readonly IBrowserSession _session;
        private bool _isLoggedIn;
        private string _currentLogin;
        private const string BaseUrl = "https://doska.ykt.ru";

        public AuthService(IBrowserSession session)
        {
            _session = session;
        }

        public bool IsLoggedIn => _isLoggedIn;
        public string CurrentLogin => _currentLogin;

        public async Task<bool> LoginAsync(string login, string password, bool showBrowser, CancellationToken cancellationToken = default)
        {
            // Fast path: already logged in with same user
            if (_isLoggedIn && string.Equals(_currentLogin, login, StringComparison.OrdinalIgnoreCase))
                return true;

            await _session.EnsureAsync(showBrowser).ConfigureAwait(false);
            var page = _session.Page;

            await page.GotoAsync(BaseUrl).ConfigureAwait(false);
            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.DOMContentLoaded).ConfigureAwait(false);
            if (await page.Locator(".ygm-userpanel_username").CountAsync().ConfigureAwait(false) > 0)
            {
                var current = (await page.Locator(".ygm-userpanel_username").First.InnerTextAsync().ConfigureAwait(false)).Trim();
                if (string.Equals(current, login, StringComparison.OrdinalIgnoreCase))
                {
                    _isLoggedIn = true; _currentLogin = login; return true;
                }
                var logout = page.Locator("a[href*='logout']");
                if (await logout.CountAsync().ConfigureAwait(false) > 0) { await logout.First.ClickAsync().ConfigureAwait(false); }
                await page.Context.ClearCookiesAsync().ConfigureAwait(false);
            }

            var returnUrl = Uri.EscapeDataString(BaseUrl);
            await page.GotoAsync($"https://id.ykt.ru/page/login?return_url={returnUrl}").ConfigureAwait(false);
            await page.Locator("input[name='login']").First.FillAsync(login).ConfigureAwait(false);
            await page.Locator("button.id-fe-button").First.ClickAsync().ConfigureAwait(false);
            await page.Locator("input[name='password']").First.FillAsync(password).ConfigureAwait(false);
            await page.Locator("button.id-fe-button:has-text('Войти')").First.ClickAsync().ConfigureAwait(false);

            // Rely on user panel appearance instead of URL/load-state which can be flaky with redirects
            await page.WaitForSelectorAsync(".ygm-userpanel_username", new() { Timeout = 90000 }).ConfigureAwait(false);
            _isLoggedIn = true; _currentLogin = login; return true;
        }

        public async Task LogoutAsync()
        {
            var page = _session.Page;
            if (page == null) return;
            try
            {
                await page.GotoAsync(BaseUrl).ConfigureAwait(false);
                var logout = page.Locator("a[href*='logout']");
                if (await logout.CountAsync().ConfigureAwait(false) > 0)
                {
                    await logout.First.ClickAsync().ConfigureAwait(false);
                }
            }
            catch { }
            finally { _isLoggedIn = false; _currentLogin = null; }
        }

        public void ResetAuth()
        {
            _isLoggedIn = false;
            _currentLogin = null;
        }
    }
}
