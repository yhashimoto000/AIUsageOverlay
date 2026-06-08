using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// アプリケーションの設定情報を保持するクラス。
    /// %AppData%\AIUsageOverlay\settings.json に JSON 形式で永続化される。
    /// </summary>
    public class AppSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>GitHub Copilot セクションを表示するかどうか</summary>
        [JsonPropertyName("gitHubCopilotEnabled")]
        public bool GitHubCopilotEnabled { get; set; } = false;

        /// <summary>OpenAI / Codex セクションを表示するかどうか</summary>
        [JsonPropertyName("codexEnabled")]
        public bool CodexEnabled { get; set; } = false;

        /// <summary>セッション制限時間（分）。デフォルト 300 分</summary>
        [JsonPropertyName("sessionLimitMinutes")]
        public int SessionLimitMinutes { get; set; } = 300;

        /// <summary>週間制限時間（分）。デフォルト 10080 分</summary>
        [JsonPropertyName("weeklyLimitMinutes")]
        public int WeeklyLimitMinutes { get; set; } = 10080;

        /// <summary>更新間隔（秒）。デフォルト 30 秒</summary>
        [JsonPropertyName("refreshIntervalSeconds")]
        public int RefreshIntervalSeconds { get; set; } = 30;

        /// <summary>ウィンドウ X 座標。-1 の場合は中央配置</summary>
        [JsonPropertyName("windowLeft")]
        public double WindowLeft { get; set; } = -1;

        /// <summary>ウィンドウ Y 座標</summary>
        [JsonPropertyName("windowTop")]
        public double WindowTop { get; set; } = 10;

        /// <summary>ウィンドウ不透明度（0.1 ～ 1.0）</summary>
        [JsonPropertyName("windowOpacity")]
        public double WindowOpacity { get; set; } = 1.0;
    }
}
