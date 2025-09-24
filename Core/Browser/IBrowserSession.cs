using System.Threading.Tasks;
using Microsoft.Playwright;

namespace DoskaYkt_AutoManagement.Core.Browser
{
    public interface IBrowserSession
    {
        bool IsActive { get; }
        IPage Page { get; }
        Task EnsureAsync(bool showBrowser);
        Task<IPage> CreatePageAsync(bool showBrowser);
        Task CloseAsync();
    }
}
