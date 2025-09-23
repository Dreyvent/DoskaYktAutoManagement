using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DoskaYkt_AutoManagement.Core.Auth;
using DoskaYkt_AutoManagement.Core.Browser;
using Microsoft.Playwright;

namespace DoskaYkt_AutoManagement.Core.Ads
{
    public class AdsActions : IAdsActions
    {
        private readonly IBrowserSession _session;
        private readonly IAuthService _auth;

        public AdsActions(IBrowserSession session, IAuthService auth)
        {
            _session = session;
            _auth = auth;
        }

        public async Task<bool> ExistsAsync(string login, string password, string adId, bool showBrowser, string adTitle = null, CancellationToken cancellationToken = default)
        {
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = _session.Page;
            await page.GotoAsync("https://doska.ykt.ru/profile/posts", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            try
            {
                await page.WaitForSelectorAsync(".d-post, div.d-pc_no_posts_title", new() { Timeout = 60000 }).ConfigureAwait(false);
                string idToCheck = Regex.IsMatch(adId ?? string.Empty, @"^\d+$") ? adId : null;
                if (idToCheck == null && !string.IsNullOrWhiteSpace(adTitle))
                {
                    var posts = page.Locator(".d-post");
                    int count = await posts.CountAsync().ConfigureAwait(false);
                    for (int i = 0; i < count; i++)
                    {
                        var item = posts.Nth(i);
                        var title = (await item.Locator(".d-post_desc").First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                        if (!string.Equals(title, adTitle, System.StringComparison.OrdinalIgnoreCase)) continue;
                        var link = item.Locator("a.d-post_link");
                        if (await link.CountAsync().ConfigureAwait(false) > 0)
                        {
                            var href = await link.First.GetAttributeAsync("href").ConfigureAwait(false) ?? string.Empty;
                            var mHref = Regex.Match(href, @"/(\d+)$");
                            if (mHref.Success) { idToCheck = mHref.Groups[1].Value; break; }
                        }
                        var info = item.Locator(".d-post_info-service span");
                        if (await info.CountAsync().ConfigureAwait(false) > 0)
                        {
                            var text = await info.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
                            var m = Regex.Match(text, @"\d+");
                            if (m.Success) { idToCheck = m.Value; break; }
                        }
                    }
                }
                if (string.IsNullOrEmpty(idToCheck)) return false;
                var any = await page.Locator(".d-post .d-post_info-service span").AllInnerTextsAsync().ConfigureAwait(false);
                return any.Any(t => (t ?? string.Empty).Contains(idToCheck));
            }
            catch { return false; }
        }

        public async Task<bool> UnpublishAsync(string login, string password, string adId, bool showBrowser, string adTitle = null, CancellationToken cancellationToken = default)
        {
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = _session.Page;
            await page.GotoAsync("https://doska.ykt.ru/profile/posts", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
            await page.WaitForSelectorAsync(".d-post, div.d-pc_no_posts_title", new() { Timeout = 60000 }).ConfigureAwait(false);

            string idToUse = Regex.IsMatch(adId ?? string.Empty, @"^\d+$") ? adId : null;
            var posts = page.Locator(".d-post");
            int count = await posts.CountAsync().ConfigureAwait(false);
            for (int i = 0; i < count; i++)
            {
                var item = posts.Nth(i);
                if (idToUse == null && !string.IsNullOrWhiteSpace(adTitle))
                {
                    var title = (await item.Locator(".d-post_desc").First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty).Trim();
                    if (!string.Equals(title, adTitle, System.StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (idToUse != null)
                {
                    var info = item.Locator(".d-post_info-service span");
                    if (await info.CountAsync().ConfigureAwait(false) == 0) continue;
                    var text = await info.First.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
                    if (!text.Contains(idToUse)) continue;
                }

                // Open unpublish modal
                var btn = item.Locator("a.d-post_info-control-btn[data-micromodal-trigger*='js-d-cancel_reason-modal-']");
                if (await btn.CountAsync().ConfigureAwait(false) == 0) continue;
                await btn.First.ClickAsync().ConfigureAwait(false);

                // Scope strictly to the open micromodal container
                var modalRoot = page.Locator(".micromodal-slide.is-open .modal__container.d-cancel_reason-modal");
                await modalRoot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 }).ConfigureAwait(false);

                // Click label text "Пропустить вопрос" inside the modal
                var skipLabel = modalRoot.Locator("label:has-text('Пропустить вопрос')");
                if (await skipLabel.CountAsync().ConfigureAwait(false) > 0)
                {
                    await skipLabel.First.ClickAsync().ConfigureAwait(false);
                }
                else
                {
                    var shortSkip = modalRoot.Locator("label:has-text('Пропустить')");
                    if (await shortSkip.CountAsync().ConfigureAwait(false) > 0)
                        await shortSkip.First.ClickAsync().ConfigureAwait(false);
                    else
                    {
                        var any = modalRoot.Locator("input[name='reason_code']");
                        if (await any.CountAsync().ConfigureAwait(false) > 0)
                            await any.First.CheckAsync().ConfigureAwait(false);
                    }
                }

                // Confirm inside the modal
                var confirm = modalRoot.Locator("button.d-cancel_reason-modal-delete");
                await confirm.First.ClickAsync().ConfigureAwait(false);

                // Wait for modal to close
                await modalRoot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 60000 }).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        public async Task<bool> RepublishAsync(string login, string password, string adId, bool showBrowser, CancellationToken cancellationToken = default)
        {
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = _session.Page;

            await page.GotoAsync("https://doska.ykt.ru/profile/posts/finished", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
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

                // Click Activate
                var activate = item.Locator("a.d-post_info-sell");
                if (await activate.CountAsync().ConfigureAwait(false) == 0) continue;
                await activate.First.ClickAsync().ConfigureAwait(false);

                // Wait service packs container
                await page.WaitForSelectorAsync(".d-sp_tabs", new() { Timeout = 60000 }).ConfigureAwait(false);

                // Click the default (free) tab label to ensure activation
                var freeTabLabel = page.Locator(".d-sp_tab.d-sp_tab--default label.d-sp_tab_inner");
                if (await freeTabLabel.CountAsync().ConfigureAwait(false) > 0)
                {
                    await freeTabLabel.First.ClickAsync().ConfigureAwait(false);
                }

                // Ensure free panel active
                try { await page.WaitForSelectorAsync("#content-free.d-servicePacks_content--active", new() { Timeout = 3000 }).ConfigureAwait(false); } catch { }

                // As a fallback, check the free radio
                var freeRadio = page.Locator("input#free.d-sp_input.d-servicePacks_input");
                if (await freeRadio.CountAsync().ConfigureAwait(false) > 0 && !(await freeRadio.First.IsCheckedAsync().ConfigureAwait(false)))
                {
                    await freeRadio.First.CheckAsync().ConfigureAwait(false);
                }

                // Submit
                var submit = page.Locator("button.yui-btn.yui-btn--green.d-post-add-submit.d-servicePacks_submit_btn.free");
                if (await submit.CountAsync().ConfigureAwait(false) == 0)
                    submit = page.Locator("button.yui-btn.yui-btn--green");
                await submit.First.ClickAsync().ConfigureAwait(false);

                // Wait redirected to /{id} or profile posts
                try { await page.WaitForURLAsync("**/" + adId, new() { Timeout = 60000 }).ConfigureAwait(false); }
                catch { await page.WaitForURLAsync("**/profile/posts**", new() { Timeout = 60000 }).ConfigureAwait(false); }
                return true;
            }
            return false;
        }
    }
}
