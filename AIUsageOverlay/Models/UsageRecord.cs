using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// Claude の使用量記録データを保持するクラス。
    /// %AppData%\AIUsageOverlay\usage.json に JSON 形式で永続化される。
    /// アプリ起動中に経過時間が加算され、週をまたぐと自動リセットされる。
    /// </summary>
    public class UsageRecord
    {
        /// <summary>
        /// 現在のセッション開始日時。
        /// セッションリセット時またはセッション制限到達時に更新される。
        /// </summary>
        [JsonPropertyName("sessionStartTime")]
        public DateTime SessionStartTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 現在週の開始日（月曜日の 0:00:00）。
        /// 週をまたぐと自動的に更新され、WeeklyUsedMinutes がリセットされる。
        /// </summary>
        [JsonPropertyName("weekStartDate")]
        public DateTime WeekStartDate { get; set; } = GetThisMonday();

        /// <summary>
        /// 今週の累計使用時間（分）。
        /// アプリが起動中のみカウントされ、月曜日に自動リセットされる。
        /// </summary>
        [JsonPropertyName("weeklyUsedMinutes")]
        public double WeeklyUsedMinutes { get; set; } = 0;

        /// <summary>
        /// 前回アプリが終了した日時。
        /// 次回起動時にアプリが停止していた時間を除外するために使用する。
        /// </summary>
        [JsonPropertyName("lastActiveTime")]
        public DateTime LastActiveTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 今週の月曜日 0:00:00 を返す静的ユーティリティメソッド。
        /// DayOfWeek の列挙値を使って現在日付から月曜日を算出する。
        /// </summary>
        /// <returns>今週月曜日の DateTime（時刻は 00:00:00）</returns>
        public static DateTime GetThisMonday()
        {
            var today = DateTime.Today;
            // DayOfWeek.Monday = 1、日曜日 = 0 のため、(dayOfWeek - 1 + 7) % 7 で月曜日からの差分を求める
            int daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return today.AddDays(-daysFromMonday);
        }
    }
}
