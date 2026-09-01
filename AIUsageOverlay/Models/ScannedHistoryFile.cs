using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// 走査した履歴ファイル 1 件の情報。差分判定（設計書 3.4 の [2]）にのみ使い、
    /// 集計には使わない。読めなかったファイルは <see cref="SkipReason"/> に理由を残し、
    /// データ品質タブ（設計書 8.5）へ出す。
    /// </summary>
    public class ScannedHistoryFile
    {
        /// <summary>履歴ファイルのフルパス。</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>ファイルサイズ（バイト）。読み取り前後で比較して追記を検出する。</summary>
        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        /// <summary>最終更新時刻（UTC）。サイズと合わせて差分判定に使う。</summary>
        [JsonPropertyName("mtimeUtc")]
        public DateTime MtimeUtc { get; set; }

        /// <summary>このファイルから作られたセッションのキー。読めなかった場合は null。</summary>
        [JsonPropertyName("sessionKey")]
        public string? SessionKey { get; set; }

        /// <summary>読めなかった理由。読めた場合は null。</summary>
        [JsonPropertyName("skipReason")]
        public string? SkipReason { get; set; }
    }
}
