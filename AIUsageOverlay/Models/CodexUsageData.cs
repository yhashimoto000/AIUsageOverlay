namespace AIUsageOverlay.Models
{
    /// <summary>
    /// Codex / ChatGPT の使用制限データを保持するクラス。
    /// CodexWebScraper によって生成され、UsageService 経由で ViewModel に渡される。
    /// </summary>
    public class CodexUsageData
    {
        /// <summary>chatgpt.com / Codex へのスクレイピングが成功したかどうか</summary>
        public bool IsConnected { get; set; }

        // ── 5時間制限 ───────────────────────────────────────────────

        /// <summary>5時間制限の使用率（0〜100）。取得できなかった場合は -1。</summary>
        public int SessionPercent { get; set; } = -1;

        /// <summary>5時間制限リセットまでの残り分数。取得できなかった場合は -1。</summary>
        public int SessionRemainingMinutes { get; set; } = -1;

        /// <summary>5時間制限のリセット時刻表示。例: "22:22"。取得できなかった場合は null。</summary>
        public string? SessionResetText { get; set; }

        /// <summary>5時間制限データが取得できたかどうか</summary>
        public bool HasSessionData { get; set; }

        // ── 週間制限 ────────────────────────────────────────────────

        /// <summary>週間制限の使用率（0〜100）。取得できなかった場合は -1。</summary>
        public int WeeklyPercent { get; set; } = -1;

        /// <summary>週間制限リセットまでの残り分数。取得できなかった場合は -1。</summary>
        public int WeeklyRemainingMinutes { get; set; } = -1;

        /// <summary>週間制限のリセット日付・時刻表示。例: "6月22日 22:22"。取得できなかった場合は null。</summary>
        public string? WeeklyResetText { get; set; }

        /// <summary>週間制限データが取得できたかどうか</summary>
        public bool HasWeeklyData { get; set; }

        /// <summary>最後に発生したエラーの説明。成功時は null。</summary>
        public string? ErrorMessage { get; set; }
    }
}
