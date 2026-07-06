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

        /// <summary>Codex 使用制限セクションを表示するかどうか</summary>
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

        // ────────────────────────────────────────────────────────────────
        // P1（CodexBar 機能取込）追加設定
        // すべて既定値ありのため、旧 settings.json は未知キー補完で後方互換を保つ。
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// トレイアイコンの形式。F-01。
        /// "dualBar"（上=セッション/下=週間の2段バー、既定） / "donut"（従来のドーナツ+%）。
        /// </summary>
        [JsonPropertyName("trayIconStyle")]
        public string TrayIconStyle { get; set; } = "dualBar";

        /// <summary>
        /// 注意（オレンジ）を開始する使用率の閾値（%）。F-03。
        /// この値以上 WarningThresholdPercent 未満が「注意」レベル。
        /// </summary>
        [JsonPropertyName("cautionThresholdPercent")]
        public int CautionThresholdPercent { get; set; } = 50;

        /// <summary>
        /// 警告（赤）を開始する使用率の閾値（%）。F-03。
        /// この値以上が「警告」レベル。
        /// </summary>
        [JsonPropertyName("warningThresholdPercent")]
        public int WarningThresholdPercent { get; set; } = 80;

        /// <summary>オーバーレイのバー上に閾値マーカー（目盛）を描画するか。F-03。</summary>
        [JsonPropertyName("showThresholdMarkers")]
        public bool ShowThresholdMarkers { get; set; } = true;

        /// <summary>
        /// リセット時刻の表示形式。F-04。
        /// "relative"（残り時間、既定） / "absolute"（"14:32 リセット" 形式）。
        /// </summary>
        [JsonPropertyName("resetDisplayMode")]
        public string ResetDisplayMode { get; set; } = "relative";
    }
}
