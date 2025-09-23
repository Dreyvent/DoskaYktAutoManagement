using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.Core.Auth;
using DoskaYkt_AutoManagement.Core.Browser;
using DoskaYkt_AutoManagement.MVVM.Model;

namespace DoskaYkt_AutoManagement.Core.Ads
{
    public class AdFetcher : IAdFetcher
    {
        private readonly IBrowserSession _session;
        private readonly IAuthService _auth;

        public AdFetcher(IBrowserSession session, IAuthService auth)
        {
            _session = session;
            _auth = auth;
        }

        public async Task<(bool success, string message, AdData ad)> FetchByIdAsync(string login, string password, string adId, bool showBrowser, CancellationToken cancellationToken = default)
        {
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = _session.Page;
            await page.GotoAsync("https://doska.ykt.ru/profile/posts").ConfigureAwait(false);
            await page.WaitForSelectorAsync(".d-post, div.d-pc_no_posts_title", new() { Timeout = 60000 }).ConfigureAwait(false);
            var posts = page.Locator(".d-post");
            int count = await posts.CountAsync().ConfigureAwait(false);
            for (int i = 0; i < count; i++)
            {
                var item = posts.Nth(i);
                var info = item.Locator(".d-post_info-service span");
                if (await info.CountAsync().ConfigureAwait(false) == 0) continue;
                var text = await info.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
                if (!text.Contains(adId)) continue;

                var titleEl = item.Locator(".d-post_desc").First;
                var title = (await titleEl.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                return (true, "OK", new AdData { Id = adId, Title = title });
            }
            return (false, "Объявление с указанным ID не найдено в списке.", null);
        }

        public async Task<(bool success, string message, AdData ad)> FetchByTitleAsync(string login, string password, string adTitle, bool showBrowser, CancellationToken cancellationToken = default)
        {
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = _session.Page;
            await page.GotoAsync("https://doska.ykt.ru/profile/posts").ConfigureAwait(false);
            await page.WaitForSelectorAsync(".d-post, div.d-pc_no_posts_title", new() { Timeout = 60000 }).ConfigureAwait(false);

            var posts = page.Locator(".d-post");
            int count = await posts.CountAsync().ConfigureAwait(false);
            for (int i = 0; i < count; i++)
            {
                var item = posts.Nth(i);
                var titleEl = item.Locator(".d-post_desc").First;
                var title = (await titleEl.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                if (!string.Equals(title, adTitle, System.StringComparison.OrdinalIgnoreCase)) continue;

                string id = null;
                var link = item.Locator("a.d-post_link");
                if (await link.CountAsync().ConfigureAwait(false) > 0)
                {
                    var href = await link.First.GetAttributeAsync("href").ConfigureAwait(false) ?? string.Empty;
                    var mHref = Regex.Match(href, @"/(\d+)$");
                    if (mHref.Success) id = mHref.Groups[1].Value;
                }
                if (string.IsNullOrEmpty(id))
                {
                    var info = item.Locator(".d-post_info-service span");
                    if (await info.CountAsync().ConfigureAwait(false) > 0)
                    {
                        var text = await info.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
                        var m = Regex.Match(text, @"\d+");
                        if (m.Success) id = m.Value;
                    }
                }
                return (true, "OK", new AdData { Id = id, Title = title });
            }
            return (false, "Объявление с указанным заголовком не найдено.", null);
        }
    }
}
