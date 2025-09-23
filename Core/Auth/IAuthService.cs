using System.Threading;
using System.Threading.Tasks;

namespace DoskaYkt_AutoManagement.Core.Auth
{
    public interface IAuthService
    {
        bool IsLoggedIn { get; }
        string CurrentLogin { get; }
        Task<bool> LoginAsync(string login, string password, bool showBrowser, CancellationToken cancellationToken = default);
        Task LogoutAsync();
    }
}
