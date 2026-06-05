using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// WebView2 を使って GitHub の Copilot 機能ページをスクレイピングし、
    /// AI credits の使用状況・次回リセット日を取得するクライアント。
    ///
    /// 動作フロー:
    ///   1. https://github.com/settings/copilot/features に Navigate する
    ///   2. fetch/XHR 傍受で copilot/credits 関連レスポンスを捕捉する
    ///   3. 捕捉失敗時は DOM テキストから "X / Y AI credits" を解析する
    ///   4. フォールバックとして billing/summary も試みる
    ///
    /// 認証:
    ///   %TEMP%\AIUsageOverlay_GitHub_WebView2 にセッションを永続保存。
    ///   Claude とは別フォルダなので両方のセッションが独立している。
    /// </summary>
    public class GitHubWebScraper : IDisposable
    {
        // ────────────────────────────────────────────────────────────────
        // 定数
        // ────────────────────────────────────────────────────────────────

        /// <summary>Copilot 機能ページ（"18 / 1,500 AI credits" が掲載される）</summary>
        private const string UsageUrl = "https://github.com/settings/copilot/features";

        /// <summary>Billing サマリーページ（UsageUrl で取得できない場合のフォールバック）</summary>
        private const string BillingUrl = "https://github.com/settings/billing/summary";

        /// <summary>ログイン URL（LoginWindow で開く）</summary>
        public const string LoginUrl = "https://github.com/login";

        /// <summary>Claude とは別フォルダに GitHub セッションを保存する</summary>
        private const string UserDataFolderName = "AIUsageOverlay_GitHub_WebView2";

        /// <summary>
        /// copilot または AI credits を含むレスポンスを捕捉する fetch/XHR 傍受スクリプト。
        /// window.__ghCopilotRaw に保存する。
        /// </summary>
        private const string InterceptorScript = @"
(function() {
    if (window.__ghInterceptorInstalled) return;
    window.__ghInterceptorInstalled = true;
    window.__ghCopilotRaw = null;

    // copilot または AI credits を含むレスポンスのみ捕捉する
    function tryCapture(url, text) {
        if (window.__ghCopilotRaw) return;
        try {
            const t = (text || '').toLowerCase();
            if ((t.includes('copilot') || t.includes('ai_credit') || t.includes('ai credit'))
                && text && text.length > 10) {
                window.__ghCopilotRaw = text;
            }
        } catch {}
    }

    const _origFetch = window.fetch;
    window.fetch = async function(...args) {
        const response = await _origFetch.apply(this, args);
        try {
            const url = typeof args[0] === 'string' ? args[0]
                      : (args[0] instanceof Request ? args[0].url : '');
            response.clone().text().then(t => tryCapture(url, t)).catch(() => {});
        } catch {}
        return response;
    };

    const _origOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url) {
        this._ghUrl = url || '';
        return _origOpen.apply(this, arguments);
    };
    const _origSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.send = function() {
        this.addEventListener('load', function() {
            try { tryCapture(this._ghUrl, this.responseText); } catch {}
        });
        return _origSend.apply(this, arguments);
    };
})();
";

        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        private Window? _hostWindow;
        private WebView2? _webView;
        private CoreWebView2Environment? _env;
        private bool _initialized;

        // ────────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>直前の呼び出しで発生したエラーの説明。成功時は null。</summary>
        public string? LastError { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// GitHub Copilot の使用状況をスクレイピングして返す。
        /// 未ログインやタイムアウトの場合は null を返す。
        /// </summary>
        public async Task<GitHubCopilotData?> FetchCopilotDataAsync()
        {
            LastError = null;
            try
            {
                await EnsureInitializedAsync();
                if (_webView?.CoreWebView2 == null)
                {
                    LastError = "WebView2初期化失敗";
                    return null;
                }

                // UsageUrl → BillingUrl の順で試みる
                var raw = await NavigateAndCaptureAsync();
                if (raw == null)
                {
                    LastError = "未ログイン（右クリック→GitHubログインしてください）";
                    return null;
                }

                var result = ParseCopilotData(raw);

                // JSON パース失敗時は DOM テキストへフォールバック
                if (result == null && !raw.StartsWith("__PAGETEXT__:"))
                {
                    var pageText = await ReadPageTextAsync();
                    if (pageText != null)
                        result = ParseCopilotData(pageText);
                }

                if (result == null)
                    LastError = "Copilot情報が取得できませんでした";

                return result;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// GitHub ログイン用の LoginWindow を表示する。
        /// GitHub 専用 Environment を共有するため Cookie が同期される。
        /// </summary>
        public async Task ShowLoginWindowAsync()
        {
            await EnsureInitializedAsync();
            if (_env == null) return;
            var loginWindow = new LoginWindow(_env, LoginUrl);
            loginWindow.Show();
        }

        public void Dispose()
        {
            _hostWindow?.Close();
            _webView = null;
            _hostWindow = null;
        }

        // ────────────────────────────────────────────────────────────────
        // 初期化
        // ────────────────────────────────────────────────────────────────

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;

            var userDataFolder = Path.Combine(Path.GetTempPath(), UserDataFolderName);
            _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            _hostWindow = new Window
            {
                Width = 1, Height = 1, Left = -9999, Top = -9999,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                AllowsTransparency = true, Opacity = 0,
                Title = "GitHubScraperHost"
            };
            _webView = new WebView2();
            _hostWindow.Content = _webView;
            _hostWindow.Show();

            await _webView.EnsureCoreWebView2Async(_env);
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(InterceptorScript);
            _initialized = true;
        }

        // ────────────────────────────────────────────────────────────────
        // Navigate & 傍受
        // ────────────────────────────────────────────────────────────────

        /// <summary>UsageUrl → BillingUrl の順に試みる</summary>
        private async Task<string?> NavigateAndCaptureAsync()
        {
            var result = await NavigateOnceAsync(UsageUrl);
            if (result != null) return result;
            return await NavigateOnceAsync(BillingUrl);
        }

        /// <summary>指定 URL に遷移し、傍受データまたは DOM テキストを返す</summary>
        private async Task<string?> NavigateOnceAsync(string url)
        {
            if (_webView?.CoreWebView2 == null) return null;

            // 傍受バッファをリセット
            await _webView.CoreWebView2.ExecuteScriptAsync("window.__ghCopilotRaw = null;");

            var navTcs = new TaskCompletionSource<bool>();
            void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNav;
                navTcs.SetResult(e.IsSuccess);
            }
            _webView.CoreWebView2.NavigationCompleted += OnNav;
            _webView.CoreWebView2.Navigate(url);

            var navDone = await Task.WhenAny(navTcs.Task, Task.Delay(20_000));
            if (navDone != navTcs.Task || !navTcs.Task.Result)
                return null;

            var currentUrl = _webView.CoreWebView2.Source ?? "";
            if (currentUrl.Contains("/login") || currentUrl.Contains("/sessions/new"))
                return null;

            // fetch/XHR 傍受データをポーリング（300ms × 33 回 ≒ 10 秒）
            for (int i = 0; i < 33; i++)
            {
                await Task.Delay(300);
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__ghCopilotRaw ?? null");
                if (encoded != "null")
                    return JsonSerializer.Deserialize<string>(encoded);
            }

            return await ReadPageTextAsync();
        }

        /// <summary>現在ページの DOM テキストを __PAGETEXT__ プレフィックス付きで返す</summary>
        private async Task<string?> ReadPageTextAsync()
        {
            if (_webView?.CoreWebView2 == null) return null;
            try
            {
                var enc = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "document.body ? document.body.innerText : ''");
                var text = JsonSerializer.Deserialize<string>(enc) ?? "";
                return text.Length > 100 ? $"__PAGETEXT__:{text}" : null;
            }
            catch { return null; }
        }

        // ────────────────────────────────────────────────────────────────
        // パース
        // ────────────────────────────────────────────────────────────────

        private static GitHubCopilotData? ParseCopilotData(string raw)
        {
            if (raw.StartsWith("__PAGETEXT__:"))
                return ParseFromPageText(raw["__PAGETEXT__:".Length..]);
            return ParseFromJson(raw);
        }

        /// <summary>傍受した JSON から Copilot データを解析する</summary>
        private static GitHubCopilotData? ParseFromJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var jsonLower = json.ToLowerInvariant();
                if (!jsonLower.Contains("copilot") && !jsonLower.Contains("credit"))
                    return null;

                DateTimeOffset? nextBilling = null;
                bool isActive    = false;
                int creditsUsed  = -1, creditsTotal = -1;

                FindCopilotFields(doc.RootElement,
                    ref nextBilling, ref isActive,
                    ref creditsUsed, ref creditsTotal);

                if (nextBilling.HasValue || isActive || creditsUsed >= 0)
                {
                    return new GitHubCopilotData
                    {
                        IsConnected      = true,
                        IsActive         = isActive || nextBilling.HasValue || creditsUsed >= 0,
                        CreditsUsed      = creditsUsed,
                        CreditsTotal     = creditsTotal,
                        HasUsageData     = creditsUsed >= 0 && creditsTotal > 0,
                        NextBillingDate  = nextBilling,
                        DaysUntilRenewal = nextBilling.HasValue
                            ? Math.Max(0, (int)(nextBilling.Value - DateTimeOffset.Now).TotalDays)
                            : -1
                    };
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// JSON ツリーを再帰探索して next_billing_date・status・AI credits を取得する
        /// </summary>
        private static void FindCopilotFields(
            JsonElement el,
            ref DateTimeOffset? nextBilling,
            ref bool isActive,
            ref int creditsUsed,
            ref int creditsTotal)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();

                    // 次回リセット日
                    if ((key.Contains("next") && key.Contains("bill"))
                        || key is "next_billing_date" or "renewal_date" or "renews_at" or "reset_at" or "resets_at")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String
                            && DateTimeOffset.TryParse(prop.Value.GetString(), out var dt))
                            nextBilling = dt;
                    }

                    // ステータス
                    if (key is "status" or "state" or "subscription_status")
                    {
                        var val = (prop.Value.GetString() ?? "").ToLowerInvariant();
                        if (val is "active" or "enabled" or "paid")
                            isActive = true;
                    }

                    // AI credits 使用数・上限
                    if (key.Contains("credit") || key.Contains("quota") || key.Contains("allowance"))
                    {
                        if (key.Contains("used") || key.Contains("consumed") || key.Contains("spent"))
                            TryGetInt(prop.Value, ref creditsUsed);
                        else if (key.Contains("total") || key.Contains("limit")
                              || key.Contains("included") || key.Contains("max") || key.Contains("allowance"))
                            TryGetInt(prop.Value, ref creditsTotal);
                    }

                    FindCopilotFields(prop.Value, ref nextBilling, ref isActive,
                        ref creditsUsed, ref creditsTotal);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    FindCopilotFields(item, ref nextBilling, ref isActive,
                        ref creditsUsed, ref creditsTotal);
            }
        }

        private static void TryGetInt(JsonElement el, ref int target)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) && v >= 0)
                target = v;
        }

        /// <summary>
        /// DOM テキストから Copilot 情報を解析する。
        /// "18 / 1,500 AI credits" や "Resets in 26 days on Jul 1, 2026" を捕捉する。
        /// </summary>
        private static GitHubCopilotData? ParseFromPageText(string pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText)) return null;

            var ltext = pageText.ToLowerInvariant();
            if (!ltext.Contains("copilot") && !ltext.Contains("credit"))
                return null;

            // Active 判定: キャンセル文言がなければアクティブとみなす
            bool isActive = !ltext.Contains("cancel")
                         && !ltext.Contains("キャンセル")
                         && !ltext.Contains("inactive")
                         && !ltext.Contains("expired")
                         && !ltext.Contains("無効");

            // AI credits の使用量を抽出: "18 / 1,500 AI credits"
            var (creditsUsed, creditsTotal) = ExtractUsagePair(pageText, "credit");

            // 次回リセット日を抽出: "Resets in 26 days on Jul 1, 2026"
            DateTimeOffset? nextBilling = ExtractNextBillingDate(pageText);

            return new GitHubCopilotData
            {
                IsConnected      = true,
                IsActive         = isActive,
                CreditsUsed      = creditsUsed,
                CreditsTotal     = creditsTotal,
                HasUsageData     = creditsUsed >= 0 && creditsTotal > 0,
                NextBillingDate  = nextBilling,
                DaysUntilRenewal = nextBilling.HasValue
                    ? Math.Max(0, (int)(nextBilling.Value - DateTimeOffset.Now).TotalDays)
                    : -1
            };
        }

        /// <summary>
        /// ページテキストから次回リセット日を抽出する。
        /// 検索起点: "resets" / "next billing" / "次の請求"
        /// </summary>
        private static DateTimeOffset? ExtractNextBillingDate(string text)
        {
            var idx = text.IndexOf("resets", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = text.IndexOf("next billing", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = text.IndexOf("次の請求", StringComparison.OrdinalIgnoreCase);

            var searchText = idx >= 0
                ? text.Substring(idx, Math.Min(200, text.Length - idx))
                : text;

            // 英語フルネーム月: "August 1, 2026" / "Jul 1, 2026"
            var m1 = Regex.Match(searchText,
                @"(January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2},?\s+\d{4}",
                RegexOptions.IgnoreCase);
            if (m1.Success && DateTimeOffset.TryParse(m1.Value, out var dt1))
                return dt1;

            // ISO 形式: "2026-08-01"
            var m2 = Regex.Match(searchText, @"\d{4}-\d{2}-\d{2}");
            if (m2.Success && DateTimeOffset.TryParse(m2.Value, out var dt2))
                return dt2;

            return null;
        }

        /// <summary>
        /// ページテキストから使用量ペア（used, total）を抽出する。
        /// 対応パターン: "18 / 1,500 AI credits" / "150 of 300 credits"
        /// </summary>
        private static (int used, int total) ExtractUsagePair(string text, string keyword)
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return (-1, -1);

            var start = Math.Max(0, idx - 50);
            var chunk = text.Substring(start, Math.Min(300, text.Length - start));

            // "18 / 1,500" or "18 of 1,500"
            var m = Regex.Match(chunk, @"([\d,]+)\s*(?:of|\/)\s*([\d,]+)", RegexOptions.IgnoreCase);
            if (m.Success
                && TryParseNumber(m.Groups[1].Value, out var u)
                && TryParseNumber(m.Groups[2].Value, out var t))
                return (u, t);

            return (-1, -1);
        }

        private static bool TryParseNumber(string s, out int result)
            => int.TryParse(s.Replace(",", ""), out result);
    }
}
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        