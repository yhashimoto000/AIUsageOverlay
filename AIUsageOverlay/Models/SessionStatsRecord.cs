using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// トークン指標の内訳。Claude Code / Cowork / Codex の差異を吸収した共通の器。
    /// 設計書 7.1 の「課金相当トークン」「総処理トークン」はここから算出する。
    /// </summary>
    public class TokenTotals
    {
        /// <summary>入力トークン。Codex ではキャッシュ読込を内数として含む（B0/Q1 で内数と確定）。</summary>
        [JsonPropertyName("input")]
        public long Input { get; set; }

        /// <summary>出力トークン。</summary>
        [JsonPropertyName("output")]
        public long Output { get; set; }

        /// <summary>キャッシュ作成トークン。Codex の cache_write_input_tokens もここへ入れる。</summary>
        [JsonPropertyName("cacheCreate")]
        public long CacheCreate { get; set; }

        /// <summary>
        /// キャッシュ作成トークンのうち 5 分 TTL 分（cacheCreate の内数）。
        /// 単価が 1 時間 TTL と異なるためコスト換算で分ける必要がある。
        /// Claude Code の usage.cache_creation にのみ記録があり、Codex では常に 0。
        /// </summary>
        [JsonPropertyName("cacheCreate5m")]
        public long CacheCreate5m { get; set; }

        /// <summary>
        /// キャッシュ作成トークンのうち 1 時間 TTL 分（cacheCreate の内数）。
        /// 5 分 TTL より単価が高い（入力単価の 2.0 倍）。
        /// </summary>
        [JsonPropertyName("cacheCreate1h")]
        public long CacheCreate1h { get; set; }

        /// <summary>キャッシュ読込トークン。Codex の cached_input_tokens（input の内数）。</summary>
        [JsonPropertyName("cacheRead")]
        public long CacheRead { get; set; }

        /// <summary>推論トークン。Codex のみ記録があり、output の内数として別掲する。</summary>
        [JsonPropertyName("reasoning")]
        public long Reasoning { get; set; }

        /// <summary>
        /// 課金相当トークン（設計書 7.1）。
        /// Claude Code / Cowork は input + cacheCreate + output。
        /// Codex は cacheRead が input の内数のため (input - cacheRead) + output。
        /// </summary>
        /// <param name="tool">SessionStatsRecord のツール識別子</param>
        public long Billable(string tool) => tool switch
        {
            SessionStatsRecord.ToolCodex => Math.Max(0, Input - CacheRead) + Output,
            SessionStatsRecord.ToolClaudeCode or SessionStatsRecord.ToolCowork or
                SessionStatsRecord.ToolCursor => Input + CacheCreate + Output,
            _ => 0,
        };

        /// <summary>
        /// 総処理トークン（設計書 7.1）。実際にモデルが処理した量。
        /// Claude Code / Cowork は課金相当 + cacheRead、Codex は input + output（= total_tokens）。
        /// </summary>
        /// <param name="tool">SessionStatsRecord のツール識別子</param>
        public long Processed(string tool) => tool switch
        {
            SessionStatsRecord.ToolCodex => Input + Output,
            SessionStatsRecord.ToolClaudeCode or SessionStatsRecord.ToolCowork or
                SessionStatsRecord.ToolCursor => Input + CacheCreate + Output + CacheRead,
            _ => 0,
        };

        /// <summary>他方の内訳を自身へ加算する（集計用）。</summary>
        /// <param name="other">加算元</param>
        public void Add(TokenTotals other)
        {
            Input         += other.Input;
            Output        += other.Output;
            CacheCreate   += other.CacheCreate;
            CacheCreate5m += other.CacheCreate5m;
            CacheCreate1h += other.CacheCreate1h;
            CacheRead     += other.CacheRead;
            Reasoning     += other.Reasoning;
        }
    }

    /// <summary>モデル別の内訳。Codex は turn_context でターンごとにモデルが変わり得る。</summary>
    public class ModelBreakdown
    {
        /// <summary>モデル名。取得できなかった場合は「(モデル不明)」。</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>該当モデルで処理したターン数。</summary>
        [JsonPropertyName("turns")]
        public int Turns { get; set; }

        /// <summary>該当モデルのトークン内訳。</summary>
        [JsonPropertyName("tokens")]
        public TokenTotals Tokens { get; set; } = new();
    }

    /// <summary>
    /// 日別の内訳。セッションは日をまたぐため、日別グラフはターン単位で分割して持つ
    /// （設計書 7.4）。日付境界はローカル時刻で切る。
    /// </summary>
    public class DayBreakdown
    {
        /// <summary>ローカル時刻での日付（yyyy-MM-dd）。</summary>
        [JsonPropertyName("dateLocal")]
        public string DateLocal { get; set; } = string.Empty;

        /// <summary>その日に開始したターン数。</summary>
        [JsonPropertyName("turns")]
        public int Turns { get; set; }

        /// <summary>その日に計上したトークン内訳。</summary>
        [JsonPropertyName("tokens")]
        public TokenTotals Tokens { get; set; } = new();
    }

    /// <summary>
    /// 重複排除の実績。データ品質タブ（設計書 8.5）に出すための負の情報。
    /// 除外を隠すと数字が実態より確からしく見えるため、必ず保持する。
    /// </summary>
    public class DedupStats
    {
        /// <summary>重複排除前に読み取った課金レコード数。</summary>
        [JsonPropertyName("rawRecords")]
        public int RawRecords { get; set; }

        /// <summary>重複排除後に採用したレコード数。</summary>
        [JsonPropertyName("acceptedRecords")]
        public int AcceptedRecords { get; set; }

        /// <summary>鍵（requestId）を持たず、一意として無条件加算した行数。</summary>
        [JsonPropertyName("keylessRecords")]
        public int KeylessRecords { get; set; }
    }

    /// <summary>
    /// セッション（Codex ではスレッド）1 件分の集計済みレコード。
    /// キャッシュ（session-stats-cache.json）に保存する実体であり、
    /// ターン単位の明細は保持しない（設計書 6 章 / N9）。
    /// </summary>
    public class SessionStatsRecord
    {
        /// <summary>ツール識別子: Claude Code。</summary>
        public const string ToolClaudeCode = "claude-code";

        /// <summary>ツール識別子: Claude Desktop Cowork。</summary>
        public const string ToolCowork = "cowork";

        /// <summary>ツール識別子: Codex。</summary>
        public const string ToolCodex = "codex";

        /// <summary>ツール識別子: Cursor。</summary>
        public const string ToolCursor = "cursor";

        /// <summary>
        /// 一意キー。「claude:セッションID」「cowork:セッションID」
        /// 「codex:スレッドID」「cursor:セッションID」。
        /// Codex は session_id（会話ツリーのルート ID）ではなく session_meta.id
        /// （スレッド固有 ID）を使う。B0/Q2 の実測で、session_id を鍵にすると
        /// サブエージェント分（全体の 75%）が落ちることを確認した。
        /// </summary>
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        /// <summary>ToolClaudeCode / ToolCowork / ToolCodex / ToolCursor。</summary>
        [JsonPropertyName("tool")]
        public string Tool { get; set; } = string.Empty;

        /// <summary>ツール側のセッション ID（Claude Code は sessionId、Codex はスレッドの id）。</summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>会話ツリーのルート ID。Codex の session_id。親子表示のグルーピングに使う。</summary>
        [JsonPropertyName("rootId")]
        public string? RootId { get; set; }

        /// <summary>親セッションのキー。サブエージェントでなければ null。</summary>
        [JsonPropertyName("parentKey")]
        public string? ParentKey { get; set; }

        /// <summary>サブエージェントかどうか。</summary>
        [JsonPropertyName("isSubagent")]
        public bool IsSubagent { get; set; }

        /// <summary>表示用のエージェント名（Codex の agent_nickname、Claude Code のファイル名由来）。</summary>
        [JsonPropertyName("agentLabel")]
        public string? AgentLabel { get; set; }

        /// <summary>プロジェクトの比較用キー（設計書 5.3 の正規化後）。</summary>
        [JsonPropertyName("projectKey")]
        public string ProjectKey { get; set; } = string.Empty;

        /// <summary>プロジェクトの表示用文字列（最初に観測した原文）。</summary>
        [JsonPropertyName("projectDisplay")]
        public string ProjectDisplay { get; set; } = string.Empty;

        /// <summary>セッション内の最初の記録時刻（UTC）。</summary>
        [JsonPropertyName("startUtc")]
        public DateTime StartUtc { get; set; }

        /// <summary>セッション内の最後の記録時刻（UTC）。</summary>
        [JsonPropertyName("endUtc")]
        public DateTime EndUtc { get; set; }

        /// <summary>経過時間（秒）。人間の読み待ち・離席を含む。作業時間ではない（設計書 7.2 / C5）。</summary>
        [JsonPropertyName("elapsedSec")]
        public long ElapsedSec { get; set; }

        /// <summary>経過時間を測定できたか。時刻が 1 種類しかない場合は false（0 分と表示しない）。</summary>
        [JsonPropertyName("elapsedMeasurable")]
        public bool ElapsedMeasurable { get; set; } = true;

        /// <summary>最初の指示文の先頭 64 文字。設定で出力を抑止できる（S11 / Q5）。</summary>
        [JsonPropertyName("firstPrompt")]
        public string? FirstPrompt { get; set; }

        /// <summary>ターン数（ユーザー発言の回数）。</summary>
        [JsonPropertyName("turns")]
        public int Turns { get; set; }

        /// <summary>セッション全体のトークン内訳。</summary>
        [JsonPropertyName("tokens")]
        public TokenTotals Tokens { get; set; } = new();

        /// <summary>モデル別内訳。</summary>
        [JsonPropertyName("byModel")]
        public List<ModelBreakdown> ByModel { get; set; } = new();

        /// <summary>日別内訳（設計書 7.4）。</summary>
        [JsonPropertyName("byDay")]
        public List<DayBreakdown> ByDay { get; set; } = new();

        /// <summary>ツール名別の実行回数。Codex は exec の内訳をコマンド名で持つ。</summary>
        [JsonPropertyName("toolCounts")]
        public Dictionary<string, int> ToolCounts { get; set; } = new();

        /// <summary>フェーズ別の実行回数（設計書 7.3）。時間ではない。</summary>
        [JsonPropertyName("phaseCounts")]
        public Dictionary<string, int> PhaseCounts { get; set; } = new();

        /// <summary>未分類コマンドの出現回数（上位のみ）。次版の語彙追加の手掛かり。</summary>
        [JsonPropertyName("unknownCommands")]
        public Dictionary<string, int> UnknownCommands { get; set; } = new();

        /// <summary>重複排除の実績。</summary>
        [JsonPropertyName("dedup")]
        public DedupStats Dedup { get; set; } = new();

        /// <summary>JSON として壊れていてスキップした行数。</summary>
        [JsonPropertyName("parseErrors")]
        public int ParseErrors { get; set; }

        /// <summary>
        /// 末尾行が壊れていたか。Codex は最終行の累積値を採るため、
        /// 壊れているとエラーにならず静かに過小計上される（設計書 8.6）。
        /// </summary>
        [JsonPropertyName("lastLineBroken")]
        public bool LastLineBroken { get; set; }

        /// <summary>Cowork で entrypoint 識別子が無く、専用ルートを根拠に採用したか。</summary>
        [JsonPropertyName("sourceIdentityMissing")]
        public bool SourceIdentityMissing { get; set; }

        /// <summary>別ファイルと同一キーが衝突し、ファイル名を付けて保持したか。</summary>
        [JsonPropertyName("keyCollision")]
        public bool KeyCollision { get; set; }
    }
}
