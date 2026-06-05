namespace AIUsageOverlay.Models
{
    /// <summary>
    /// GitHub Copilot の使用状況データを保持するクラス。
    /// GitHubApiClient によって生成され、UsageService 経由で ViewModel に渡される。
    /// </summary>
    public class GitHubCopilotData
    {
        /// <summary>GitHub PAT による認証が成功したかどうか</summary>
        public bool IsConnected { get; set; }

        /// <summary>認証済みユーザーのログイン名（例: "yhashimoto"）</summary>
        public string UserLogin { get; set; } = string.Empty;

        /// <summary>
        /// 今サイクルのアクティブシート数（組織プランのみ）。
        /// 個人プランの場合は 0。
        /// </summary>
        public int SeatsUsed { get; set; }

        /// <summary>
        /// 組織の総シート数（組織プランのみ）。
        /// 個人プランの場合は 0。
        /// </summary>
        public int SeatsTotal { get; set; }

        /// <summary>
        /// 組織のシート情報が取得できたかどうか。
        /// false の場合は個人プランまたは組織名未設定。
        /// </summary>
        public bool HasOrgData { get; set; }

        /// <summary>最後に発生したエラーの説明。成功時は null。</summary>
        public string? ErrorMessage { get; set; }
    }
}
