using System.Threading;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core.Ads
{
    public interface IAdsActions
    {
        Task<bool> ExistsAsync(string login, string password, string adId, bool showBrowser, string adTitle = null, CancellationToken cancellationToken = default);
        Task<bool> UnpublishAsync(string login, string password, string adId, bool showBrowser, string adTitle = null, CancellationToken cancellationToken = default);
        Task<bool> RepublishAsync(string login, string password, string adId, bool showBrowser, CancellationToken cancellationToken = default);
    }
}
