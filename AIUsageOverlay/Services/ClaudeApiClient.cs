using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.Threading;
using System.Windows;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.Parsing;

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

        /// <summary>
        /// 初期化処理の多重実行を防ぐ排他制御。
        /// 起動直後の自動更新とユーザーのログイン操作が同時に走った場合でも、
        /// 同じユーザーデータフォルダを使う WebView2 Environment を一度だけ作る。
        /// </summary>
        private readonly SemaphoreSlim _initializationGate = new(1, 1);

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

                var result = ClaudeUsageParser.Parse(json);
                if (result == null)
                    LastError = $"ParseError: {json[..Math.Min(100, json.Length)]}";

                return result;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
            finally
            {
                // 取得完了後（成功・失敗いずれも）にアイドル時メモリを抑制する
                await ReleaseMemoryAsync();
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
            ResetWebViewState();
            _initializationGate.Dispose();
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
            if (IsInitialized()) return;

            await _initializationGate.WaitAsync();
            try
            {
                if (IsInitialized()) return;

                ResetWebViewState();

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
            catch
            {
                ResetWebViewState();
                throw;
            }
            finally
            {
                _initializationGate.Release();
            }
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

            // 前回 Low に下げたメモリ目標を通常へ戻してから取得する
            ResumeMemory();

            // SPA 遷移や前回失敗時に古い傍受結果が残っていても誤読しないよう初期化する
            await _webView.CoreWebView2.ExecuteScriptAsync("window.__claudeUsageData = null;");

            // ナビゲーション完了を待機する
            var navTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                navTcs.TrySetResult(e.IsSuccess);
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
        // メモリ最適化
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 取得完了後に呼び出し、常駐 WebView2 のメモリ使用量を抑える。
        /// 重いページ DOM/JS を解放するため about:blank へ遷移し、メモリ目標レベルを
        /// Low に下げる（次回取得時に <see cref="ResumeMemory"/> で通常へ復帰）。
        /// メモリ最適化は失敗しても機能に影響しないため例外は無視する。
        /// </summary>
        private async Task ReleaseMemoryAsync()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return;

                // 重いページを破棄して常駐メモリを削減する
                var navTcs = new TaskCompletionSource<bool>();
                void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    _webView.CoreWebView2.NavigationCompleted -= OnNav;
                    navTcs.TrySetResult(true);
                }
                _webView.CoreWebView2.NavigationCompleted += OnNav;
                _webView.CoreWebView2.Navigate("about:blank");
                await Task.WhenAny(navTcs.Task, Task.Delay(3_000));

                // アイドル時のメモリ目標を下げる
                _webView.CoreWebView2.MemoryUsageTargetLevel =
                    CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch { /* メモリ最適化失敗は無視（機能には影響しない） */ }
        }

        /// <summary>
        /// 取得開始時に呼び出し、Low に下げていたメモリ目標を Normal へ戻す。
        /// ページ描画・スクリプト実行を通常速度で行えるようにする。
        /// </summary>
        private void ResumeMemory()
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.MemoryUsageTargetLevel =
                        CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { /* 同上 */ }
        }

        /// <summary>
        /// WebView2 が利用可能な状態まで初期化済みかを判定する。
        /// _initialized だけで判断すると、起動直後の初期化失敗後に壊れた参照を再利用する恐れがある。
        /// </summary>
        private bool IsInitialized()
            => _initialized && _env != null && _webView?.CoreWebView2 != null;

        /// <summary>
        /// 初期化途中または利用済みの WebView2 関連オブジェクトを破棄し、次回呼び出しで再作成できる状態へ戻す。
        /// WebView2 初期化失敗はアプリ起動直後に起きやすいため、失敗状態を固定しないことが重要。
        /// </summary>
        private void ResetWebViewState()
        {
            try
            {
                _hostWindow?.Close();
            }
            catch { /* 終了時・初期化失敗時の破棄失敗は次回再作成で回復する */ }

            _webView = null;
            _hostWindow = null;
            _env = null;
            _initialized = false;
        }
    }
}
