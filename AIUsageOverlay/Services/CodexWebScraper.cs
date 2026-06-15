using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.Parsing;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// WebView2 を使って chatgpt.com/codex の使用制限表示をスクレイピングし、
    /// Codex の 5時間制限・週間制限の使用率を取得するクライアント。
    ///
    /// 動作フロー:
    ///   1. https://chatgpt.com/codex に Navigate する
    ///   2. fetch/XHR 傍受で usage limit / rate limit 関連レスポンスを捕捉する
    ///   3. 捕捉失敗時は DOM テキストから "5h 60%" / "Weekly 18%" などを解析する
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

        /// <summary>Codex のメインページ（使用制限情報の取得トリガー）</summary>
        private const string CodexUrl = "https://chatgpt.com/codex";

        /// <summary>ChatGPT ホーム（CodexUrl で取得できない場合のフォールバック）</summary>
        private const string ChatGptUrl = "https://chatgpt.com/";

        /// <summary>Codex Cloud のタスク一覧候補 URL</summary>
        private const string CodexTasksUrl = "https://chatgpt.com/codex/tasks";

        /// <summary>Codex 公式ヘルプが案内している Usage パネル URL</summary>
        private const string CodexUsageUrl = "https://chatgpt.com/codex/settings/usage";

        /// <summary>Codex ログイン確認用 URL。未ログイン時は ChatGPT 側のログイン導線が表示される。</summary>
        public const string LoginUrl = CodexUrl;

        /// <summary>セッション保存フォルダ名</summary>
        private const string UserDataFolderName = "AIUsageOverlay_Codex_WebView2";

        /// <summary>
        /// Codex 使用量取得時に試すページ URL。
        /// いずれも表示/設定系ページの読み取りであり、会話送信や Codex 実行は行わない。
        /// </summary>
        private static readonly string[] UsageUrls =
        [
            CodexUsageUrl,
            CodexTasksUrl,
            CodexUrl,
            ChatGptUrl
        ];

        /// <summary>fetch/XHR 候補のポーリング間隔ミリ秒。</summary>
        private const int CapturePollIntervalMs = 250;

        /// <summary>1ページあたりの fetch/XHR 候補待機上限回数。</summary>
        private const int MaxCapturePolls = 16;

        /// <summary>候補が同一状態で続いた場合に早期終了する回数。</summary>
        private const int StableCapturePolls = 3;

        /// <summary>
        /// Codex / usage limit を含むレスポンスを捕捉する fetch/XHR 傍受スクリプト。
        /// window.__codexRawCandidates に候補を蓄積する。
        /// </summary>
        private const string InterceptorScript = @"
(function() {
    if (window.__codexInterceptorInstalled) return;
    window.__codexInterceptorInstalled = true;
    window.__codexRaw = null;
    window.__codexRawCandidates = [];

    function tryCapture(url, text) {
        try {
            if (!text || text.length <= 10) return;
            if (!Array.isArray(window.__codexRawCandidates)) {
                window.__codexRawCandidates = [];
            }
            if (window.__codexRawCandidates.length >= 100) return;

            const u = (url || '').toLowerCase();
            const t = (text || '').toLowerCase();
            const relevantUrl =
                u.includes('codex') ||
                u.includes('usage') ||
                u.includes('limit') ||
                u.includes('rate') ||
                u.includes('cap') ||
                u.includes('quota') ||
                u.includes('account') ||
                u.includes('settings');
            const relevantText =
                t.includes('codex') ||
                t.includes('usage') ||
                t.includes('limit') ||
                t.includes('quota') ||
                t.includes('cap') ||
                t.includes('rate_limit') ||
                t.includes('five_hour') ||
                t.includes('seven_day') ||
                t.includes('weekly') ||
                t.includes('utilization') ||
                t.includes('resets_at');

            if (relevantUrl || relevantText) {
                window.__codexRaw = text;
                window.__codexRawCandidates.push(text);
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

        /// <summary>
        /// 初期化処理の多重実行を防ぐ排他制御。
        /// 自動更新とログイン操作が同時に走っても、同一プロファイルの WebView2 を一度だけ作成する。
        /// </summary>
        private readonly SemaphoreSlim _initializationGate = new(1, 1);

        private bool _initialized;

        /// <summary>直前の取得で集めた候補数。エラー表示と診断ログ用。</summary>
        private int _lastCandidateCount;

        // ────────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>直前の呼び出しで発生したエラーの説明。成功時は null。</summary>
        public string? LastError { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Codex / ChatGPT の使用制限表示をスクレイピングして CodexUsageData を返す。
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

                var rawCandidates = await NavigateAndCaptureAsync();
                if (rawCandidates == null)
                {
                    LastError = "未ログイン（右クリック→Codexログインしてください）";
                    return null;
                }

                CodexUsageData? result = null;
                foreach (var raw in rawCandidates)
                {
                    result = CodexUsageParser.Parse(raw);
                    if (result != null) break;
                }

                if (result == null)
                {
                    LastError = $"Codex使用制限情報が取得できませんでした（候補: {_lastCandidateCount}件）";
                }

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

        /// <summary>Codex / ChatGPT ログイン用の LoginWindow を表示する</summary>
        public async Task ShowLoginWindowAsync()
        {
            await EnsureInitializedAsync();
            if (_env == null) return;
            var loginWindow = new LoginWindow(_env, LoginUrl, "Codex");
            loginWindow.Show();
        }

        public void Dispose()
        {
            ResetWebViewState();
            _initializationGate.Dispose();
        }

        // ────────────────────────────────────────────────────────────────
        // 初期化
        // ────────────────────────────────────────────────────────────────

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
                    Title = "CodexScraperHost"
                };
                _webView = new WebView2();
                _hostWindow.Content = _webView;
                _hostWindow.Show();

                await _webView.EnsureCoreWebView2Async(_env);
                _webView.CoreWebView2.Settings.IsStatusBarEnabled           = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled           = false;

                // Google OAuth がWebView2をブロックしないよう通常のChrome UAを設定する
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

        // ────────────────────────────────────────────────────────────────
        // Navigate & 傍受
        // ────────────────────────────────────────────────────────────────

        private async Task<IReadOnlyList<string>?> NavigateAndCaptureAsync()
        {
            if (_webView?.CoreWebView2 == null) return null;

            // 前回 Low に下げたメモリ目標を通常へ戻してから取得する
            ResumeMemory();

            _lastCandidateCount = 0;

            var fallbackCandidates = new List<string>();
            foreach (var url in UsageUrls)
            {
                var result = await NavigateOnceAsync(url);
                if (result == null)
                    return null;

                if (result.Count == 0)
                    continue;

                fallbackCandidates.AddRange(result);
                _lastCandidateCount = fallbackCandidates.Count;
                if (ContainsParseableCodexData(result))
                    return fallbackCandidates;
            }

            _lastCandidateCount = fallbackCandidates.Count;
            return fallbackCandidates;
        }

        /// <summary>
        /// 指定 URL に遷移し、fetch/XHR 傍受候補または DOM テキストを返す。
        /// ログインページに遷移した場合は null を返し、呼び出し元で未ログインとして扱う。
        /// </summary>
        private async Task<IReadOnlyList<string>?> NavigateOnceAsync(string url, bool allowCloudLinkFollow = true)
        {
            if (_webView?.CoreWebView2 == null) return null;

            await _webView.CoreWebView2.ExecuteScriptAsync(
                "window.__codexRaw = null; window.__codexRawCandidates = [];");

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

            // ログインページへのリダイレクト検知
            var currentUrl = _webView.CoreWebView2.Source ?? "";
            if (IsLoginUrl(currentUrl))
            {
                return null;
            }

            if (allowCloudLinkFollow && IsCodexLandingUrl(currentUrl))
            {
                await Task.Delay(1_000);
                var cloudUrl = await FindCodexCloudUrlAsync(currentUrl);
                if (!string.IsNullOrWhiteSpace(cloudUrl))
                {
                    return await NavigateOnceAsync(cloudUrl, allowCloudLinkFollow: false);
                }
            }

            var lastCandidates = new List<string>();
            var lastCandidateSignature = "";
            var stablePolls = 0;

            // fetch/XHR 傍受データをポーリングする。候補が増えなくなったら早めに
            // backend-api / inline-state の直接読み取りへ進み、表示待ち時間を短縮する。
            for (int i = 0; i < MaxCapturePolls; i++)
            {
                await Task.Delay(CapturePollIntervalMs);
                var candidates = await ReadCapturedCandidatesAsync();
                if (candidates.Count == 0)
                    continue;

                var candidateSignature = CreateCandidateSignature(candidates);
                if (candidateSignature == lastCandidateSignature)
                    stablePolls++;
                else
                {
                    stablePolls = 0;
                    lastCandidateSignature = candidateSignature;
                }

                lastCandidates = [.. candidates];
                if (ContainsLoggedOutState(candidates))
                    return null;

                if (ContainsParseableCodexData(candidates))
                    return await PreferRemainingUsagePageTextAsync(candidates);

                if (stablePolls >= StableCapturePolls)
                {
                    break;
                }
            }

            var apiCandidates = await ReadKnownChatGptApiResponsesAsync();
            if (apiCandidates.Count > 0)
            {
                lastCandidates.AddRange(apiCandidates);
                if (ContainsLoggedOutState(apiCandidates))
                    return null;

                if (ContainsParseableCodexData(apiCandidates))
                    return await PreferRemainingUsagePageTextAsync(lastCandidates);
            }

            var inlineCandidates = await ReadInlineStateCandidatesAsync();
            if (inlineCandidates.Count > 0)
            {
                lastCandidates.AddRange(inlineCandidates);
                if (ContainsLoggedOutState(inlineCandidates))
                {
                    return null;
                }

                if (ContainsParseableCodexData(inlineCandidates))
                    return await PreferRemainingUsagePageTextAsync(lastCandidates);
            }

            var pageText = await ReadPageTextAsync();
            if (pageText != null)
            {
                lastCandidates.Add(pageText);
                if (ContainsLoggedOutState([pageText]))
                {
                    return null;
                }
            }

            return lastCandidates;
        }

        /// <summary>
        /// 傍受スクリプトが蓄積したレスポンス候補を読み出す。
        /// 最初の候補だけで打ち切ると、UI 初期化用 JSON などを誤捕捉して本命の usage limit JSON を逃すため、
        /// 候補配列として保持して Parser 側で順に判定する。
        /// </summary>
        private async Task<IReadOnlyList<string>> ReadCapturedCandidatesAsync()
        {
            if (_webView?.CoreWebView2 == null) return [];
            try
            {
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__codexRawCandidates ?? []");
                return JsonSerializer.Deserialize<List<string>>(encoded) ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// 候補配列の増減を軽量に判定するためのシグネチャを作成する。
        /// 全文比較は避け、件数と末尾候補の長さで安定状態を判定する。
        /// </summary>
        private static string CreateCandidateSignature(IReadOnlyList<string> candidates)
        {
            if (candidates.Count == 0)
                return "0:0";

            var last = candidates[^1];
            return $"{candidates.Count}:{last.Length}";
        }

        /// <summary>
        /// Codex 公開ページ内の Cloud 遷移リンクを探す。
        /// ログイン済みでも /codex が公開ランディングを返すことがあるため、画面上の導線から Cloud 側へ進む。
        /// </summary>
        private async Task<string?> FindCodexCloudUrlAsync(string currentUrl)
        {
            if (_webView?.CoreWebView2 == null) return null;

            const string script = @"
(() => {
    const links = Array.from(document.querySelectorAll('a[href]')).map(a => ({
        href: a.href || '',
        text: (a.innerText || a.textContent || a.getAttribute('aria-label') || '').trim().slice(0, 80)
    }));
    return links.filter(x => {
        const joined = `${x.text} ${x.href}`.toLowerCase();
        return joined.includes('codex') ||
               joined.includes('cloud') ||
               joined.includes('クラウド') ||
               joined.includes('cloudへ移動');
    }).slice(0, 20);
})()
";

            try
            {
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                var links = JsonSerializer.Deserialize<List<CodexLinkCandidate>>(
                    encoded,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

                foreach (var link in links)
                {
                    if (IsSafeCodexCloudUrl(link.Href, currentUrl))
                        return link.Href;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        /// <summary>現在 URL が Codex の公開ランディング URL か判定する。</summary>
        private static bool IsCodexLandingUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            return uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
                && path == "/codex";
        }

        /// <summary>自動遷移してよい Codex Cloud URL か判定する。</summary>
        private static bool IsSafeCodexCloudUrl(string url, string currentUrl)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            if (!uri.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase))
                return false;

            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            if (!path.StartsWith("/codex", StringComparison.Ordinal))
                return false;

            if (path == "/codex")
                return false;

            return !string.Equals(
                uri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
                currentUrl.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ChatGPT の既知 GET エンドポイントを読み取り、Codex 使用制限候補を探す。
        /// GET のみで、会話送信・Codex 実行・補完生成は行わないためトークンは消費しない。
        /// 404 や権限エラーは通常候補として扱い、Parser が無視する。
        /// </summary>
        private async Task<IReadOnlyList<string>> ReadKnownChatGptApiResponsesAsync()
        {
            if (_webView?.CoreWebView2 == null) return [];

            const string script = @"
(async () => {
    const urls = [
        '/backend-api/me',
        '/backend-api/settings/user',
        '/backend-api/accounts/check/v4-2023-04-27',
        '/backend-api/models',
        '/backend-api/codex',
        '/backend-api/codex/bootstrap',
        '/backend-api/codex/settings',
        '/backend-api/codex/usage',
        '/backend-api/codex/usage_limits',
        '/backend-api/codex/settings/usage',
        '/backend-api/codex/limits',
        '/backend-api/codex/rate_limits',
        '/backend-api/codex/quota',
        '/backend-api/user_segments/codex_surface_usage'
    ];
    const results = [];
    for (const url of urls) {
        try {
            const response = await fetch(url, { credentials: 'include' });
            const text = await response.text();
            if (text && text.length > 10) {
                results.push(`__URL__:${url}\n${text}`);
            }
        } catch {}
    }
    return results;
})()
";

            try
            {
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                return JsonSerializer.Deserialize<List<string>>(encoded) ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// ページに埋め込まれた JSON / Next.js state / storage から使用制限候補を拾う。
        /// ブラウザ内の既存表示データを読むだけで、外部送信やトークン消費は発生しない。
        /// </summary>
        private async Task<IReadOnlyList<string>> ReadInlineStateCandidatesAsync()
        {
            if (_webView?.CoreWebView2 == null) return [];

            const string script = @"
(() => {
    const results = [];
    const pattern = /(codex|usage|limit|quota|rate|five_hour|seven_day|weekly|resets_at|utilization)/i;
    const push = (label, text) => {
        try {
            if (!text || text.length <= 10 || !pattern.test(text)) return;
            results.push(`__SOURCE__:${label}\n${text.slice(0, 50000)}`);
        } catch {}
    };

    document.querySelectorAll('script').forEach((script, index) => {
        push(`script:${index}:${script.id || ''}`, script.textContent || '');
    });

    for (const storage of [localStorage, sessionStorage]) {
        for (let i = 0; i < storage.length; i++) {
            const key = storage.key(i);
            if (!key) continue;
            push(`storage:${key}`, storage.getItem(key) || '');
        }
    }

    push('document-text', document.body ? document.body.innerText : '');
    return results.slice(0, 100);
})()
";

            try
            {
                var encoded = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                return JsonSerializer.Deserialize<List<string>>(encoded) ?? [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// 候補文字列の中に CodexUsageParser が解釈できるデータが含まれているかを判定する。
        /// ルーティング変更時に最初のページで無関係な JSON だけを捕捉した場合は、
        /// 次の URL へフォールバックして本命の usage limit データを探す。
        /// </summary>
        private static bool ContainsParseableCodexData(IReadOnlyList<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (CodexUsageParser.Parse(candidate) != null)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 候補データが未ログイン状態または公開ランディングページを示しているか判定する。
        /// ChatGPT は /codex でログイン画面へ遷移せず公開ページを返すことがあるため、本文側も検査する。
        /// </summary>
        private static bool ContainsLoggedOutState(IReadOnlyList<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (IsLoggedOutState(candidate) || IsPublicCodexLandingPage(candidate))
                    return true;
            }

            return false;
        }

        /// <summary>React Router の bootstrap state などに含まれる未ログイン状態を判定する。</summary>
        private static bool IsLoggedOutState(string text)
        {
            var normalized = text
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace(" ", "", StringComparison.Ordinal)
                .ToLowerInvariant();

            return normalized.Contains("\"authstatus\":\"logged_out\"", StringComparison.Ordinal)
                || normalized.Contains("\"authstatus\":\"unauthenticated\"", StringComparison.Ordinal)
                || normalized.Contains("\"session\":null", StringComparison.Ordinal)
                   && normalized.Contains("\"authstatus\"", StringComparison.Ordinal);
        }

        /// <summary>ログイン済みアプリ画面ではなく Codex の公開ランディングページか判定する。</summary>
        private static bool IsPublicCodexLandingPage(string text)
        {
            var lower = text.ToLowerInvariant();
            var hasCodex = lower.Contains("codex", StringComparison.Ordinal);
            var hasLogin = text.Contains("ログイン", StringComparison.Ordinal)
                || lower.Contains("log in", StringComparison.Ordinal)
                || lower.Contains("login", StringComparison.Ordinal);
            var hasPricing = text.Contains("料金", StringComparison.Ordinal)
                || lower.Contains("pricing", StringComparison.Ordinal);
            var hasDownload = text.Contains("ダウンロード", StringComparison.Ordinal)
                || lower.Contains("download", StringComparison.Ordinal);
            var hasContactSales = text.Contains("営業へのお問い合わせ", StringComparison.Ordinal)
                || lower.Contains("contact sales", StringComparison.Ordinal);

            return hasCodex && hasLogin && hasPricing && hasDownload && hasContactSales;
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

        /// <summary>
        /// Usage パネルの DOM に「残り使用量」がある場合は、そのテキストを先頭候補にして返す。
        /// API 候補が残量%を使用率として返す場合があるため、画面表示の文脈を優先して Parser に渡す。
        /// </summary>
        private async Task<IReadOnlyList<string>> PreferRemainingUsagePageTextAsync(IReadOnlyList<string> candidates)
        {
            string? pageText = null;
            for (int i = 0; i < 3; i++)
            {
                pageText = await ReadPageTextAsync();
                if (!string.IsNullOrWhiteSpace(pageText) && IsRemainingUsagePageText(pageText))
                    break;

                await Task.Delay(250);
            }

            if (string.IsNullOrWhiteSpace(pageText) || !IsRemainingUsagePageText(pageText))
                return candidates;

            var prioritized = new List<string> { pageText };
            prioritized.AddRange(candidates);
            return prioritized;
        }

        /// <summary>ページ本文が Codex Usage パネルの「残り使用量」を含むか判定する。</summary>
        private static bool IsRemainingUsagePageText(string text)
        {
            var normalized = text.ToLowerInvariant();
            return normalized.Contains("残り使用量", StringComparison.Ordinal)
                || normalized.Contains("remaining usage", StringComparison.Ordinal)
                || normalized.Contains("remaining use", StringComparison.Ordinal)
                || Regex.IsMatch(normalized, @"\d{1,3}\s*%\s*残り", RegexOptions.IgnoreCase)
                || Regex.IsMatch(normalized, @"\d{1,3}\s*%\s*(remaining|left)", RegexOptions.IgnoreCase);
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
        /// 現在 URL が ChatGPT / OpenAI のログイン・認可画面かを判定する。
        /// auth.openai.com など別ホストへ飛ぶケースもあるため、パスだけでなくホスト文字列も見る。
        /// </summary>
        private static bool IsLoginUrl(string url)
        {
            var lowerUrl = url.ToLowerInvariant();
            return lowerUrl.Contains("/login")
                || lowerUrl.Contains("/auth/")
                || lowerUrl.Contains("/authorize")
                || lowerUrl.Contains("auth.openai.com")
                || lowerUrl.Contains("auth0.openai.com");
        }

        /// <summary>
        /// WebView2 が利用可能な状態まで初期化済みかを判定する。
        /// 起動直後の初期化失敗で壊れた参照を再利用しないため、CoreWebView2 の存在も確認する。
        /// </summary>
        private bool IsInitialized()
            => _initialized && _env != null && _webView?.CoreWebView2 != null;

        /// <summary>
        /// WebView2 関連オブジェクトを破棄し、次回呼び出しで再初期化できる状態へ戻す。
        /// </summary>
        private void ResetWebViewState()
        {
            try
            {
                _hostWindow?.Close();
            }
            catch { /* 初期化失敗時の破棄失敗は次回再作成で回復する */ }

            _webView = null;
            _hostWindow = null;
            _env = null;
            _initialized = false;
        }

        /// <summary>Codex 公開ページ内のリンク候補。</summary>
        private sealed class CodexLinkCandidate
        {
            /// <summary>リンク先 URL。</summary>
            public string Href { get; set; } = "";

            /// <summary>リンク表示テキスト。</summary>
            public string Text { get; set; } = "";
        }

    }
}
