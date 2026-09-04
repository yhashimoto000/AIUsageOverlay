using System.IO;
using System.Text.Json;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// Claude / GitHub Copilot / Codex / Cursor の使用量追跡・管理を担当するサービスクラス。
    /// </summary>
    public class UsageService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIUsageOverlay");
        private static readonly string SettingsFilePath      = Path.Combine(AppDataFolder, "settings.json");
        private static readonly string UsageFilePath          = Path.Combine(AppDataFolder, "usage.json");
        private static readonly string CursorTokensFilePath   = Path.Combine(AppDataFolder, "cursor-tokens.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private AppSettings _settings;
        private UsageRecord _usageRecord;
        private DateTime    _lastUpdateTime;

        // ── 外部サービスクライアント ──────────────────────────────────
        private readonly ClaudeApiClient  _apiClient      = new();
        private readonly GitHubWebScraper _gitHubScraper  = new();
        private readonly CodexWebScraper  _codexScraper   = new();
        private readonly CursorWebScraper _cursorScraper  = new();

        /// <summary>
        /// 使用率%の自己記録（スパークライン用）。統合窓口としてここに内包し、
        /// 記録・取得は本クラス経由で ViewModel へ提供する（データ取得は各サービスとの直接通信のみ）。
        /// 保持窓は AppSettings.SparklineRetentionHours をコンストラクタで注入する（F-16）。
        /// </summary>
        private readonly UsageHistoryService _history;

        public UsageService()
        {
            Directory.CreateDirectory(AppDataFolder);
            _settings       = LoadSettings();
            // F-16: スパークライン履歴の保持窓と最大点数を設定値で初期化する（_settings 確定後に生成する）
            _history        = new UsageHistoryService(
                _settings.SparklineRetentionHours, _settings.RefreshIntervalSeconds);
            _usageRecord    = LoadUsageRecord();
            _lastUpdateTime = DateTime.Now;
            ResetWeeklyIfNeeded();
        }

        // ── 使用率履歴（スパークライン用）────────────────────────────

        /// <summary>指定系列に使用率%を 1 点記録する（取得成功時に ViewModel から呼ぶ）。</summary>
        /// <param name="series"><see cref="UsageHistoryService.SeriesClaude"/> 等の系列キー</param>
        /// <param name="sessionPercent">主使用率（%）</param>
        /// <param name="weeklyPercent">週間使用率（%、無い場合 0）</param>
        public void RecordHistory(string series, double sessionPercent, double weeklyPercent)
            => _history.Record(series, sessionPercent, weeklyPercent);

        /// <summary>指定系列の使用率%推移（描画用に間引き済み）を古い順で取得する。</summary>
        public IReadOnlyList<double> GetHistory(string series)
            => _history.GetSessionSeries(series);

        // ── Claude ───────────────────────────────────────────────────

        public AppSettings GetSettings() => _settings;
        public string? GetLastApiError() => _apiClient.LastError;

        public async Task ShowLoginWindowAsync() => await _apiClient.ShowLoginWindowAsync();

        public void SaveSettings(AppSettings settings)
        {
            _settings = settings;
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }

        public (double sessionRatio, int sessionRemainingMinutes,
                double weeklyRatio, int weeklyRemainingMinutes) UpdateAndGetUsage()
        {
            var now            = DateTime.Now;
            var elapsedMinutes = (now - _lastUpdateTime).TotalMinutes;
            _lastUpdateTime    = now;

            ResetWeeklyIfNeeded();

            var sessionElapsed = (now - _usageRecord.SessionStartTime).TotalMinutes;
            if (sessionElapsed >= _settings.SessionLimitMinutes)
            {
                ResetSession();
                sessionElapsed = 0;
            }

            _usageRecord.WeeklyUsedMinutes += elapsedMinutes;
            _usageRecord.LastActiveTime     = now;
            if (_usageRecord.WeeklyUsedMinutes > _settings.WeeklyLimitMinutes)
                _usageRecord.WeeklyUsedMinutes = _settings.WeeklyLimitMinutes;

            SaveUsageRecord();

            double sessionRatio    = Math.Min(1.0, sessionElapsed / _settings.SessionLimitMinutes);
            int    sessionRemaining = Math.Max(0, (int)(_settings.SessionLimitMinutes - sessionElapsed));
            double weeklyRatio     = Math.Min(1.0, _usageRecord.WeeklyUsedMinutes / _settings.WeeklyLimitMinutes);
            var    nextMonday      = _usageRecord.WeekStartDate.AddDays(7);
            int    weeklyRemaining = Math.Max(0, (int)(nextMonday - now).TotalMinutes);

            return (sessionRatio, sessionRemaining, weeklyRatio, weeklyRemaining);
        }

        /// <summary>
        /// Claude の使用量を取得する。WebView2 傍受（API）が成功すればそれを返し、
        /// 失敗時はローカル推定値へフォールバックする。
        ///
        /// F-04 で戻り値に sessionResetAt / weeklyResetAt（リセット日時）を追加した。
        /// API 経由のときのみ実値が入り、ローカルフォールバック時は null（絶対時刻表示は不可）。
        /// isFromApi が false のとき、呼び出し側は stale（情報が古い）として扱う。
        /// </summary>
        public async Task<(double sessionRatio, int sessionRemainingMinutes,
                           double weeklyRatio, int weeklyRemainingMinutes, bool isFromApi,
                           DateTime? sessionResetAt, DateTime? weeklyResetAt)>
            UpdateAndGetUsageAsync()
        {
            var scraped = await _apiClient.FetchUsageAsync();
            if (scraped != null)
            {
                return (scraped.SessionPercent / 100.0, scraped.SessionRemainingMinutes,
                        scraped.WeeklyPercent  / 100.0, scraped.WeeklyRemainingMinutes, true,
                        scraped.SessionResetAt, scraped.WeeklyResetAt);
            }
            var local = UpdateAndGetUsage();
            // ローカル推定はリセット日時を持たないため null を返す（表示側で相対表示にフォールバック）
            return (local.sessionRatio, local.sessionRemainingMinutes,
                    local.weeklyRatio,  local.weeklyRemainingMinutes, false, null, null);
        }

        public void ResetSession()
        {
            _usageRecord.SessionStartTime = DateTime.Now;
            _lastUpdateTime = DateTime.Now;
            SaveUsageRecord();
        }

        public void ResetWeekly()
        {
            _usageRecord.WeekStartDate     = UsageRecord.GetThisMonday();
            _usageRecord.WeeklyUsedMinutes = 0;
            SaveUsageRecord();
        }

        // ── GitHub Copilot ───────────────────────────────────────────

        public string? GetLastGitHubError() => _gitHubScraper.LastError;

        public async Task<GitHubCopilotData?> FetchGitHubCopilotAsync()
        {
            if (!_settings.GitHubCopilotEnabled) return null;
            return await _gitHubScraper.FetchCopilotDataAsync();
        }

        public async Task ShowGitHubLoginWindowAsync()
            => await _gitHubScraper.ShowLoginWindowAsync();

        // ── Codex / ChatGPT ──────────────────────────────────────────

        public string? GetLastCodexError() => _codexScraper.LastError;

        public async Task<CodexUsageData?> FetchCodexAsync()
        {
            if (!_settings.CodexEnabled) return null;
            return await _codexScraper.FetchUsageAsync();
        }

        public async Task ShowCodexLoginWindowAsync()
            => await _codexScraper.ShowLoginWindowAsync();

        // ── Cursor ───────────────────────────────────────────────────

        public string? GetLastCursorError() => _cursorScraper.LastError;

        public async Task<CursorUsageData?> FetchCursorAsync()
        {
            if (!_settings.CursorEnabled) return null;
            return await _cursorScraper.FetchUsageAsync();
        }

        public async Task ShowCursorLoginWindowAsync()
            => await _cursorScraper.ShowLoginWindowAsync();

        /// <summary>
        /// FetchCursorAsync の後に呼び出し、傍受済みトークンイベントを
        /// cursor-tokens.json に差分マージして保存する。
        /// 今月分が未蓄積なら全件取得にフォールバックする。
        /// </summary>
        /// <returns>新たに追加したイベント件数</returns>
        public async Task<int> MergeCursorTokenEventsFromCacheAsync()
        {
            if (!_settings.CursorEnabled) return 0;

            var yearMonth = DateTime.UtcNow.ToString("yyyy-MM");
            var existing = LoadCursorTokenStore().GetMonth(yearMonth);
            if (existing.EventCount == 0)
                return await FetchAndSaveAllCurrentMonthCursorTokensAsync();

            var events = await _cursorScraper.FetchTokenEventsFromCacheAsync();
            if (events.Count == 0) return 0;
            return SaveCursorTokenEvents(events);
        }

        /// <summary>
        /// 起動時に当月トークンイベント全件を取得して cursor-tokens.json に保存する。
        /// </summary>
        /// <returns>新たに追加したイベント件数</returns>
        public async Task<int> FetchAndSaveAllCurrentMonthCursorTokensAsync()
        {
            if (!_settings.CursorEnabled) return 0;
            var events = await _cursorScraper.FetchAllCurrentMonthTokenEventsAsync();
            // 0 件でも最終試行時刻を残し、次回フォールバック判定と診断に使う
            if (events.Count == 0)
            {
                var empty = LoadCursorTokenStore();
                empty.SavedAtUtc = DateTime.UtcNow;
                File.WriteAllText(CursorTokensFilePath,
                    JsonSerializer.Serialize(empty, JsonOptions));
                return 0;
            }
            return SaveCursorTokenEvents(events);
        }

        /// <summary>
        /// 指定年月のトークンイベントを取得して cursor-tokens.json にマージ保存する。
        /// メンテナンス用（例: 過去月の一括取り込み）。CursorEnabled の可否は見ない。
        /// </summary>
        /// <param name="year">西暦年</param>
        /// <param name="month">月（1〜12）</param>
        /// <returns>新たに追加したイベント件数</returns>
        public async Task<int> FetchAndSaveCursorMonthAsync(int year, int month)
        {
            var events = await _cursorScraper.FetchMonthTokenEventsAsync(year, month);
            if (events.Count == 0) return 0;
            return SaveCursorTokenEvents(events);
        }

        /// <summary>
        /// cursor-tokens.json を読み込み、イベントをマージして上書き保存する。
        /// </summary>
        private int SaveCursorTokenEvents(List<Models.CursorTokenRecord> events)
        {
            var store = LoadCursorTokenStore();
            var added = store.Merge(events);
            if (added > 0)
            {
                store.SavedAtUtc = DateTime.UtcNow;
                File.WriteAllText(CursorTokensFilePath,
                    JsonSerializer.Serialize(store, JsonOptions));
            }
            return added;
        }

        /// <summary>cursor-tokens.json から CursorTokenStore を読み込む。</summary>
        public static Models.CursorTokenStore LoadCursorTokenStoreStatic()
            => LoadCursorTokenStoreFrom(CursorTokensFilePath);

        /// <summary>cursor-tokens.json から CursorTokenStore を読み込む（インスタンスメソッド）。</summary>
        public Models.CursorTokenStore LoadCursorTokenStore()
            => LoadCursorTokenStoreFrom(CursorTokensFilePath);

        private static Models.CursorTokenStore LoadCursorTokenStoreFrom(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new Models.CursorTokenStore();
                return JsonSerializer.Deserialize<Models.CursorTokenStore>(
                    File.ReadAllText(path))
                    ?? new Models.CursorTokenStore();
            }
            catch
            {
                return new Models.CursorTokenStore();
            }
        }

        /// <summary>指定年月の Cursor 月別トークン集計を返す。</summary>
        /// <param name="yearMonth">yyyy-MM 形式。省略時は今月。</param>
        /// <returns>月別集計（データ無しの場合はゼロ値）</returns>
        public Models.CursorMonthlyTokens GetCursorMonthlyTokens(string? yearMonth = null)
        {
            yearMonth ??= DateTime.UtcNow.ToString("yyyy-MM");
            return LoadCursorTokenStore().GetMonth(yearMonth);
        }

        // ── 内部ヘルパー ─────────────────────────────────────────────

        private void ResetWeeklyIfNeeded()
        {
            var thisMonday = UsageRecord.GetThisMonday();
            if (_usageRecord.WeekStartDate.Date != thisMonday.Date)
            {
                _usageRecord.WeekStartDate     = thisMonday;
                _usageRecord.WeeklyUsedMinutes = 0;
                SaveUsageRecord();
            }
        }

        private AppSettings LoadSettings()
        {
            if (!File.Exists(SettingsFilePath)) return new AppSettings();
            try
            {
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsFilePath)) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        private UsageRecord LoadUsageRecord()
        {
            if (!File.Exists(UsageFilePath)) return new UsageRecord();
            try
            {
                return JsonSerializer.Deserialize<UsageRecord>(
                    File.ReadAllText(UsageFilePath)) ?? new UsageRecord();
            }
            catch { return new UsageRecord(); }
        }

        private void SaveUsageRecord()
        {
            try
            {
                File.WriteAllText(UsageFilePath,
                    JsonSerializer.Serialize(_usageRecord, JsonOptions));
            }
            catch { }
        }
    }
}
