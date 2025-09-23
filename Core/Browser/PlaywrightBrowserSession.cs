using System.Threading.Tasks;
using Microsoft.Playwright;

namespace DoskaYkt_AutoManagement.Core.Browser
{
    public class PlaywrightBrowserSession : IBrowserSession
    {
        private IPlaywright _playwright;
        private IBrowser _browser;
        private IBrowserContext _context;
        private IPage _page;

        public bool IsActive => _browser != null;
        public IPage Page => _page;

        public async Task EnsureAsync(bool showBrowser)
        {
            if (_browser != null) return;
            if (_playwright == null) _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = !showBrowser }).ConfigureAwait(false);
            _context = await _browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 800 } }).ConfigureAwait(false);
            _page = await _context.NewPageAsync().ConfigureAwait(false);
        }

        public async Task CloseAsync()
        {
            try { if (_page != null) await _page.CloseAsync(); } catch { }
            try { if (_context != null) await _context.CloseAsync(); } catch { }
            try { if (_browser != null) await _browser.CloseAsync(); } catch { }
            try { _page = null; _context = null; _browser = null; _playwright?.Dispose(); _playwright = null; } catch { }
        }
    }
}
