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
    /// WebView2 を使って platform.openai.com の Billing ページをスクレイピングし、
    /// OpenAI / Codex のクレジット残高・当月使用量を取得するクライアント。
    ///
    /// 動作フロー:
    ///   1. https://platform.openai.com/settings/billing/overview に Navigate する
    ///   2. fetch/XHR 傍受で billing/credit 関連レスポンスを捕捉する
    ///   3. 捕捉失敗時は DOM テキストから "$X.XX" などを解析する
    ///
    /// 認証:
    ///   %TEMP%\AIUsageOverlay_Codex_WebView2 にセッションを永続保存。
    ///   Claude・GitHub とは別フォルダで独立している。
    /// </summary>
    public class CodexWebScraper : IDisposable
    {
        // ────────────────────────────────────────────────────────────────
        // 定数
        // ────────────────────────────────────────────────────────────────

        /// <summary>OpenAI Billing 概要ページ（クレジット残高が掲載される）</summary>
        private const string BillingUrl = "https://platform.openai.com/settings/billing/overview";

        /// <summary>ログイン URL</summary>
        public const string LoginUrl = "https://platform.openai.com/login";

        /// <summary>セッション保存フォルダ名</summary>
        private const string UserDataFolderName = "AIUsageOverlay_Codex_WebView2";

        /// <summary>
        /// billing/credit/usage を含むレスポンスを捕捉する fetch/XHR 傍受スクリプト。
        /// window.__codexRaw に保存する。
        /// </summary>
        private const string InterceptorScript = @"
(function() {
    if (window.__codexInterceptorInstalled) return;
    window.__codexInterceptorInstalled = true;
    window.__codexRaw = null;

    function tryCapture(url, text) {
        if (window.__codexRaw) return;
        try {
            const u = (url || '').toLowerCase();
            const t = (text || '').toLowerCase();
            if ((t.includes('credit') || t.includes('balance') || u.includes('billing') || u.includes('usage'))
                && text && text.length > 10) {
                window.__codexRaw = text;
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
        this._codexUrl = url || '';
        return _origOpen.apply(this, arguments);
    };
    const _origSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.send = function() {
        this.addEventListener('load', function() {
            try { tryCapture(this._codexUrl, this.responseText); } catch {}
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
        /// OpenAI Billing ページをスクレイピングして CodexUsageData を返す。
        /// 未ログインやタイムアウトの場合は null を返す。
        /// </summary>
        public async Task<CodexUsageData?> FetchUsageAsync()
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

                var raw = await NavigateAndCaptureAsync();
                if (raw == null)
                {
                    LastError = "未ログイン（右クリック→OpenAIログインしてください）";
                    return null;
                }

                var result = ParseUsageData(raw);

                // JSON パース失敗時は DOM テキストへフォールバック
                if (result == null && !raw.StartsWith("__PAGETEXT__:"))
                {
                    var pageText = await ReadPageTextAsync();
                    if (pageText != null)
                        result = ParseUsageData(pageText);
                }

                if (result == null)
                    LastError = "クレジット情報が取得できませんでした";

                return result;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }

        /// <summary>OpenAI ログイン用の LoginWindow を表示する</summary>
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
                Title = "CodexScraperHost"
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

        private async Task<string?> NavigateAndCaptureAsync()
        {
            if (_webView?.CoreWebView2 == null) return null;

            await _webView.CoreWebView2.ExecuteScriptAsync("window.__codexRaw = null;");

            var navTcs = new TaskCompletionSource<bool>();
            void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNav;
                navTcs.SetResult(e.IsSuccess);
            }
            _webView.CoreWebView2.NavigationCompleted += OnNav;
            _webView.CoreWebView2.Navigate(BillingUrl);

            var navDone = await Task.WhenAny(navTcs.Task, Task.Delay(20_000));
            if (navDone != navTcs.Task || !navTcs.Task.Result)
                return null;

            // ログインページへのリダイレクト検知
            var currentUrl = _webView.CoreWebView2.Source ?? "";
            if (currentUrl.Contains("/login") || currentUrl.Contains("/auth/"))
                return null;

            // fetch/XHR 傍受データをポーリング（300ms × 33 回 ≒ 10 秒）
            for (int i = 0; i < 33; i++)
            {
                await Task.Delay(300);
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__codexRaw ?? null");
                if (encoded != "null")
                    return JsonSerializer.Deserialize<string>(encoded);
            }

            return await ReadPageTextAsync();
        }

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

        private static CodexUsageData? ParseUsageData(string raw)
        {
            if (raw.StartsWith("__PAGETEXT__:"))
                return ParseFromPageText(raw["__PAGETEXT__:".Length..]);
            return ParseFromJson(raw);
        }

        /// <summary>
        /// 傍受した JSON からクレジット残高・使用量を解析する。
        ///
        /// OpenAI Billing API レスポンスの例:
        ///   { "total_available": 5.23, "total_granted": 10.00 }
        ///   { "total_usage": 3.47 }  ← /dashboard/billing/usage
        /// </summary>
        private static CodexUsageData? ParseFromJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var lj  = json.ToLowerInvariant();

                // billing/credit/usage 関連でなければスキップ
                if (!lj.Contains("credit") && !lj.Contains("balance")
                    && !lj.Contains("usage") && !lj.Contains("grant"))
                    return null;

                decimal balance  = -1m, total   = -1m, monthlyUsage = -1m;
                FindBillingFields(doc.RootElement, ref balance, ref total, ref monthlyUsage);

                if (balance >= 0 || monthlyUsage >= 0)
                {
                    return new CodexUsageData
                    {
                        IsConnected      = true,
                        CreditBalance    = balance,
                        CreditTotal      = total,
                        HasCreditData    = balance >= 0,
                        MonthlyUsageUsd  = monthlyUsage,
                        HasUsageData     = monthlyUsage >= 0
                    };
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// JSON ツリーを再帰探索して残高・使用量フィールドを取得する。
        ///
        /// 対応キー候補:
        ///   残高系: total_available, hard_limit_usd, balance, available_credit
        ///   使用量系: total_usage, amount_due, usage_usd
        /// </summary>
        private static void FindBillingFields(
            JsonElement el,
            ref decimal balance,
            ref decimal total,
            ref decimal monthlyUsage)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();

                    // 残高系
                    if (key is "total_available" or "balance" or "available_credit"
                        or "credits_remaining" or "credit_balance")
                        TryGetDecimal(prop.Value, ref balance);

                    // 上限・付与系
                    if (key is "total_granted" or "hard_limit_usd" or "soft_limit_usd"
                        or "total_credit" or "credit_total")
                        TryGetDecimal(prop.Value, ref total);

                    // 使用量系
                    if (key is "total_usage" or "amount_due" or "usage_usd"
                        or "total_billed" or "current_usage")
                        TryGetDecimal(prop.Value, ref monthlyUsage);

                    FindBillingFields(prop.Value, ref balance, ref total, ref monthlyUsage);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    FindBillingFields(item, ref balance, ref total, ref monthlyUsage);
            }
        }

        private static void TryGetDecimal(JsonElement el, ref decimal target)
        {
            if (target >= 0) return; // 既に取得済み
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var v) && v >= 0)
                target = v;
        }

        /// <summary>
        /// DOM テキストからクレジット残高・使用量を解析する。
        ///
        /// OpenAI Billing ページのテキスト例（英語 UI）:
        ///   "Credit balance  $5.23"
        ///   "Add to credit balance"
        ///   "Usage this month  $3.47"
        ///   "Auto recharge is on"
        /// </summary>
        private static CodexUsageData? ParseFromPageText(string pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText)) return null;

            var ltext = pageText.ToLowerInvariant();

            // OpenAI の billing ページであることを確認
            if (!ltext.Contains("credit") && !ltext.Contains("balance") && !ltext.Contains("usage"))
                return null;

            // "$X.XX" パターンで金額を抽出する
            decimal balance     = ExtractDollarAmount(pageText, "credit balance");
            decimal monthlyUsage = ExtractDollarAmount(pageText, "usage this month");
            if (monthlyUsage < 0)
                monthlyUsage    = ExtractDollarAmount(pageText, "usage");

            if (balance >= 0 || monthlyUsage >= 0)
            {
                return new CodexUsageData
                {
                    IsConnected     = true,
                    CreditBalance   = balance,
                    HasCreditData   = balance >= 0,
                    MonthlyUsageUsd = monthlyUsage,
                    HasUsageData    = monthlyUsage >= 0
                };
            }

            return null;
        }

        /// <summary>
        /// テキストから keyword 近傍の "$X.XX" を抽出して decimal で返す。
        /// 見つからない場合は -1。
        /// </summary>
        private static decimal ExtractDollarAmount(string text, string keyword)
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1m;

            var chunk = text.Substring(idx, Math.Min(150, text.Length - idx));

            // "$5.23" or "5.23 USD"
            var m = Regex.Match(chunk, @"\$\s*([\d,]+\.?\d*)|(\b[\d,]+\.?\d*)\s*USD",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var numStr = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                    .Replace(",", "");
                if (decimal.TryParse(numStr, out var v))
                    return v;
            }
            return -1m;
        }
    }
}
