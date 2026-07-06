namespace AIUsageOverlay.Models
{
    /// <summary>
    /// claude.ai/settings/usage ページのスクレイピング結果を保持するクラス。
    /// ClaudeApiClient（WebView2 経由）によって生成され、UsageService で使用される。
    /// </summary>
    public class ScrapedUsageData
    {
        /// <summary>
        /// 現在のセッション使用率（%）。
        /// HTML の aria-valuenow 属性から取得する（例: 35）。
        /// </summary>
        public int SessionPercent { get; set; }

        /// <summary>
        /// セッションのリセットまでの残り時間（分）。
        /// "4時間12分後にリセット" などのテキストをパースして算出する。
        /// </summary>
        public int SessionRemainingMinutes { get; set; }

        /// <summary>
        /// 週間制限の使用率（%）。
        /// 「すべてのモデル」行の aria-valuenow 属性から取得する（例: 32）。
        /// </summary>
        public int WeeklyPercent { get; set; }

        /// <summary>
        /// 週間リセットまでの残り時間（分）。
        /// "23:59 (土)にリセット" などのテキストをパースして現在時刻との差分を算出する。
        /// </summary>
        public int WeeklyRemainingMinutes { get; set; }

        /// <summary>
        /// セッション（5時間枠）のリセット日時（ローカル時刻）。
        /// API の resets_at をそのまま保持する。F-04 の絶対時刻表示、
        /// および後続の F-05（ペース計算）/ F-07（リセット検知）が参照する基盤値。
        /// 未使用（resets_at が null）のときは null。
        /// </summary>
        public DateTime? SessionResetAt { get; set; }

        /// <summary>
        /// 週間枠のリセット日時（ローカル時刻）。
        /// 未使用（resets_at が null）のときは null。
        /// </summary>
        public DateTime? WeeklyResetAt { get; set; }
    }
}
