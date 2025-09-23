using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.MVVM.Model;
using DoskaYkt_AutoManagement.Core.Browser;
using DoskaYkt_AutoManagement.Core.Auth;
using DoskaYkt_AutoManagement.Core.Ads;

namespace DoskaYkt_AutoManagement.Core
{
    public class DoskaYktService : ISiteAutomation
    {
        private static DoskaYktService _instance;

        // SRP services (Playwright)
        private readonly IBrowserSession _browserSession;
        private readonly IAuthService _authService;
        private readonly IAdsScanner _adsScanner;
        private readonly IAdsActions _adsActions;
        private readonly IAdFetcher _adFetcher;

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
            _browserSession = new PlaywrightBrowserSession();
            _authService = new AuthService(_browserSession);
            _adsScanner = new AdsScanner(_browserSession, _authService);
            _adsActions = new AdsActions(_browserSession, _authService);
            _adFetcher = new AdFetcher(_browserSession, _authService);
        }

        public bool IsSessionActive => _browserSession.IsActive;
        public bool IsLoggedIn => _authService.IsLoggedIn;
        public string CurrentLogin => _authService.CurrentLogin;

        public void CloseSession()
        {
            Task.Run(async () =>
            {
                try { await _browserSession.CloseAsync(); } catch { }
                try { _authService.ResetAuth(); } catch { }
            });
        }

        public async Task CloseSessionAsync()
        {
            try { await _browserSession.CloseAsync(); } catch { }
            try { _authService.ResetAuth(); } catch { }
        }

        // ===== Auth =====
        public Task<bool> LoginAsync(string login, string password, bool showChrome, CancellationToken cancellationToken = default)
            => _authService.LoginAsync(login, password, showChrome, cancellationToken);

        // ===== Scan =====
        public (bool success, string message, List<AdData> ads) CheckAds(string login, string password, bool showChrome, CancellationToken cancellationToken = default)
        {
            try { return _adsScanner.CheckAdsAsync(login, password, showChrome, cancellationToken).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                TerminalLogger.Instance.Log("[CheckAds] Ошибка → " + ex.Message);
                return (false, $"Ошибка при проверке объявлений: {ex.Message}", new List<AdData>());
            }
        }

        public (bool success, string message, List<AdData> ads) CheckUnpublishedAds(string login, string password, bool showChrome, CancellationToken cancellationToken = default)
        {
            try { return _adsScanner.CheckUnpublishedAdsAsync(login, password, showChrome, cancellationToken).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                TerminalLogger.Instance.Log($"[CheckUnpublished] Ошибка → {ex.Message}");
                return (false, $"Ошибка при проверке неопубликованных объявлений: {ex.Message}", new List<AdData>());
            }
        }

        // ===== Actions =====
        public Task<bool> ExistsAdOnSiteAsync(string login, string password, string adId, bool showChrome, string adTitle = null, CancellationToken cancellationToken = default)
            => _adsActions.ExistsAsync(login, password, adId, showChrome, adTitle, cancellationToken);

        public Task<bool> UnpublishAdAsync(string login, string password, string adId, bool showChrome, string adTitle = null, CancellationToken cancellationToken = default)
            => _adsActions.UnpublishAsync(login, password, adId, showChrome, adTitle, cancellationToken);

        public Task<bool> RepublishAdAsync(string login, string password, string adId, bool showChrome, CancellationToken cancellationToken = default)
            => _adsActions.RepublishAsync(login, password, adId, showChrome, cancellationToken);

        public async Task<bool> RepostAdWithDelay(string login, string password, string siteId, string title)
        {
            var unpub = await UnpublishAdAsync(login, password, siteId, true, title);
            if (!unpub) return false;

            var minSec = Math.Max(0, Properties.Settings.Default.RepostDelayMinSec);
            var maxSec = Math.Max(minSec, Properties.Settings.Default.RepostDelayMaxSec);
            await Task.Delay(TimeSpan.FromSeconds(new System.Random().Next(minSec, maxSec + 1)));

            return await RepublishAdAsync(login, password, siteId, true);
        }

        // ===== Fetch =====
        public Task<(bool success, string message, AdData ad)> FetchAdFromByIdAsync(string login, string password, string adId, bool showChrome, CancellationToken cancellationToken = default)
            => _adFetcher.FetchByIdAsync(login, password, adId, showChrome, cancellationToken);

        public Task<(bool success, string message, AdData ad)> FetchAdFromTitleAsync(string login, string password, string adTitle, bool showChrome, CancellationToken cancellationToken = default)
            => _adFetcher.FetchByTitleAsync(login, password, adTitle, showChrome, cancellationToken);
    }
}

