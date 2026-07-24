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
        /// トレイアイコンの形式。F-01 / デザイン刷新（1e）。
        /// "ring"（ストローク弧+中央%、既定） / "dualBar"（上=セッション/下=週間の2段バー改） /
        /// "donut"（従来のドーナツ+%） / "numeric"（%数値+ミニバー）。
        /// </summary>
        [JsonPropertyName("trayIconStyle")]
        public string TrayIconStyle { get; set; } = "ring";

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

        // ────────────────────────────────────────────────────────────────
        // P2（ペースと通知）追加設定
        // ────────────────────────────────────────────────────────────────

        /// <summary>ペース行（F-05/F-06）を表示するか。既定 true。OFF で計算もスキップ。</summary>
        [JsonPropertyName("paceEnabled")]
        public bool PaceEnabled { get; set; } = true;

        /// <summary>閾値超過・リセット・上限到達の通知（F-07）を出すか。既定 true。</summary>
        [JsonPropertyName("notificationsEnabled")]
        public bool NotificationsEnabled { get; set; } = true;

        /// <summary>
        /// 通知する使用率の閾値（%）。F-07。既定 [70, 90]（Win-CodexBar と一致）。
        /// 各窓ごとに「前回値 &lt; 閾値 ≤ 今回値」の跨ぎを 1 回通知する。
        /// </summary>
        [JsonPropertyName("notificationThresholds")]
        public int[] NotificationThresholds { get; set; } = new[] { 70, 90 };

        /// <summary>セッション/週間枠のリセット完了を通知するか。F-07。既定 true。</summary>
        [JsonPropertyName("notifyOnReset")]
        public bool NotifyOnReset { get; set; } = true;

        /// <summary>100% 到達（上限）を閾値設定と独立に通知するか。F-07。既定 true。</summary>
        [JsonPropertyName("notifyOnExhausted")]
        public bool NotifyOnExhausted { get; set; } = true;

        // ────────────────────────────────────────────────────────────────
        // P4（運用改善）追加設定
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 適応更新間隔（F-10）を有効にするか。既定 true。
        /// OFF のときは従来どおり <see cref="RefreshIntervalSeconds"/> の固定間隔で更新する。
        /// </summary>
        [JsonPropertyName("adaptiveRefreshEnabled")]
        public bool AdaptiveRefreshEnabled { get; set; } = true;

        // ────────────────────────────────────────────────────────────────
        // デザイン刷新（オーバーレイ 2b）追加設定
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// オーバーレイのサービス行にスパークライン（使用率%の自己記録推移）を表示するか。既定 true。
        /// OFF のとき履歴の記録は継続するが描画のみ省く。点が 2 未満のときは自動的に非表示。
        /// スパークラインは表示形式 "list"（1a）でのみ描画する。
        /// </summary>
        [JsonPropertyName("showSparkline")]
        public bool ShowSparkline { get; set; } = true;

        /// <summary>
        /// オーバーレイの表示形式。デザイン刷新（1a⇔1b 切替）。
        /// "list"（1a: 縦積みリスト型＋スパークライン、既定） /
        /// "compact"（1b: コンパクト⇔詳細のクリック切替型）。
        /// </summary>
        [JsonPropertyName("overlayLayout")]
        public string OverlayLayout { get; set; } = "list";

        /// <summary>
        /// 表示形式 "compact"（1b）のときの詳細パネル展開状態。既定 true（展開）。
        /// クリックのたびに反転して永続化し、次回起動時に前回の状態を復元する。
        /// </summary>
        [JsonPropertyName("overlayExpanded")]
        public bool OverlayExpanded { get; set; } = true;

        /// <summary>
        /// スパークライン履歴の保持時間（時間）。F-16。既定 24h。
        /// この窓より古い点は破棄する。旧実装は Claude/Codex の 5時間枠に合わせ 5h 固定だったが、
        /// 断続起動でも履歴が残りグラフが表示されるよう延長・設定化した。読込時に最小 1h でガードする。
        /// </summary>
        [JsonPropertyName("sparklineRetentionHours")]
        public int SparklineRetentionHours { get; set; } = 24;
    }
}
