using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// WebView2（実際の Chromium エンジン）を使って claude.ai の Usage API を呼び出すクライアント。
    ///
    /// 動作フロー:
    ///   1. 不可視の WPF ウィンドウに WebView2 を初期化する（初回のみ）
    ///   2. https://claude.ai/settings/usage に Navigate する
    ///   3. ページが内部で呼び出す /api/organizations/{id}/usage のレスポンスを傍受する
    ///   4. JSON をパースして ScrapedUsageData を返す
    ///
    /// 認証について:
    ///   WebView2 は %TEMP%\AIUsageOverlay_WebView2 に永続セッションを保存するため、
    ///   初回ログイン後は Cookie の手動入力が不要になる。
    ///   未ログイン時は ShowLoginWindowAsync() でブラウザを表示してユーザーにログインさせる。
    /// </summary>
    public class ClaudeApiClient : IDisposable
    {
        // ────────────────────────────────────────────────────────────────
        // 定数
        // ────────────────────────────────────────────────────────────────

        /// <summary>データ取得のトリガーとなる設定ページ URL</summary>
        private const string SettingsUsageUrl = "https://claude.ai/settings/usage";

        /// <summary>WebView2 のユーザーデータフォルダ名（%TEMP% 以下に永続保存）</summary>
        private const string UserDataFolderName = "AIUsageOverlay_WebView2";

        /// <summary>
        /// ページ生成時に注入する fetch() 傍受スクリプト。
        /// /organizations/{id}/usage を含む URL への fetch レスポンスを
        /// window.__claudeUsageData に保存する。
        /// </summary>
        private const string InterceptorScript = @"
(function() {
    if (window.__claudeInterceptorInstalled) return;
    window.__claudeInterceptorInstalled = true;
    window.__claudeUsageData = null;

    const _orig = window.fetch;
    window.fetch = async function(...args) {
        const response = await _orig.apply(this, args);
        try {
            const url = (typeof args[0] === 'string') ? args[0]
                      : (args[0] instanceof Request ? args[0].url : '');
            if (url.includes('/organizations/') && url.includes('/usage')) {
                response.clone().text().then(t => {
                    window.__claudeUsageData = t;
                }).catch(() => {});
            }
        } catch {}
        return response;
    };
})();
";

        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>WebView2 コントロールを保持する不可視の WPF ウィンドウ</summary>
        private Window? _hostWindow;

        /// <summary>API 取得に使う WebView2 コントロール</summary>
        private WebView2? _webView;

        /// <summary>
        /// WebView2 の共有 Environment（LoginWindow と同じユーザーデータフォルダを参照）。
        /// LoginWindow に渡すことで Cookie を共有し、ログイン後に自動認証される。
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
        // JSON デシリアライズ用モデル
        // ────────────────────────────────────────────────────────────────

        /// <summary>API レスポンスのルートオブジェクト</summary>
        private sealed class UsageResponse
        {
            [JsonPropertyName("five_hour")]
            public UsagePeriod? FiveHour { get; set; }

            [JsonPropertyName("seven_day")]
            public UsagePeriod? SevenDay { get; set; }
        }

        /// <summary>各制限期間（5時間 / 7日）の使用量データ</summary>
        private sealed class UsagePeriod
        {
            /// <summary>使用率（0.0 ～ 100.0 %）</summary>
            [JsonPropertyName("utilization")]
            public double Utilization { get; set; }

            /// <summary>
            /// リセット日時（ISO 8601 / UTC オフセット付き）。
            /// 使用率が 0% の場合など、未使用のときは API が null を返すため nullable にする。
            /// </summary>
            [JsonPropertyName("resets_at")]
            public DateTimeOffset? ResetsAt { get; set; }
        }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// claude.ai/settings/usage に Navigate し、ページが内部で呼び出す
        /// Usage API のレスポンスを傍受して ScrapedUsageData を返す。
        /// 未ログイン等でタイムアウトした場合は null を返す（LastError に理由が入る）。
        /// </summary>
        public async Task<ScrapedUsageData?> FetchUsageAsync()
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

                var json = await NavigateAndInterceptUsageAsync();
                if (json == null)
                {
                    LastError = "未ログイン（右クリック→ログインしてください）";
                    return null;
                }

                var result = ParseUsage(json);
                if (result == null)
                    LastError = $"ParseError: {json[..Math.Min(100, json.Length)]}";

                return result;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// ログイン用の LoginWindow を表示する。
        /// ホストウィンドウとは独立した通常ウィンドウで開くため、
        /// AllowsTransparency の制約を受けない。
        /// 同じ CoreWebView2Environment を共有するため、ここでログインした
        /// Cookie がバックグラウンドの WebView2 にも引き継がれる。
        /// </summary>
        public async Task ShowLoginWindowAsync()
        {
            await EnsureInitializedAsync();
            if (_env == null) return;

            // 別ウィンドウで開くことで AllowsTransparency の問題を回避する
            var loginWindow = new LoginWindow(_env, "https://claude.ai/", "Claude");
            loginWindow.Show();
        }

        /// <summary>リソースを解放する。ホストウィンドウを閉じて WebView2 を破棄する。</summary>
        public void Dispose()
        {
            _hostWindow?.Close();
            _webView = null;
            _hostWindow = null;
        }

        // ────────────────────────────────────────────────────────────────
        // 初期化
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// WebView2 を初期化する（初回呼び出し時のみ実行）。
        /// 永続ユーザーデータフォルダにセッションを保存するため、
        /// 一度ログインすれば再起動後も認証が維持される。
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;

            var userDataFolder = Path.Combine(Path.GetTempPath(), UserDataFolderName);
            // LoginWindow と同じ Environment を共有することで Cookie を同期する
            _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            var env = _env;

            // 通常は画面外の不可視ウィンドウとして作成する（ログイン時のみ可視化）
            _hostWindow = new Window
            {
                Width  = 1,
                Height = 1,
                Left   = -9999,
                Top    = -9999,
                ShowInTaskbar      = false,
                WindowStyle        = WindowStyle.None,
                AllowsTransparency = true,
                Opacity            = 0,
                Title              = "ClaudeApiClientHost"
            };

            _webView = new WebView2();
            _hostWindow.Content = _webView;
            _hostWindow.Show();

            await _webView.EnsureCoreWebView2Async(env);

            // 不要な UI 機能を無効化する
            _webView.CoreWebView2.Settings.IsStatusBarEnabled           = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled           = false;

            // Google OAuth がWebView2をブロックしないよう通常のChrome UAを設定する
            _webView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/130.0.0.0 Safari/537.36";

            // fetch() 傍受スクリプトをすべてのページ生成前に注入する（一度だけ登録）
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(InterceptorScript);

            _initialized = true;
        }

        // ────────────────────────────────────────────────────────────────
        // Navigate & 傍受
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// settings/usage ページに Navigate し、傍受した Usage API のレスポンス JSON を返す。
        /// ページ読み込み後、最大 15 秒間 window.__claudeUsageData をポーリングする。
        /// タイムアウト（未ログイン等）の場合は null を返す。
        /// </summary>
        private async Task<string?> NavigateAndInterceptUsageAsync()
        {
            if (_webView?.CoreWebView2 == null) return null;

            // ナビゲーション完了を待機する
            var navTcs = new TaskCompletionSource<bool>();
            void OnNavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                navTcs.SetResult(e.IsSuccess);
            }
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.Navigate(SettingsUsageUrl);

            // ナビゲーション完了を最大 20 秒待つ
            var navWinner = await Task.WhenAny(navTcs.Task, Task.Delay(20_000));
            if (navWinner != navTcs.Task || !navTcs.Task.Result)
                return null;

            // ナビゲーション完了後、ページの JS が fetch を呼ぶまで最大 15 秒ポーリングする
            // （ポーリング間隔: 300ms × 最大 50 回 = 15 秒）
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(300);

                // 傍受スクリプトが保存した JSON を読み取る
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__claudeUsageData ?? null");

                if (encoded != "null")
                    return JsonSerializer.Deserialize<string>(encoded);
            }

            return null;
        }

        // ────────────────────────────────────────────────────────────────
        // JSON パース
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// JSON テキストから ScrapedUsageData を生成する。
        /// five_hour / seven_day のいずれかが欠損している場合は null を返す。
        ///
        /// resets_at が null のケース（使用率 0% で未使用のとき API が null を返す）:
        ///   - セッション残り時間 → 5時間（300分）をそのまま残り時間として扱う
        ///   - 週間残り時間      → 7日（10080分）をそのまま残り時間として扱う
        /// </summary>
        private static ScrapedUsageData? ParseUsage(string json)
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var resp = JsonSerializer.Deserialize<UsageResponse>(json, opts);

                if (resp?.FiveHour == null || resp.SevenDay == null)
                    return null;

                var now = DateTimeOffset.Now;

                // resets_at が null の場合（未使用で制限未到達）は各制限期間をフル残りとして扱う
                int sessionRemainingMinutes;
                if (resp.FiveHour.ResetsAt.HasValue)
                {
                    // リセット日時が分かっている場合は差分を計算する
                    var sessionRemaining = resp.FiveHour.ResetsAt.Value - now;
                    sessionRemainingMinutes = sessionRemaining.TotalMinutes > 0
                                             ? (int)sessionRemaining.TotalMinutes : 0;
                }
                else
                {
                    // null = まだリセット不要（使用率 0%）→ 5時間フル残り
                    sessionRemainingMinutes = 5 * 60;
                }

                int weeklyRemainingMinutes;
                if (resp.SevenDay.ResetsAt.HasValue)
                {
                    // リセット日時が分かっている場合は差分を計算する
                    var weeklyRemaining = resp.SevenDay.ResetsAt.Value - now;
                    weeklyRemainingMinutes = weeklyRemaining.TotalMinutes > 0
                                            ? (int)weeklyRemaining.TotalMinutes : 0;
                }
                else
                {
                    // null = まだリセット不要（使用率 0%）→ 7日フル残り
                    weeklyRemainingMinutes = 7 * 24 * 60;
                }

                return new ScrapedUsageData
                {
                    SessionPercent          = (int)Math.Round(resp.FiveHour.Utilization),
                    SessionRemainingMinutes = sessionRemainingMinutes,
                    WeeklyPercent           = (int)Math.Round(resp.SevenDay.Utilization),
                    WeeklyRemainingMinutes  = weeklyRemainingMinutes
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
