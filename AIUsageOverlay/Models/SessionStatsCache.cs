using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// セッション統計キャッシュの全体。
    /// %AppData%\AIUsageOverlay\session-stats-cache.json に保存する（設計書 6 章）。
    /// 既存の history.json（使用率のリングバッファ）とは別物である。
    /// </summary>
    public class SessionStatsCache
    {
        /// <summary>
        /// 現在のスキーマ版数。分類ルールや指標定義を変えたら上げる。
        /// 版が違えば全件再パースする（設計書 6 章）。
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>キャッシュのスキーマ版数。</summary>
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>最後に走査した時刻（UTC）。</summary>
        [JsonPropertyName("lastScanUtc")]
        public DateTime LastScanUtc { get; set; }

        /// <summary>差分判定用のファイル情報。集計には使わない。</summary>
        [JsonPropertyName("files")]
        public List<ScannedHistoryFile> Files { get; set; } = new();

        /// <summary>集計の実体。1 セッション（Codex は 1 スレッド）1 件。</summary>
        [JsonPropertyName("sessions")]
        public List<SessionStatsRecord> Sessions { get; set; } = new();
    }
}
