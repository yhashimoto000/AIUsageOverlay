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
    /// WebView2（実際の Chromium エンジン）を使って GitHub の Billing ページを
    /// スクレイピングし、GitHub Copilot の使用状況を取得するクライアント。
    ///
    /// 動作フロー:
    ///   1. 不可視の WPF ウィンドウに WebView2 を初期化する（初回のみ）
    ///   2. https://github.com/settings/billing/summary に Navigate する
    ///   3. ページが呼び出す内部 API のレスポンスを fetch/XHR 傍受で取得する
    ///   4. 傍受失敗時は DOM テキストからのフォールバック解析を行う
    ///   5. GitHub Copilot の状態・次回更新日を返す
    ///
    /// 認証について:
    ///   WebView2 は %TEMP%\AIUsageOverlay_GitHub_WebView2 に永続セッションを保存するため、
    ///   初回ログイン後は再ログイン不要。Claude とは別のユーザーデータフォルダを使用する。
    /// </summary>
    public class GitHubWebScraper : IDisposable
    {
        // ────────────────────────────────────────────────────────────────
        // 定数
        // ────────────────────────────────────────────────────────────────

        /// <summary>Billing ページ URL（GitHub Copilot の購読情報が掲載される）</summary>
        private const string BillingUrl = "https://github.com/settings/billing/summary";

        /// <summary>GitHub ログイン URL（LoginWindow で開く）</summary>
        public const string LoginUrl = "https://github.com/login";

        /// <summary>Claude とは別に GitHub セッションを永続保存するフォルダ名</summary>
        private const string UserDataFolderName = "AIUsageOverlay_GitHub_WebView2";

        /// <summary>
        /// ページ生成時に注入する fetch/XHR 傍受スクリプト。
        /// billing・copilot に関連するレスポンスを window.__ghCopilotRaw に保存する。
        /// また埋め込みスクリプトタグや window グローバルも探索する。
        /// </summary>
        private const string InterceptorScript = @"
(function() {
    if (window.__ghInterceptorInstalled) return;
    window.__ghInterceptorInstalled = true;
    window.__ghCopilotRaw = null;

    // billing/copilot に関するレスポンスを捕捉する共通関数
    function tryCapture(url, text) {
        if (window.__ghCopilotRaw) return;   // 既に取得済み
        try {
            const u = (url || '').toLowerCase();
            const t = (text || '').toLowerCase();
            if ((u.includes('billing') || u.includes('copilot') || t.includes('copilot'))
                && text && text.length > 10) {
                window.__ghCopilotRaw = text;
            }
        } catch {}
    }

    // fetch() を傍受する
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

    // XMLHttpRequest を傍受する
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

        /// <summary>WebView2 コントロールを保持する不可視の WPF ウィンドウ</summary>
        private Window? _hostWindow;

        /// <summary>スクレイピングに使う WebView2 コントロール</summary>
        private WebView2? _webView;

        /// <summary>
        /// GitHub セッション用の CoreWebView2Environment。
        /// LoginWindow と共有することで Cookie を同期する。
        /// </summary>
        private CoreWebView2Environment? _env;

        /// <summary>WebView2 の初期化完了フラグ</summary>
        private bool _initialized;

        // ────────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 直前の呼び出しで発生したエラーの説明。成功時は null。
        /// 例: "未ログイン" / "取得タイムアウト" / "ParseError"
        /// </summary>
        public string? LastError { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// GitHub Billing ページをスクレイピングして GitHubCopilotData を返す。
        /// 未ログインやタイムアウトの場合は null を返す（LastError に理由が入る）。
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

                var raw = await NavigateAndCaptureAsync();
                if (raw == null)
                {
                    LastError = "未ログイン（右クリック→GitHubログインしてください）";
                    return null;
                }

                var result = ParseCopilotData(raw);
                if (result == null)
                    LastError = $"ParseError: {raw[..Math.Min(120, raw.Length)]}";

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
        /// GitHub 専用の CoreWebView2Environment を共有するため、
        /// ここでログインした Cookie がスクレイピング用 WebView2 にも引き継がれる。
        /// </summary>
        public async Task ShowLoginWindowAsync()
        {
            await EnsureInitializedAsync();
            if (_env == null) return;

            // LoginWindow を GitHub 用 URL で開く
            var loginWindow = new LoginWindow(_env, LoginUrl);
            loginWindow.Show();
        }

        /// <summary>リソースを解放する。ホストウィンドウを閉じて WebView2 を破棄する。</summary>
        public void Dispose()
        {
            _hostWindow?.Close();
            _webView    = null;
            _hostWindow = null;
        }

        // ────────────────────────────────────────────────────────────────
        // 初期化
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// WebView2 を初期化する（初回呼び出し時のみ実行）。
        /// Claude とは別フォルダにセッションを保存するため、GitHub セッションは独立している。
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;

            // Claude とは別のユーザーデータフォルダを使い、セッションを分離する
            var userDataFolder = Path.Combine(Path.GetTempPath(), UserDataFolderName);
            _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            var env = _env;

            // 不可視ホストウィンドウを作成する
            _hostWindow = new Window
            {
                Width              = 1,
                Height             = 1,
                Left               = -9999,
                Top                = -9999,
                ShowInTaskbar      = false,
                WindowStyle        = WindowStyle.None,
                AllowsTransparency = true,
                Opacity            = 0,
                Title              = "GitHubScraperHost"
            };

            _webView = new WebView2();
            _hostWindow.Content = _webView;
            _hostWindow.Show();

            await _webView.EnsureCoreWebView2Async(env);

            // 不要な UI 機能を無効化する
            _webView.CoreWebView2.Settings.IsStatusBarEnabled            = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled            = false;

            // fetch/XHR 傍受スクリプトをすべてのページ生成前に注入する
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(InterceptorScript);

            _initialized = true;
        }

        // ────────────────────────────────────────────────────────────────
        // Navigate & 傍受
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Billing ページに Navigate し、傍受データまたは DOM テキストを返す。
        ///
        /// 手順:
        ///   1. Billing ページへ遷移し、ナビゲーション完了を待つ
        ///   2. ページ内の fetch/XHR 傍受を最大 10 秒ポーリングする
        ///   3. 傍受データが得られなければ DOM テキストをフォールバックとして返す
        ///   4. ログインページ（/login）にリダイレクトされていたら null を返す
        /// </summary>
        private async Task<string?> NavigateAndCaptureAsync()
        {
            if (_webView?.CoreWebView2 == null) return null;

            // ナビゲーション完了を待つ
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

            // ログインページにリダイレクトされていないか確認する
            var currentUrl = _webView.CoreWebView2.Source ?? "";
            if (currentUrl.Contains("/login") || currentUrl.Contains("/sessions/new"))
                return null;

            // fetch/XHR 傍受データをポーリングする（300ms × 最大 33 回 ≒ 10 秒）
            for (int i = 0; i < 33; i++)
            {
                await Task.Delay(300);
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__ghCopilotRaw ?? null");
                if (encoded != "null")
                    return JsonSerializer.Deserialize<string>(encoded);
            }

            // 傍受できなかった場合は DOM テキストを返す（フォールバック）
            var pageTextEncoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                "document.body ? document.body.innerText : ''");
            var pageText = JsonSerializer.Deserialize<string>(pageTextEncoded) ?? "";

            // GitHub にログインしているが Copilot 情報がない場合は認証失敗として扱わない
            return pageText.Length > 100 ? $"__PAGETEXT__:{pageText}" : null;
        }

        // ────────────────────────────────────────────────────────────────
        // パース
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 傍受データまたは DOM テキストから GitHubCopilotData を生成する。
        ///
        /// 処理の優先順位:
        ///   1. 傍受 JSON: "copilot" キーを含む JSON を解析して状態と次回更新日を取得する
        ///   2. DOM テキスト（__PAGETEXT__ プレフィックス付き）:
        ///      "GitHub Copilot" セクションのテキストからステータスと日付をパースする
        ///
        /// いずれも失敗した場合は null を返す。
        /// </summary>
        private static GitHubCopilotData? ParseCopilotData(string raw)
        {
            if (raw.StartsWith("__PAGETEXT__:"))
                return ParseFromPageText(raw["__PAGETEXT__:".Length..]);

            return ParseFromJson(raw);
        }

        /// <summary>
        /// 傍受した JSON レスポンスから Copilot データを解析する。
        /// GitHub 内部 API のレスポンス形式に対応した柔軟なパーサー。
        /// </summary>
        private static GitHubCopilotData? ParseFromJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // JSON 全体を文字列化して Copilot 関連キーを探す
                var jsonLower = json.ToLowerInvariant();
                if (!jsonLower.Contains("copilot"))
                    return null;

                // 再帰的にノードを探索して次回請求日と状態を取得する
                DateTimeOffset? nextBilling = null;
                bool isActive = false;

                FindCopilotFields(root, ref nextBilling, ref isActive);

                // 解析できた場合はデータを返す
                if (nextBilling.HasValue || isActive)
                {
                    return new GitHubCopilotData
                    {
                        IsConnected       = true,
                        IsActive          = isActive,
                        NextBillingDate   = nextBilling,
                        DaysUntilRenewal  = nextBilling.HasValue
                            ? Math.Max(0, (int)(nextBilling.Value - DateTimeOffset.Now).TotalDays)
                            : -1
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// JSON 要素ツリーを再帰的に探索して next_billing_date と status を取得する。
        /// GitHub の内部 API レスポンスは構造が変わる可能性があるため、
        /// キー名で汎用的にマッチさせる。
        /// </summary>
        private static void FindCopilotFields(
            JsonElement el,
            ref DateTimeOffset? nextBilling,
            ref bool isActive)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();

                    // 次回請求日のキー候補
                    if ((key.Contains("next") && key.Contains("bill"))
                        || key is "next_billing_date" or "renewal_date" or "renews_at")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String
                            && DateTimeOffset.TryParse(prop.Value.GetString(), out var dt))
                            nextBilling = dt;
                    }

                    // ステータスのキー候補
                    if (key is "status" or "state" or "subscription_status")
                    {
                        var val = (prop.Value.GetString() ?? "").ToLowerInvariant();
                        if (val is "active" or "enabled" or "paid")
                            isActive = true;
                    }

                    // 再帰探索
                    FindCopilotFields(prop.Value, ref nextBilling, ref isActive);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    FindCopilotFields(item, ref nextBilling, ref isActive);
            }
        }

        /// <summary>
        /// DOM テキスト（page.innerText）から Copilot 情報を解析する。
        ///
        /// GitHub の Billing ページテキストには以下のような記述が含まれる（英語 UI）:
        ///   "GitHub Copilot"
        ///   "Individual"
        ///   "Active"
        ///   "Next billing date  August 1, 2026"
        ///
        /// 日本語 UI の場合は "アクティブ" や "次の請求日" などが含まれる可能性がある。
        /// </summary>
        private static GitHubCopilotData? ParseFromPageText(string pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText))
                return null;

            var ltext = pageText.ToLowerInvariant();

            // GitHub Copilot の記載がなければ null
            if (!ltext.Contains("copilot"))
                return null;

            // ステータス判定: "active" / "アクティブ" が含まれるかどうか
            bool isActive = ltext.Contains("active") || ltext.Contains("アクティブ");

            // 次回請求日の抽出: 英語日付パターン（例: "August 1, 2026" / "Aug 1, 2026"）
            DateTimeOffset? nextBilling = ExtractNextBillingDate(pageText);

            return new GitHubCopilotData
            {
                IsConnected      = true,
                IsActive         = isActive,
                NextBillingDate  = nextBilling,
                DaysUntilRenewal = nextBilling.HasValue
                    ? Math.Max(0, (int)(nextBilling.Value - DateTimeOffset.Now).TotalDays)
                    : -1
            };
        }

        /// <summary>
        /// テキストから "Next billing date" 近傍の英語日付文字列を抽出して
        /// DateTimeOffset に変換する。
        ///
        /// 対応フォーマット:
        ///   - "August 1, 2026"
        ///   - "Aug 1, 2026"
        ///   - "2026-08-01"
        /// </summary>
        private static DateTimeOffset? ExtractNextBillingDate(string text)
        {
            // "next billing" / "次の請求" 付近の 200 文字を探索対象にする
            var idx = text.IndexOf("next billing", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                idx = text.IndexOf("次の請求", StringComparison.OrdinalIgnoreCase);

            var searchText = idx >= 0
                ? text.Substring(idx, Math.Min(200, text.Length - idx))
                : text;

            // 英語フルネーム月: "August 1, 2026"
            var fullMonth = Regex.Match(searchText,
                @"(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}",
                RegexOptions.IgnoreCase);
            if (fullMonth.Success && DateTimeOffset.TryParse(fullMonth.Value, out var dt1))
                return dt1;

            // 英語省略月: "Aug 1, 2026"
            var shortMonth = Regex.Match(searchText,
                @"(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2},?\s+\d{4}",
                RegexOptions.IgnoreCase);
            if (shortMonth.Success && DateTimeOffset.TryParse(shortMonth.Value, out var dt2))
                return dt2;

            // ISO 形式: "2026-08-01"
            var iso = Regex.Match(searchText, @"\d{4}-\d{2}-\d{2}");
            if (iso.Success && DateTimeOffset.TryParse(iso.Value, out var dt3))
                return dt3;

            return null;
        }
    }
}
