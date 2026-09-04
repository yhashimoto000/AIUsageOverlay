using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// cursor.com ダッシュボード API（get-filtered-usage-events）の
    /// 1 イベント分のトークン使用量を保持する POCO。
    ///
    /// 個人プランではコスト（chargedCents）は 2026-07-31 以降ゼロ返しになるため
    /// トークン数フィールドのみを記録する。
    ///
    /// <para>改訂履歴</para>
    /// <list type="table"><listheader>
    /// <term>Date</term><term>Author</term><term>Version</term><term>Comment</term>
    /// </listheader>
    /// <item>
    /// <description>2026.09.04</description><description>Auto</description><description>-</description>
    /// <description>新規作成</description>
    /// </item>
    /// </list>
    /// </summary>
    public class CursorTokenRecord
    {
        /// <summary>イベントのタイムスタンプ（エポック秒またはミリ秒文字列）。</summary>
        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        /// <summary>会話 ID。重複除去キーとして使用する。</summary>
        [JsonPropertyName("conversationId")]
        public string ConversationId { get; set; } = string.Empty;

        /// <summary>使用モデル名。</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>入力トークン数。</summary>
        [JsonPropertyName("inputTokens")]
        public long InputTokens { get; set; }

        /// <summary>出力トークン数。</summary>
        [JsonPropertyName("outputTokens")]
        public long OutputTokens { get; set; }

        /// <summary>キャッシュ書き込みトークン数。</summary>
        [JsonPropertyName("cacheWriteTokens")]
        public long CacheWriteTokens { get; set; }

        /// <summary>キャッシュ読み取りトークン数。</summary>
        [JsonPropertyName("cacheReadTokens")]
        public long CacheReadTokens { get; set; }

        /// <summary>
        /// 請求対象トークン数（Claude Code に合わせた計算式）。
        /// inputTokens + outputTokens * 3 + cacheWriteTokens + cacheReadTokens * 0.1
        /// ただし Cursor の課金体系は異なるため参考値扱い。
        /// </summary>
        [JsonIgnore]
        public long BillableApprox =>
            InputTokens + OutputTokens * 3L + CacheWriteTokens + (long)(CacheReadTokens * 0.1);

        /// <summary>イベント日付（UTC、yyyy-MM-dd 形式）。月別集計のキーに使用。</summary>
        [JsonIgnore]
        public string DateUtc => TryParseTimestamp(Timestamp, out var dt)
            ? dt.ToString("yyyy-MM-dd")
            : string.Empty;

        /// <summary>イベント年月（yyyy-MM 形式）。月別ストアのキーに使用。</summary>
        [JsonIgnore]
        public string YearMonth => DateUtc.Length >= 7 ? DateUtc[..7] : string.Empty;

        /// <summary>
        /// イベント単位の重複除去キー。
        /// 同一 conversationId でもモデル呼び出しごとに別イベントになるため、
        /// 会話 ID ではなく timestamp・モデル・各トークン数を組み合わせる。
        /// </summary>
        [JsonIgnore]
        public string EventKey =>
            $"{Timestamp}|{ConversationId}|{Model}|{InputTokens}|{OutputTokens}|{CacheWriteTokens}|{CacheReadTokens}";

        /// <summary>タイムスタンプ文字列を UTC の DateTime に変換する。</summary>
        /// <param name="ts">エポックミリ秒またはエポック秒の文字列</param>
        /// <param name="result">変換結果</param>
        /// <returns>変換成功かどうか</returns>
        public static bool TryParseTimestamp(string ts, out DateTime result)
        {
            result = default;
            if (!long.TryParse(ts, out var n)) return false;
            // エポックミリ秒（13桁）かエポック秒（10桁）かを桁数で判定
            var epoch = n > 1_000_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(n)
                : DateTimeOffset.FromUnixTimeSeconds(n);
            result = epoch.UtcDateTime;
            return true;
        }
    }

    /// <summary>
    /// 月別の Cursor トークン累計を保持する永続化コンテナ。
    /// %AppData%\AIUsageOverlay\cursor-tokens.json に保存される。
    ///
    /// <para>改訂履歴</para>
    /// <list type="table"><listheader>
    /// <term>Date</term><term>Author</term><term>Version</term><term>Comment</term>
    /// </listheader>
    /// <item>
    /// <description>2026.09.04</description><description>Auto</description><description>-</description>
    /// <description>新規作成</description>
    /// </item>
    /// </list>
    /// </summary>
    public class CursorTokenStore
    {
        /// <summary>
        /// 既に保存済みのイベントキー集合（重複除去用）。
        /// キーは "yyyy-MM"、値はその月に取り込んだ EventKey の集合。
        /// </summary>
        [JsonPropertyName("seenIds")]
        public Dictionary<string, HashSet<string>> SeenIds { get; set; } = [];

        /// <summary>
        /// 月別トークン集計。キーは "yyyy-MM"、値は集計値。
        /// </summary>
        [JsonPropertyName("monthly")]
        public Dictionary<string, CursorMonthlyTokens> Monthly { get; set; } = [];

        /// <summary>最終保存日時（UTC）。</summary>
        [JsonPropertyName("savedAtUtc")]
        public DateTime SavedAtUtc { get; set; }

        /// <summary>
        /// イベント列を取り込んで重複除去しながら月別集計に加算する。
        /// </summary>
        /// <param name="events">パース済みイベント列</param>
        /// <returns>新たに追加されたイベント数</returns>
        public int Merge(IEnumerable<CursorTokenRecord> events)
        {
            int added = 0;
            foreach (var ev in events)
            {
                if (string.IsNullOrEmpty(ev.YearMonth)) continue;

                // 重複除去：同じモデル呼び出しイベントは月内で 1 回だけ計上
                // （同一 conversationId の複数リクエストは別キーになり、トークンを落とさない）
                if (!SeenIds.TryGetValue(ev.YearMonth, out var seen))
                {
                    seen = [];
                    SeenIds[ev.YearMonth] = seen;
                }
                if (!seen.Add(ev.EventKey)) continue;

                if (!Monthly.TryGetValue(ev.YearMonth, out var month))
                {
                    month = new CursorMonthlyTokens { YearMonth = ev.YearMonth };
                    Monthly[ev.YearMonth] = month;
                }
                month.InputTokens      += ev.InputTokens;
                month.OutputTokens     += ev.OutputTokens;
                month.CacheWriteTokens += ev.CacheWriteTokens;
                month.CacheReadTokens  += ev.CacheReadTokens;
                month.EventCount++;
                added++;
            }
            return added;
        }

        /// <summary>指定年月の集計を取得する。存在しない場合は空の集計を返す。</summary>
        /// <param name="yearMonth">yyyy-MM 形式</param>
        /// <returns>月別集計</returns>
        public CursorMonthlyTokens GetMonth(string yearMonth)
            => Monthly.TryGetValue(yearMonth, out var m) ? m : new CursorMonthlyTokens { YearMonth = yearMonth };
    }

    /// <summary>
    /// 1 ヶ月分の Cursor トークン集計値。
    ///
    /// <para>改訂履歴</para>
    /// <list type="table"><listheader>
    /// <term>Date</term><term>Author</term><term>Version</term><term>Comment</term>
    /// </listheader>
    /// <item>
    /// <description>2026.09.04</description><description>Auto</description><description>-</description>
    /// <description>新規作成</description>
    /// </item>
    /// </list>
    /// </summary>
    public class CursorMonthlyTokens
    {
        /// <summary>対象年月（yyyy-MM）。</summary>
        [JsonPropertyName("yearMonth")]
        public string YearMonth { get; set; } = string.Empty;

        /// <summary>入力トークン合計。</summary>
        [JsonPropertyName("inputTokens")]
        public long InputTokens { get; set; }

        /// <summary>出力トークン合計。</summary>
        [JsonPropertyName("outputTokens")]
        public long OutputTokens { get; set; }

        /// <summary>キャッシュ書き込みトークン合計。</summary>
        [JsonPropertyName("cacheWriteTokens")]
        public long CacheWriteTokens { get; set; }

        /// <summary>キャッシュ読み取りトークン合計。</summary>
        [JsonPropertyName("cacheReadTokens")]
        public long CacheReadTokens { get; set; }

        /// <summary>イベント件数（リクエスト数）。</summary>
        [JsonPropertyName("eventCount")]
        public int EventCount { get; set; }

        /// <summary>入力 + 出力の合計トークン数（表示用）。</summary>
        [JsonIgnore]
        public long TotalTokens => InputTokens + OutputTokens;
    }
}
