using System.IO;
using System.Text.Json;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// Claude / GitHub Copilot / Codex の使用量追跡・管理を担当するサービスクラス。
    /// </summary>
    public class UsageService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIUsageOverlay");
        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
        private static readonly string UsageFilePath    = Path.Combine(AppDataFolder, "usage.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private AppSettings _settings;
        private UsageRecord _usageRecord;
        private DateTime    _lastUpdateTime;

        // ── 外部サービスクライアント ──────────────────────────────────
        private readonly ClaudeApiClient  _apiClient     = new();
        private readonly GitHubWebScraper _gitHubScraper = new();
        private readonly CodexWebScraper  _codexScraper  = new();

        public UsageService()
        {
            Directory.CreateDirectory(AppDataFolder);
            _settings       = LoadSettings();
            _usageRecord    = LoadUsageRecord();
            _lastUpdateTime = DateTime.Now;
            ResetWeeklyIfNeeded();
        }

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
