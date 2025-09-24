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
            if (!Regex.IsMatch(adId ?? string.Empty, @"^\d+$")) return false;
            var url = $"https://doska.ykt.ru/{adId}";
            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
                // Признаки существования: есть тело объявления, или статус, или кнопки действий
                var exists = await page.Locator(".d-post, .d-post_head, .d-post_status, a[href*='/post-pay?id=']").CountAsync().ConfigureAwait(false) > 0;
                return exists;
            }
            catch { return false; }
        }

        public async Task<bool> UnpublishAsync(string login, string password, string adId, bool showBrowser, string adTitle = null, CancellationToken cancellationToken = default)
        {
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = _session.Page;
            if (!Regex.IsMatch(adId ?? string.Empty, @"^\d+$")) return false;
            var url = $"https://doska.ykt.ru/{adId}";

            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);

            // If already archived, nothing to unpublish
            var archived = await page.Locator(".d-post_status.d-post_status--red:has-text('Архив')").CountAsync().ConfigureAwait(false) > 0;
            if (archived) return true;

            // Button: data-micromodal-trigger contains ad id
            var trigger = page.Locator($"a[data-micromodal-trigger='js-d-cancel_reason-modal-{adId}']");
            if (await trigger.CountAsync().ConfigureAwait(false) == 0)
            {
                // fallback by partial
                trigger = page.Locator("a[data-micromodal-trigger*='js-d-cancel_reason-modal-']");
            }
            if (await trigger.CountAsync().ConfigureAwait(false) == 0) return false;

            await trigger.First.ClickAsync().ConfigureAwait(false);

            var modalRoot = page.Locator(".micromodal-slide.is-open .modal__container.d-cancel_reason-modal");
            await modalRoot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 }).ConfigureAwait(false);

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

            var confirm = modalRoot.Locator("button.d-cancel_reason-modal-delete");
            await confirm.First.ClickAsync().ConfigureAwait(false);
            await modalRoot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 60000 }).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> RepublishAsync(string login, string password, string adId, bool showBrowser, CancellationToken cancellationToken = default)
        {
            await _auth.LoginAsync(login, password, showBrowser, cancellationToken).ConfigureAwait(false);
            var page = _session.Page;
            if (!Regex.IsMatch(adId ?? string.Empty, @"^\d+$")) return false;
            var url = $"https://doska.ykt.ru/{adId}";

            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);

            // If already published, nothing to do
            var unpubStatus = await page.Locator(".d-post_status.d-post_status--red:has-text('Архив')").CountAsync().ConfigureAwait(false) == 0;
            if (unpubStatus) return true;

            // Click Activate on ad page
            var activate = page.Locator($"a.d-btn.d-btn-green[href='/post-pay?id={adId}']");
            if (await activate.CountAsync().ConfigureAwait(false) == 0)
                activate = page.Locator("a.d-btn.d-btn-green[href*='/post-pay?id=']");
            if (await activate.CountAsync().ConfigureAwait(false) == 0) return false;
            await activate.First.ClickAsync().ConfigureAwait(false);

            // Wait service packs
            await page.WaitForSelectorAsync(".d-sp_tabs", new() { Timeout = 60000 }).ConfigureAwait(false);

            var freeTabLabel = page.Locator(".d-sp_tab.d-sp_tab--default label.d-sp_tab_inner");
            if (await freeTabLabel.CountAsync().ConfigureAwait(false) > 0)
            {
                await freeTabLabel.First.ClickAsync().ConfigureAwait(false);
            }
            try { await page.WaitForSelectorAsync("#content-free.d-servicePacks_content--active", new() { Timeout = 3000 }).ConfigureAwait(false); } catch { }

            var freeRadio = page.Locator("input#free.d-sp_input.d-servicePacks_input");
            if (await freeRadio.CountAsync().ConfigureAwait(false) > 0 && !(await freeRadio.First.IsCheckedAsync().ConfigureAwait(false)))
            {
                await freeRadio.First.CheckAsync().ConfigureAwait(false);
            }

            var submit = page.Locator("button.yui-btn.yui-btn--green.d-post-add-submit.d-servicePacks_submit_btn.free");
            if (await submit.CountAsync().ConfigureAwait(false) == 0)
                submit = page.Locator("button.yui-btn.yui-btn--green");
            await submit.First.ClickAsync().ConfigureAwait(false);

            try { await page.WaitForURLAsync("**/" + adId, new() { Timeout = 60000 }).ConfigureAwait(false); }
            catch { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 60000 }).ConfigureAwait(false); }
            return true;
        }
    }
}
