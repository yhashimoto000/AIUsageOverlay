using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// アプリケーションの設定情報を保持するクラス。
    /// %AppData%\AIUsageOverlay\settings.json に JSON 形式で永続化される。
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Anthropic API キー（将来の API 連携機能のために予約）
        /// </summary>
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = string.Empty;



        /// <summary>
        /// セッションの制限時間（分）。
        /// デフォルト: 300 分（5 時間）= Claude Pro の標準セッション上限に相当
        /// </summary>
        [JsonPropertyName("sessionLimitMinutes")]
        public int SessionLimitMinutes { get; set; } = 300;

        /// <summary>
        /// 週間の制限時間（分）。
        /// デフォルト: 10080 分（7 日間 × 24 時間 × 60 分）
        /// </summary>
        [JsonPropertyName("weeklyLimitMinutes")]
        public int WeeklyLimitMinutes { get; set; } = 10080;

        /// <summary>
        /// 使用量データの更新間隔（秒）。
        /// デフォルト: 30 秒
        /// </summary>
        [JsonPropertyName("refreshIntervalSeconds")]
        public int RefreshIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// オーバーレイウィンドウの X 座標。
        /// -1 の場合は画面水平中央に自動配置する。
        /// </summary>
        [JsonPropertyName("windowLeft")]
        public double WindowLeft { get; set; } = -1;

        /// <summary>
        /// オーバーレイウィンドウの Y 座標（画面上端からのピクセル数）。
        /// デフォルト: 10（画面上端から 10px）
        /// </summary>
        [JsonPropertyName("windowTop")]
        public double WindowTop { get; set; } = 10;

        /// <summary>
        /// ウィンドウの不透明度（0.1 ～ 1.0）。
        /// デフォルト: 1.0（完全不透明）
        /// </summary>
        [JsonPropertyName("windowOpacity")]
        public double WindowOpacity { get; set; } = 1.0;
    }
}
