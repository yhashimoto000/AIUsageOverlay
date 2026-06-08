namespace AIUsageOverlay.Models
{
    /// <summary>
    /// GitHub Copilot の使用状況データを保持するクラス。
    /// GitHubWebScraper によって生成され、UsageService 経由で ViewModel に渡される。
    /// </summary>
    public class GitHubCopilotData
    {
        /// <summary>GitHub へのスクレイピングが成功したかどうか</summary>
        public bool IsConnected { get; set; }

        /// <summary>サブスクリプションがアクティブかどうか</summary>
        public bool IsActive { get; set; }

        // ── 月次 AI クレジット ───────────────────────────────────────

        /// <summary>
        /// 今月使用した AI credits 数。
        /// 例: GitHub Copilot Pro では 1,500 credits/月。取得できなかった場合は -1。
        /// </summary>
        public int CreditsUsed { get; set; } = -1;

        /// <summary>
        /// 月次 AI credits の上限。取得できなかった場合は -1。
        /// </summary>
        public int CreditsTotal { get; set; } = -1;

        /// <summary>使用量データが取得できたかどうか</summary>
        public bool HasUsageData { get; set; }

        // ── 請求サイクル ─────────────────────────────────────────────

        /// <summary>次回リセット日（取得できなかった場合は null）</summary>
        public DateTimeOffset? NextBillingDate { get; set; }

        /// <summary>次回リセットまでの残り日数（取得できなかった場合は -1）</summary>
        public int DaysUntilRenewal { get; set; } = -1;

        /// <summary>最後に発生したエラーの説明。成功時は null。</summary>
        public string? ErrorMessage { get; set; }
    }
}
