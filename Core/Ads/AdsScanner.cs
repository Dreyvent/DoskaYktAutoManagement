using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.Core.Auth;
using DoskaYkt_AutoManagement.Core.Browser;
using DoskaYkt_AutoManagement.MVVM.Model;
using Microsoft.Playwright;

namespace DoskaYkt_AutoManagement.Core.Ads
{
    public class AdsScanner : IAdsScanner
    {
        private readonly IBrowserSession _session;
        private readonly IAuthService _auth;

        public AdsScanner(IBrowserSession session, IAuthService auth)
        {
            _session = session;
            _auth = auth;
        }

        public async Task<(bool success, string message, List<AdData> ads)> CheckAdsAsync(string login, string password, bool showBrowser, CancellationToken cancellationToken = default)
        {
            var ads = new List<AdData>();
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = await _session.CreatePageAsync(showBrowser).ConfigureAwait(false);

            string baseUrl = "https://doska.ykt.ru/profile/posts";
            int safetyPages = 50; // предохранитель от бесконечных переходов
            int pagesScanned = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await page.GotoAsync(baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);

                // Пустой список
                if (await page.Locator("div.d-pc_no_posts_title").CountAsync().ConfigureAwait(false) > 0)
                {
                    break;
                }

                // Ждём элементы
                try
                {
                    await page.WaitForSelectorAsync(".d-post, a.d-post_link, .d-post_desc", new() { Timeout = 30000 }).ConfigureAwait(false);
                }
                catch
                {
                    await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
                    if (await page.Locator("div.d-pc_no_posts_title").CountAsync().ConfigureAwait(false) > 0)
                        break;
                    await page.WaitForSelectorAsync(".d-post, a.d-post_link, .d-post_desc", new() { Timeout = 30000 }).ConfigureAwait(false);
                }

                // Сбор карточек текущей страницы
                var postLocator = page.Locator(".d-post");
                int count = await postLocator.CountAsync().ConfigureAwait(false);
                if (count == 0)
                {
                    var descs = page.Locator(".d-post_desc");
                    int dcount = await descs.CountAsync().ConfigureAwait(false);
                    for (int i = 0; i < dcount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var title = (await descs.Nth(i).InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                        ads.Add(new AdData { Title = title, IsPublished = true });
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var item = postLocator.Nth(i);
                        string title = "";
                        try { title = (await item.Locator(".d-post_desc").First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim(); } catch { }

                        string adId = null;
                        try
                        {
                            if (await item.Locator("a.d-post_link").CountAsync().ConfigureAwait(false) > 0)
                            {
                                var href = await item.Locator("a.d-post_link").First.GetAttributeAsync("href").ConfigureAwait(false) ?? string.Empty;
                                var mHref = Regex.Match(href, @"/(\d+)$");
                                if (mHref.Success) adId = mHref.Groups[1].Value;
                            }
                            if (string.IsNullOrEmpty(adId))
                            {
                                var infoSpan = item.Locator(".d-post_info-service span");
                                if (await infoSpan.CountAsync().ConfigureAwait(false) > 0)
                                {
                                    var text = (await infoSpan.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                                    var m = Regex.Match(text, @"\d+");
                                    if (m.Success) adId = m.Value;
                                }
                            }
                        }
                        catch { }
                        ads.Add(new AdData { Title = title, Id = adId, IsPublished = true });
                    }
                }

                pagesScanned++;
                if (pagesScanned >= safetyPages)
                    break;

                // Пытаемся перейти на следующую страницу
                var nextLink = page.Locator(".d-pager a:has(i.yui-icon-chevron-right)");
                if (await nextLink.CountAsync().ConfigureAwait(false) > 0)
                {
                    try
                    {
                        await nextLink.First.ClickAsync();
                        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded).ConfigureAwait(false);
                        // Обновим baseUrl на текущий адрес, чтобы сохранить fromTime/page
                        baseUrl = page.Url;
                        continue;
                    }
                    catch { break; }
                }
                break;
            }

            return (true, $"Найдено {ads.Count} объявлений", ads);
            
        }

        public async Task<(bool success, string message, List<AdData> ads)> CheckUnpublishedAdsAsync(string login, string password, bool showBrowser, CancellationToken cancellationToken = default)
        {
            var ads = new List<AdData>();
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = await _session.CreatePageAsync(showBrowser).ConfigureAwait(false);

            string baseUrl = "https://doska.ykt.ru/profile/posts/finished";
            int safetyPages = 50;
            int pagesScanned = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await page.GotoAsync(baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
                if (await page.Locator("div.d-pc_no_posts_title").CountAsync().ConfigureAwait(false) > 0)
                    break;

                await page.WaitForSelectorAsync(".d-post, a.d-post_link, .d-post_desc", new() { Timeout = 30000 }).ConfigureAwait(false);

                var postLocator = page.Locator(".d-post");
                int count = await postLocator.CountAsync().ConfigureAwait(false);
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = postLocator.Nth(i);
                    try
                    {
                        var titleEl = item.Locator(".d-post_desc").First;
                        var title = (await titleEl.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();

                        string adId = null;
                        var linkEl = item.Locator("a.d-post_link");
                        if (await linkEl.CountAsync().ConfigureAwait(false) > 0)
                        {
                            var href = await linkEl.First.GetAttributeAsync("href").ConfigureAwait(false) ?? string.Empty;
                            var mHref = Regex.Match(href, @"/(\d+)$");
                            if (mHref.Success) adId = mHref.Groups[1].Value;
                        }

                        if (!string.IsNullOrEmpty(adId) && !string.IsNullOrEmpty(title))
                        {
                            ads.Add(new AdData { Id = adId, Title = title, IsPublished = false });
                        }
                    }
                    catch { }
                }

                pagesScanned++;
                if (pagesScanned >= safetyPages)
                    break;

                var nextLink = page.Locator(".d-pager a:has(i.yui-icon-chevron-right)");
                if (await nextLink.CountAsync().ConfigureAwait(false) > 0)
                {
                    try
                    {
                        await nextLink.First.ClickAsync();
                        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded).ConfigureAwait(false);
                        baseUrl = page.Url;
                        continue;
                    }
                    catch { break; }
                }
                break;
            }

            return (true, $"Найдено {ads.Count} неопубликованных объявлений", ads);
        }
    }
}
