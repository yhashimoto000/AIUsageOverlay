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
        /// GitHub Copilot 表示を有効にするかどうか。
        /// true のとき GitHub Copilot セクションを表示し、スクレイピングを実行する。
        /// WebView2 の GitHub セッションは %TEMP%\AIUsageOverlay_GitHub_WebView2 に永続保存される。
        /// </summary>
        [JsonPropertyName("gitHubCopilotEnabled")]
        public bool GitHubCopilotEnabled { get; set; } = false;

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
        /// オーバーレイウィンド�