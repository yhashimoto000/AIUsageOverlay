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
        /// GitHub Personal Access Token。
        /// GitHub Copilot 使用状況の取得に使用する（scopes: read:user, read:org）。
        /// 未設定の場合は GitHub Copilot セクションを非表示にする。
        /// </summary>
        [JsonPropertyName("gitHubPat")]
        public string GitHubPat { get; set; } = string.Empty;

        /// <summary>
        /// GitHub 組織名（省略可）。
        /// 設定した場合、組織の Copilot シート使用状況（使用中/総数）を取得する。
        /// 未設定の場合は個人プランとして認証確認のみ行う。
        /// </summary>
        [JsonPropertyName("gitHubOrg")]
        public string GitHubOrg { get; set; } = string.Empty;



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

        /// <su