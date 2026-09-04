using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.Parsing;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// WebView2 を使って cursor.com の使用量ダッシュボードをスクレイピングし、
    /// 月次プラン枠の使用率と請求サイクル終了を取得するクライアント。
    ///
    /// 動作フロー:
    ///   1. https://cursor.com/dashboard/usage に Navigate する
    ///   2. fetch/XHR 傍受で /api/usage-summary 等のレスポンスを捕捉する
    ///   3. 捕捉失敗時は DOM テキストから百分率を解析する
    ///
    /// 認証:
    ///   %TEMP%\AIUsageOverlay_Cursor_WebView2 にセッションを永続保存。
    ///   Claude / GitHub / Codex とは別フォルダで独立している。
    ///   利用データ・認証情報は cursor.com との直接通信以外に送信しない。
    /// </summary>
    public class CursorWebScraper : IDisposable
    {
        /// <summary>使用量ダッシュボード（usage-summary の取得トリガー）</summary>
        private const string UsageUrl = "https://cursor.com/dashboard/usage";

        /// <summary>旧設定ページ。ダッシュボードが取れない場合のフォールバック</summary>
        private const string SettingsUsageUrl = "https://cursor.com/settings/usage";

        /// <summary>ログイン URL（LoginWindow で開く）</summary>
        public const string LoginUrl = "https://cursor.com/login";

        /// <summary>他サービスとは別フォルダに Cursor セッションを保存する</summary>
        private const string UserDataFolderName = "AIUsageOverlay_Cursor_WebView2";

        /// <summary>
        /// usage-summary / period-usage および get-filtered-usage-events を含む
        /// レスポンスを捕捉する fetch/XHR 傍受スクリプト。
        /// window.__cursorRawCandidates に使用量サマリー候補を蓄積する。
        /// window.__cursorTokenEventPages にトークンイベントページを蓄積する。
        /// </summary>
        private const string InterceptorScript = @"
(function() {
    if (window.__cursorInterceptorInstalled) return;
    window.__cursorInterceptorInstalled = true;
    window.__cursorRawCandidates = [];
    window.__cursorTokenEventPages = [];

    function tryCapture(url, text) {
        try {
            if (!text || text.length <= 10) return;
            const u = (url || '').toLowerCase();
            const t = (text || '').toLowerCase();

            // トークンイベントページ（get-filtered-usage-events）
            const isEventUrl = u.includes('get-filtered-usage-events');
            if (isEventUrl) {
                if (!Array.isArray(window.__cursorTokenEventPages)) {
                    window.__cursorTokenEventPages = [];
                }
                if (window.__cursorTokenEventPages.length < 200) {
                    window.__cursorTokenEventPages.push(text);
                }
                return; // 使用量サマリーとは別バッファ
            }

            // 使用量サマリー
            if (!Array.isArray(window.__cursorRawCandidates)) {
                window.__cursorRawCandidates = [];
            }
            if (window.__cursorRawCandidates.length >= 40) return;

            const relevantUrl =
                u.includes('usage-summary') ||
                u.includes('get-current-period-usage') ||
                u.includes('/api/usage');
            const relevantText =
                t.includes('billingcyclestart') ||
                t.includes('individualusage') ||
                t.includes('totalpercentused') ||
                t.includes('autopercentused') ||
                t.includes('apipercentused');

            if (relevantUrl || relevantText) {
                window.__cursorRawCandidates.push(text);
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
        this._cursorUrl = url || '';
        return _origOpen.apply(this, arguments);
    };
    const _origSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.send = function() {
        this.addEventListener('load', function() {
            try { tryCapture(this._cursorUrl, this.responseText); } catch {}
        });
        return _origSend.apply(this, arguments);
    };
})();
";

        private Window? _hostWindow;
        private WebView2? _webView;
        private CoreWebView2Environment? _env;

        /// <summary>
        /// 初期化処理の多重実行を防ぐ排他制御。
        /// 自動更新とログイン操作が同時に走っても、同一プロファイルの WebView2 を一度だけ作成する。
        /// </summary>
        private readonly SemaphoreSlim _initializationGate = new(1, 1);

        private bool _initialized;

        /// <summary>
        /// WebView2 のブラウザプロセス ID。生存確認に使う（未初期化・取得失敗時は 0）。
        /// </summary>
        private int _browserProcessId;

        /// <summary>
        /// WebView2 の作り直しが必要になったか（ブラウザプロセス異常終了・致命的例外を検知した）。
        /// ProcessFailed イベント（UI スレッド）と取得処理の双方から書き込むため volatile にする。
        /// </summary>
        private volatile bool _needsRecreate;

        /// <summary>直前の呼び出しで発生したエラーの説明。成功時は null。</summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// FetchUsageAsync 中に傍受し、ReleaseMemory（about:blank）前に退避した
        /// get-filtered-usage-events ページ JSON。MergeCursorTokenEventsFromCacheAsync が読む。
        /// </summary>
        private List<string> _lastTokenEventPages = [];

        /// <summary>
        /// Cursor の使用状況をスクレイピングして返す。
        /// 未ログインやタイムアウトの場合は null を返す。
        /// </summary>
        public async Task<CursorUsageData?> FetchUsageAsync()
        {
            LastError = null;
            _lastTokenEventPages = [];
            try
            {
                await EnsureInitializedAsync();
                if (_webView?.CoreWebView2 == null)
                {
                    LastError = "WebView2初期化失敗";
                    return null;
                }

                var rawCandidates = await NavigateAndCaptureAsync();
                if (rawCandidates == null)
                {
                    LastError = "未ログイン（右クリック→Cursorログインしてください）";
                    return null;
                }

                // about:blank へ移る前にトークンイベント傍受バッファを退避する
                _lastTokenEventPages = await ReadTokenEventPagesAsync();

                CursorUsageData? result = null;
                foreach (var raw in rawCandidates)
                {
                    result = CursorUsageParser.Parse(raw);
                    if (result != null) break;
                }

                if (result == null)
                    LastError = "Cursor使用量情報が取得できませんでした";

                return result;
            }
            catch (Exception ex)
            {
                if (WebView2Health.IsFatalWebViewError(ex))
                    _needsRecreate = true;

                LastError = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
            finally
            {
                await ReleaseMemoryAsync();
            }
        }

        /// <summary>
        /// 直前の FetchUsageAsync で退避した get-filtered-usage-events ページを
        /// <see cref="CursorTokenRecord"/> のリストとして返す。
        /// </summary>
        /// <returns>傍受できたイベントのリスト（0 件の場合もある）</returns>
        public Task<List<CursorTokenRecord>> FetchTokenEventsFromCacheAsync()
        {
            var result = new List<CursorTokenRecord>();
            foreach (var page in _lastTokenEventPages)
                result.AddRange(CursorUsageParser.ParseUsageEvents(page));
            return Task.FromResult(result);
        }

        /// <summary>
        /// 起動時に当月の get-filtered-usage-events を全ページ取得する。
        /// </summary>
        /// <returns>取得できたイベントの全リスト</returns>
        public Task<List<CursorTokenRecord>> FetchAllCurrentMonthTokenEventsAsync()
        {
            var now = DateTime.UtcNow;
            return FetchMonthTokenEventsAsync(now.Year, now.Month);
        }

        /// <summary>
        /// 指定年月の get-filtered-usage-events を全ページ取得する。
        /// </summary>
        /// <param name="year">西暦年</param>
        /// <param name="month">月（1〜12）</param>
        /// <returns>取得できたイベントの全リスト</returns>
        public async Task<List<CursorTokenRecord>> FetchMonthTokenEventsAsync(int year, int month)
        {
            try
            {
                await EnsureInitializedAsync();
                if (_webView?.CoreWebView2 == null) return [];

                ResumeMemory();
                var navResult = await NavigateOnceAsync(UsageUrl);
                if (navResult == null) return []; // 未ログイン

                var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);
                long startMs = new DateTimeOffset(monthStart).ToUnixTimeMilliseconds();
                long endMs = new DateTimeOffset(monthEnd).ToUnixTimeMilliseconds();

                var intercepted = new List<CursorTokenRecord>();
                int maxPage = 1;
                string? lastRaw = null;

                for (int pg = 1; pg <= maxPage && pg <= 50; pg++)
                {
                    var json = await FetchUsageEventsPageAsync(pg, startMs, endMs);
                    lastRaw = json;
                    if (string.IsNullOrWhiteSpace(json)) break;
                    if (json.Contains("\"__httpStatus\"", StringComparison.Ordinal) ||
                        json.Contains("\"__error\"", StringComparison.Ordinal))
                        break;

                    intercepted.AddRange(CursorUsageParser.ParseUsageEvents(json));
                    maxPage = Math.Max(maxPage, ExtractNumPages(json));
                    await Task.Delay(300);
                }

                WriteTokenFetchDiagnostic(lastRaw, intercepted.Count);
                await ReleaseMemoryAsync();
                return intercepted;
            }
            catch { return []; }
        }

        /// <summary>
        /// JS で get-filtered-usage-events に POST して指定ページを取得する。
        /// async fetch だと WebView2 の ExecuteScriptAsync が Promise を待たない場合があるため、
        /// 同期 XMLHttpRequest で結果文字列を直接返す。
        /// </summary>
        /// <param name="page">1 始まりのページ番号</param>
        /// <param name="startMs">期間開始（Unix ミリ秒）</param>
        /// <param name="endMs">期間終了（Unix ミリ秒）</param>
        private async Task<string> FetchUsageEventsPageAsync(int page, long startMs, long endMs)
        {
            if (_webView?.CoreWebView2 == null) return string.Empty;
            try
            {
                var bodyJson = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["startDate"] = startMs.ToString(),
                    ["endDate"] = endMs.ToString(),
                    ["page"] = page,
                    ["pageSize"] = 100,
                });
                var bodyLiteral = JsonSerializer.Serialize(bodyJson);
                var script = $@"
(() => {{
    try {{
        const xhr = new XMLHttpRequest();
        xhr.open('POST', 'https://cursor.com/api/dashboard/get-filtered-usage-events', false);
        xhr.withCredentials = true;
        xhr.setRequestHeader('Content-Type', 'application/json');
        xhr.send({bodyLiteral});
        if (xhr.status < 200 || xhr.status >= 300) {{
            return JSON.stringify({{ __httpStatus: xhr.status, __body: (xhr.responseText || '').slice(0, 500) }});
        }}
        return xhr.responseText || '';
    }} catch (e) {{
        return JSON.stringify({{ __error: String(e) }});
    }}
}})()";
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                return JsonSerializer.Deserialize<string>(encoded) ?? string.Empty;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { __error = ex.Message });
            }
        }

        /// <summary>
        /// トークン取得の診断情報を %AppData%\AIUsageOverlay\cursor-token-debug.json へ書く。
        /// 取得失敗時の切り分け用（本番でも上書き保存する）。
        /// </summary>
        private static void WriteTokenFetchDiagnostic(string? raw, int parsedCount)
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AIUsageOverlay", "cursor-token-debug.json");
                var preview = raw ?? "";
                if (preview.Length > 2000) preview = preview[..2000];
                var payload = new
                {
                    atUtc = DateTime.UtcNow,
                    parsedCount,
                    rawLength = raw?.Length ?? 0,
                    preview,
                };
                File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 診断失敗は無視 */ }
        }

        /// <summary>傍受バッファ __cursorTokenEventPages を読み出す。</summary>
        private async Task<List<string>> ReadTokenEventPagesAsync()
        {
            if (_webView?.CoreWebView2 == null) return [];
            try
            {
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "JSON.stringify(window.__cursorTokenEventPages ?? [])");
                var json = JsonSerializer.Deserialize<string>(encoded);
                if (string.IsNullOrEmpty(json)) return [];
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            catch { return []; }
        }

        /// <summary>
        /// レスポンス JSON から総ページ数を読む。
        /// pagination.numPages が無ければ totalUsageEventsCount / pageSize から算出する。
        /// </summary>
        private static int ExtractNumPages(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("pagination", out var pg) &&
                    pg.TryGetProperty("numPages", out var n) &&
                    n.TryGetInt32(out var v))
                    return Math.Max(1, v);

                int total = 0;
                int pageSize = 100;
                if (root.TryGetProperty("totalUsageEventsCount", out var totalEl))
                {
                    if (totalEl.ValueKind == JsonValueKind.Number)
                        totalEl.TryGetInt32(out total);
                    else if (totalEl.ValueKind == JsonValueKind.String)
                        int.TryParse(totalEl.GetString(), out total);
                }
                if (root.TryGetProperty("pageSize", out var ps) && ps.TryGetInt32(out var psz) && psz > 0)
                    pageSize = psz;

                if (total > 0)
                    return Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            }
            catch { }
            return 1;
        }

        /// <summary>レスポンス JSON から pagination.currentPage を読む。読めなければ 0。</summary>
        private static int ExtractCurrentPage(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("pagination", out var pg) &&
                    pg.TryGetProperty("currentPage", out var n) &&
                    n.TryGetInt32(out var v))
                    return v;
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Cursor ログイン用の LoginWindow を表示する。
        /// Cursor 専用 Environment を共有するため Cookie が同期される。
        /// </summary>
        public async Task ShowLoginWindowAsync()
        {
            await EnsureInitializedAsync();
            if (_env == null) return;
            var loginWindow = new LoginWindow(_env, LoginUrl, "Cursor");
            loginWindow.Show();
        }

        public void Dispose()
        {
            ResetWebViewState();
            _initializationGate.Dispose();
        }

        private async Task EnsureInitializedAsync()
        {
            if (IsInitialized()) return;

            await _initializationGate.WaitAsync();
            try
            {
                if (IsInitialized()) return;

                ResetWebViewState();

                var userDataFolder = Path.Combine(Path.GetTempPath(), UserDataFolderName);
                _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                _hostWindow = new Window
                {
                    Width = 1, Height = 1, Left = -9999, Top = -9999,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                    AllowsTransparency = true, Opacity = 0,
                    Title = "CursorScraperHost"
                };
                _webView = new WebView2();
                _hostWindow.Content = _webView;
                _hostWindow.Show();

                await _webView.EnsureCoreWebView2Async(_env);
                _webView.CoreWebView2.ProcessFailed += OnProcessFailed;
                _browserProcessId = unchecked((int)_webView.CoreWebView2.BrowserProcessId);

                _webView.CoreWebView2.Settings.IsStatusBarEnabled            = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled            = false;

                _webView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/130.0.0.0 Safari/537.36";

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

        /// <summary>UsageUrl → SettingsUsageUrl の順に試みる</summary>
        private async Task<IReadOnlyList<string>?> NavigateAndCaptureAsync()
        {
            ResumeMemory();

            var result = await NavigateOnceAsync(UsageUrl);
            if (result == null)
                return null;
            if (result.Count > 0 && ContainsParseableData(result))
                return result;

            var fallback = await NavigateOnceAsync(SettingsUsageUrl);
            if (fallback == null)
                return result.Count > 0 ? result : null;
            if (fallback.Count == 0)
                return result.Count > 0 ? result : fallback;
            return fallback;
        }

        /// <summary>
        /// 指定 URL に遷移し、傍受候補または DOM テキストを返す。
        /// ログインページへ遷移した場合は null を返す。
        /// </summary>
        private async Task<IReadOnlyList<string>?> NavigateOnceAsync(string url)
        {
            if (_webView?.CoreWebView2 == null) return null;

            await _webView.CoreWebView2.ExecuteScriptAsync("window.__cursorRawCandidates = [];");

            var navTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.CoreWebView2.NavigationCompleted -= OnNav;
                navTcs.TrySetResult(e.IsSuccess);
            }
            _webView.CoreWebView2.NavigationCompleted += OnNav;
            _webView.CoreWebView2.Navigate(url);

            var navDone = await Task.WhenAny(navTcs.Task, Task.Delay(20_000));
            if (navDone != navTcs.Task || !navTcs.Task.Result)
                return [];

            var currentUrl = _webView.CoreWebView2.Source ?? "";
            if (IsLoginUrl(currentUrl))
                return null;

            List<string> lastCandidates = [];
            for (int i = 0; i < 33; i++)
            {
                await Task.Delay(300);
                var candidates = await ReadCapturedCandidatesAsync();
                if (candidates.Count == 0)
                    continue;

                lastCandidates = candidates;
                if (ContainsParseableData(candidates))
                    return candidates;
            }

            if (lastCandidates.Count > 0)
                return lastCandidates;

            var pageText = await ReadPageTextAsync();
            return pageText == null ? [] : [pageText];
        }

        /// <summary>傍受バッファの候補を読み出す。</summary>
        private async Task<List<string>> ReadCapturedCandidatesAsync()
        {
            if (_webView?.CoreWebView2 == null) return [];
            try
            {
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "JSON.stringify(window.__cursorRawCandidates ?? [])");
                var json = JsonSerializer.Deserialize<string>(encoded);
                if (string.IsNullOrEmpty(json))
                    return [];
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>候補のいずれかをパーサが解釈できるか。</summary>
        private static bool ContainsParseableData(IReadOnlyList<string> candidates)
        {
            foreach (var raw in candidates)
            {
                if (CursorUsageParser.Parse(raw) != null)
                    return true;
            }
            return false;
        }

        /// <summary>未ログインのリダイレクト先かどうかを判定する。</summary>
        private static bool IsLoginUrl(string url)
        {
            var u = url.ToLowerInvariant();
            return u.Contains("/login") ||
                   u.Contains("authenticator.cursor.sh") ||
                   u.Contains("accounts.google.com") ||
                   u.Contains("workos.com");
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

        /// <summary>
        /// 取得完了後に呼び出し、常駐 WebView2 のメモリ使用量を抑える。
        /// </summary>
        private async Task ReleaseMemoryAsync()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return;

                var navTcs = new TaskCompletionSource<bool>();
                void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    _webView.CoreWebView2.NavigationCompleted -= OnNav;
                    navTcs.TrySetResult(true);
                }
                _webView.CoreWebView2.NavigationCompleted += OnNav;
                _webView.CoreWebView2.Navigate("about:blank");
                await Task.WhenAny(navTcs.Task, Task.Delay(3_000));

                _webView.CoreWebView2.MemoryUsageTargetLevel =
                    CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch { /* メモリ最適化失敗は無視（機能には影響しない） */ }
        }

        /// <summary>
        /// 取得開始時に呼び出し、Low に下げていたメモリ目標を Normal へ戻す。
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
        /// WebView2 のプロセス異常終了ハンドラ。
        /// ブラウザプロセスが落ちても C# 側の CoreWebView2 参照は残るため、何もしないと
        /// 「初期化済み」と誤判定したまま取得もログイン画面も失敗し続ける。
        /// </summary>
        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
                return;

            _needsRecreate = true;
            LastError = $"WebView2プロセス異常終了（{e.ProcessFailedKind}）。次回取得時に再初期化します。";
        }

        /// <summary>
        /// WebView2 が利用可能な状態まで初期化済みかを判定する。
        /// CoreWebView2 の存在・再作成要求・ブラウザプロセスの生存まで確認する。
        /// </summary>
        private bool IsInitialized()
            => _initialized
            && !_needsRecreate
            && _env != null
            && _webView?.CoreWebView2 != null
            && WebView2Health.IsBrowserProcessAlive(_browserProcessId);

        /// <summary>
        /// WebView2 関連オブジェクトを破棄し、次回呼び出しで再初期化できる状態へ戻す。
        /// </summary>
        private void ResetWebViewState()
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.ProcessFailed -= OnProcessFailed;
            }
            catch { /* 既にプロセスが死んでいる場合の解除失敗は無視してよい */ }

            try
            {
                _webView?.Dispose();
            }
            catch { /* 破棄失敗は次回再作成で回復する */ }

            try
            {
                _hostWindow?.Close();
            }
            catch { /* 初期化失敗時の破棄失敗は次回再作成で回復する */ }

            _webView = null;
            _hostWindow = null;
            _env = null;
            _initialized = false;
            _needsRecreate = false;
            _browserProcessId = 0;
        }
    }
}
