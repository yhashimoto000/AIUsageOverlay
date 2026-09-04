namespace AIUsageOverlay.Models
{
    /// <summary>
    /// Cursor の使用制限データを保持するクラス。
    /// CursorWebScraper によって生成され、UsageService 経由で ViewModel に渡される。
    ///
    /// Cursor の枠は月次のプラン枠（included usage）が主軸。
    /// 5時間枠は無く、必要なら API モデル枠（apiPercentUsed）をチップで併記する。
    /// </summary>
    public class CursorUsageData
    {
        /// <summary>cursor.com へのスクレイピングが成功したかどうか</summary>
        public bool IsConnected { get; set; }

        /// <summary>プラン枠の使用率（0〜100）。取得できなかった場合は -1。</summary>
        public int PlanPercent { get; set; } = -1;

        /// <summary>プラン枠の使用量（リクエストまたはセント）。取得できなかった場合は -1。</summary>
        public int PlanUsed { get; set; } = -1;

        /// <summary>プラン枠の上限。取得できなかった場合は -1。</summary>
        public int PlanLimit { get; set; } = -1;

        /// <summary>プラン枠データが取得できたかどうか</summary>
        public bool HasPlanData { get; set; }

        /// <summary>Auto モデル枠の使用率（0〜100）。取得できなかった場合は -1。</summary>
        public int AutoPercentUsed { get; set; } = -1;

        /// <summary>指名モデル（API）枠の使用率（0〜100）。取得できなかった場合は -1。</summary>
        public int ApiPercentUsed { get; set; } = -1;

        /// <summary>請求サイクル終了日時。取得できなかった場合は null。</summary>
        public DateTimeOffset? BillingCycleEnd { get; set; }

        /// <summary>請求サイクル終了までの残り日数。取得できなかった場合は -1。</summary>
        public int DaysUntilReset { get; set; } = -1;

        /// <summary>請求サイクル終了までの残り分数。取得できなかった場合は -1。</summary>
        public int RemainingMinutes { get; set; } = -1;

        /// <summary>プラン種別（pro / business / enterprise 等）。取得できなかった場合は null。</summary>
        public string? MembershipType { get; set; }

        /// <summary>プラン枠が無制限かどうか</summary>
        public bool IsUnlimited { get; set; }

        /// <summary>オンデマンド（従量）が有効かどうか</summary>
        public bool OnDemandEnabled { get; set; }

        /// <summary>オンデマンド使用量。単位は API 依存。取得できなかった場合は -1。</summary>
        public int OnDemandUsed { get; set; } = -1;

        /// <summary>最後に発生したエラーの説明。成功時は null。</summary>
        public string? ErrorMessage { get; set; }
    }
}
