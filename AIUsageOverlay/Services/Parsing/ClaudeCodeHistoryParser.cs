using System.Text.Json;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// assistant 行に含まれる tool_use ブロック 1 件。
    /// Bash / PowerShell はツール名だけでは意図が分からないため、
    /// フェーズ分類（設計書 7.3.1）で使うコマンド本体も併せて持つ。
    /// </summary>
    public class ClaudeToolUse
    {
        /// <summary>ツール名（Read / Edit / Bash など）。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Bash / PowerShell の場合のコマンド本体。それ以外は null。</summary>
        public string? Command { get; init; }
    }

    /// <summary>
    /// Claude Code 履歴 JSONL の 1 行をパースした結果。
    /// ファイル単位の集約は SessionStatsScanner が行う。
    /// </summary>
    public class ClaudeCodeLine
    {
        /// <summary>行種別（assistant / user / system など）。</summary>
        public string Type { get; init; } = string.Empty;

        /// <summary>記録時刻（UTC）。取得できなければ null。</summary>
        public DateTime? TimestampUtc { get; init; }

        /// <summary>リクエスト ID。重複排除の第 1 鍵（設計書 5.1）。</summary>
        public string? RequestId { get; init; }

        /// <summary>メッセージ ID。重複排除の第 2 鍵。</summary>
        public string? MessageId { get; init; }

        /// <summary>モデル名。</summary>
        public string? Model { get; init; }

        /// <summary>作業ディレクトリ。プロジェクト別集計に使う。</summary>
        public string? Cwd { get; init; }

        /// <summary>セッション ID。</summary>
        public string? SessionId { get; init; }

        /// <summary>サブエージェント（サイドチェーン）かどうか。</summary>
        public bool IsSidechain { get; init; }

        /// <summary>入力トークン。</summary>
        public long InputTokens { get; init; }

        /// <summary>出力トークン。</summary>
        public long OutputTokens { get; init; }

        /// <summary>キャッシュ作成トークン。</summary>
        public long CacheCreationTokens { get; init; }

        /// <summary>キャッシュ読込トークン。</summary>
        public long CacheReadTokens { get; init; }

        /// <summary>usage ブロックを持っていたか（課金レコードかどうかの判定）。</summary>
        public bool HasUsage { get; init; }

        /// <summary>この行に含まれる tool_use ブロック。</summary>
        public IReadOnlyList<ClaudeToolUse> ToolUses { get; init; } = Array.Empty<ClaudeToolUse>();

        /// <summary>user 行のテキスト本文。指示文の抽出に使う。</summary>
        public string? UserText { get; init; }
    }

    /// <summary>
    /// Claude Code の履歴 JSONL を 1 行ずつ構造体へ変換する純関数パーサ。
    /// ファイル I/O は持たない（CLAUDE.md の規約でパースは Services/Parsing に置く）。
    /// </summary>
    public static class ClaudeCodeHistoryParser
    {
        /// <summary>
        /// JSONL の 1 行をパースする。壊れた行は null を返し、呼び出し側でスキップ数に計上する。
        /// 1 行の破損でセッション全体を捨てない（設計書 4 章）。
        /// </summary>
        /// <param name="line">JSONL の 1 行</param>
        /// <returns>パース結果。JSON として壊れている場合は null</returns>
        public static ClaudeCodeLine? ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
                    ? (JsonElement?)m
                    : null;

                // usage: iterations / cache_creation の入れ子は同じ値の再掲なので加算に使わない（設計書 4.1）
                long input = 0, output = 0, cacheCreate = 0, cacheRead = 0;
                var hasUsage = false;
                if (message is { } msg &&
                    msg.TryGetProperty("usage", out var usage) &&
                    usage.ValueKind == JsonValueKind.Object)
                {
                    hasUsage = true;
                    input       = ReadLong(usage, "input_tokens");
                    output      = ReadLong(usage, "output_tokens");
                    cacheCreate = ReadLong(usage, "cache_creation_input_tokens");
                    cacheRead   = ReadLong(usage, "cache_read_input_tokens");
                }

                return new ClaudeCodeLine
                {
                    Type                = ReadString(root, "type") ?? string.Empty,
                    TimestampUtc        = ReadTimestamp(root, "timestamp"),
                    RequestId           = ReadString(root, "requestId"),
                    MessageId           = message is { } mm ? ReadString(mm, "id") : null,
                    Model               = message is { } mo ? ReadString(mo, "model") : null,
                    Cwd                 = ReadString(root, "cwd"),
                    SessionId           = ReadString(root, "sessionId"),
                    IsSidechain         = root.TryGetProperty("isSidechain", out var sc) &&
                                          sc.ValueKind == JsonValueKind.True,
                    InputTokens         = input,
                    OutputTokens        = output,
                    CacheCreationTokens = cacheCreate,
                    CacheReadTokens     = cacheRead,
                    HasUsage            = hasUsage,
                    ToolUses            = ExtractToolUses(message),
                    UserText            = ExtractUserText(root, message),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// message.content から tool_use ブロックを取り出す。
        /// Bash / PowerShell は input.command も併せて拾い、フェーズ分類の材料にする。
        /// </summary>
        /// <param name="message">message 要素</param>
        private static IReadOnlyList<ClaudeToolUse> ExtractToolUses(JsonElement? message)
        {
            if (message is not { } msg ||
                !msg.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ClaudeToolUse>();
            }

            List<ClaudeToolUse>? uses = null;
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (ReadString(block, "type") != "tool_use")
                {
                    continue;
                }
                var name = ReadString(block, "name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                string? command = null;
                if (block.TryGetProperty("input", out var input) &&
                    input.ValueKind == JsonValueKind.Object)
                {
                    command = ReadString(input, "command");
                }

                (uses ??= new List<ClaudeToolUse>()).Add(new ClaudeToolUse
                {
                    Name = name,
                    Command = command,
                });
            }
            return (IReadOnlyList<ClaudeToolUse>?)uses ?? Array.Empty<ClaudeToolUse>();
        }

        /// <summary>
        /// user 行の本文を取り出す。content は文字列の場合と、
        /// type=text のブロック配列の場合がある。
        /// </summary>
        /// <param name="root">行のルート要素</param>
        /// <param name="message">message 要素</param>
        private static string? ExtractUserText(JsonElement root, JsonElement? message)
        {
            if (ReadString(root, "type") != "user" || message is not { } msg)
            {
                return null;
            }

            if (!msg.TryGetProperty("content", out var content))
            {
                return null;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            if (content.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (ReadString(block, "type") != "text")
                {
                    continue;
                }
                var text = ReadString(block, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            return null;
        }

        /// <summary>オブジェクトから文字列プロパティを安全に読む。</summary>
        /// <param name="element">対象要素</param>
        /// <param name="name">プロパティ名</param>
        private static string? ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        /// <summary>オブジェクトから数値プロパティを安全に読む。欠落時は 0。</summary>
        /// <param name="element">対象要素</param>
        /// <param name="name">プロパティ名</param>
        private static long ReadLong(JsonElement element, string name)
            => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number &&
               v.TryGetInt64(out var n)
                ? n
                : 0;

        /// <summary>ISO8601 の時刻文字列を UTC の DateTime として読む。</summary>
        /// <param name="element">対象要素</param>
        /// <param name="name">プロパティ名</param>
        private static DateTime? ReadTimestamp(JsonElement element, string name)
        {
            var s = ReadString(element, name);
            if (string.IsNullOrEmpty(s))
            {
                return null;
            }
            return DateTime.TryParse(
                s,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dt)
                ? dt
                : null;
        }
    }
}
