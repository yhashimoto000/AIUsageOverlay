namespace AIUsageOverlay.Models
{
    /// <summary>
    /// OpenAI / Codex の使用状況データを保持するクラス。
    /// CodexWebScraper によって生成され、UsageService 経由で ViewModel に渡される。
    /// </summary>
    public class CodexUsageData
    {
        /// <summary>platform.openai.com へのスクレイピングが成功したかどうか</summary>
        public bool IsConnected { get; set; }

        // ── クレジット残高 ───────────────────────────────────────────

        /// <summary>
        /// 残りクレジット残高（USD）。
        /// プリペイドクレジットを使用している場合に取得できる。
        /// 取得できなかった場合は -1。
        /// </summary>
        public decimal CreditBalance { get; set; } = -1m;

        /// <summary>購入済み総クレジット（USD）。取得できなかった場合は -1。</summary>
        public decimal CreditTotal { get; set; } = -1m;

        /// <summary>クレジット残高データが取得できたかどうか</summary>
        public bool HasCreditData { get; set; }

        // ── 当月使用量 ───────────────────────────────────────────────

        /// <summary>
        /// 当月の API 使用額（USD）。
        /// 取得できなかった場合は -1。
        /// </summary>
        public decimal MonthlyUsageUsd { get; set; } = -1m;

        /// <summary>当月の使用量データが取得できたかどうか</summary>
        public bool HasUsageData { get; set; }

        /// <summary>最後に発生したエラーの説明。成功時は null。</summary>
        public string? ErrorMessage { get; set; }
    }
}
