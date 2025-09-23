using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.MVVM.Model;

namespace DoskaYkt_AutoManagement.Core
{
    public interface ISiteAutomation
    {
        bool IsSessionActive { get; }
        bool IsLoggedIn { get; }
        string CurrentLogin { get; }

        void CloseSession();
        Task CloseSessionAsync();

        Task<bool> LoginAsync(string login, string password, bool showBrowser, CancellationToken cancellationToken = default);

        (bool success, string message, List<AdData> ads) CheckAds(string login, string password, bool showBrowser, CancellationToken cancellationToken = default);
        (bool success, string message, List<AdData> ads) CheckUnpublishedAds(string login, string password, bool showBrowser, CancellationToken cancellationToken = default);

        Task<bool> ExistsAdOnSiteAsync(string login, string password, string adId, bool showBrowser, string adTitle = null, CancellationToken cancellationToken = default);
        Task<bool> UnpublishAdAsync(string login, string password, string adId, bool showBrowser, string adTitle = null, CancellationToken cancellationToken = default);
        Task<bool> RepublishAdAsync(string login, string password, string adId, bool showBrowser, CancellationToken cancellationToken = default);
        Task<bool> RepostAdWithDelay(string login, string password, string siteId, string title);

        Task<(bool success, string message, AdData ad)> FetchAdFromByIdAsync(string login, string password, string adId, bool showBrowser, CancellationToken cancellationToken = default);
        Task<(bool success, string message, AdData ad)> FetchAdFromTitleAsync(string login, string password, string adTitle, bool showBrowser, CancellationToken cancellationToken = default);
    }
}


