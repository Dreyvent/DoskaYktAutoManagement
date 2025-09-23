using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.MVVM.Model;

namespace DoskaYkt_AutoManagement.Core.Ads
{
    public interface IAdFetcher
    {
        Task<(bool success, string message, AdData ad)> FetchByIdAsync(string login, string password, string adId, bool showBrowser, CancellationToken cancellationToken = default);
        Task<(bool success, string message, AdData ad)> FetchByTitleAsync(string login, string password, string adTitle, bool showBrowser, CancellationToken cancellationToken = default);
    }
}
