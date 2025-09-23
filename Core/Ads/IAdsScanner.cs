using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.MVVM.Model;

namespace DoskaYkt_AutoManagement.Core.Ads
{
    public interface IAdsScanner
    {
        Task<(bool success, string message, List<AdData> ads)> CheckAdsAsync(string login, string password, bool showBrowser, CancellationToken cancellationToken = default);
        Task<(bool success, string message, List<AdData> ads)> CheckUnpublishedAdsAsync(string login, string password, bool showBrowser, CancellationToken cancellationToken = default);
    }
}
