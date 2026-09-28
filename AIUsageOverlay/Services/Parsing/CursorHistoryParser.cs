using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// Cursor エージェント履歴の tool_use ブロック 1 件。
    /// Shell はツール名だけでは意図が分からないため、フェーズ分類で使うコマンド本体も併せて持つ。
    /// </summary>
    public class CursorToolUse
    {
        /// <summary>ツール名（Read / StrReplace / Shell など）。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Shell の場合のコマンド本体。それ以外は null。</summary>
        public string? Command { get; init; }
    }

    /// <summary>
    /// Cursor の agent-transcripts JSONL の 1 行をパースした結果。
    /// ファイル単位の集約は SessionStatsScanner が行う。
    ///
    /// 実測（371 ファイル）では token / usage フィールドは存在しない。
    /// トークン内訳は出せず、セッション・ツール実行・フェーズのみを集計する。
    /// </summary>
    public class CursorHistoryLine
    {
        /// <summary>行の役割（user / assistant）。</summary>
        public string Role { get; init; } = string.Empty;

        /// <summary>user 本文に埋め込まれた時刻（UTC）。無ければ null。</summary>
        public DateTime? TimestampUtc { get; init; }

        /// <summary>user の指示文。timestamp タグを除いた本文。</summary>
        public string? UserText { get; init; }

        /// <summary>この行に含まれる tool_use ブロック。</summary>
        public IReadOnlyList<CursorToolUse> ToolUses { get; init; } = Array.Empty<CursorToolUse>();
    }

    /// <summary>
    /// Cursor の agent-transcripts JSONL を 1 行ずつ構造体へ変換する純関数パーサ。
    /// ファイル I/O は持たない。
    /// </summary>
    public static class CursorHistoryParser
    {
        /// <summary>user 本文に埋め込まれた時刻タグ。</summary>
        private static readonly Regex TimestampTag = new(
            @"<timestamp>\s*(.*?)\s*</timestamp>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>user 本文の指示文タグ。</summary>
        private static readonly Regex UserQueryTag = new(
            @"<user_query>\s*(.*?)\s*</user_query>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// JSONL の 1 行をパースする。壊れた行は null を返し、呼び出し側でスキップ数に計上する。
        /// </summary>
        /// <param name="line">JSONL の 1 行</param>
        /// <returns>パース結果。JSON として壊れている場合は null</returns>
        public static CursorHistoryLine? ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                var role = ReadString(root, "role") ?? string.Empty;
                var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
                    ? (JsonElement?)m
                    : null;

                string? userText = null;
                DateTime? timestampUtc = null;
                if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    userText = ExtractUserText(message);
                    timestampUtc = ExtractTimestampUtc(userText);
                    userText = StripPromptTags(userText);
                }

                return new CursorHistoryLine
                {
                    Role = role,
                    TimestampUtc = timestampUtc,
                    UserText = userText,
                    ToolUses = ExtractToolUses(message),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>message.content の text ブロックを連結する。</summary>
        private static string? ExtractUserText(JsonElement? message)
        {
            if (message is not { } msg ||
                !msg.TryGetProperty("content", out var content))
            {
                return null;
            }

            if (content.ValueKind == JsonValueKind.String)
                return content.GetString();

            if (content.ValueKind != JsonValueKind.Array)
                return null;

            List<string>? parts = null;
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                    continue;
                if (!string.Equals(ReadString(block, "type"), "text", StringComparison.OrdinalIgnoreCase))
                    continue;
                var text = ReadString(block, "text");
                if (string.IsNullOrEmpty(text))
                    continue;
                parts ??= new List<string>();
                parts.Add(text);
            }

            return parts is null ? null : string.Join("\n", parts);
        }

        /// <summary>本文中の timestamp タグを UTC に変換する。</summary>
        private static DateTime? ExtractTimestampUtc(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var match = TimestampTag.Match(text);
            if (!match.Success)
                return null;

            var raw = match.Groups[1].Value.Trim();

            // 例: "Thursday, Sep 3, 2026, 11:39 PM (UTC+9)"
            // DateTimeOffset.TryParse は末尾の (UTC±N) を解釈できないため、本体とオフセットを分ける。
            var withOffset = Regex.Match(raw,
                @"^(?<body>.+?)\s*\(UTC(?<offset>[+-]\d{1,2})\)$",
                RegexOptions.CultureInvariant);
            if (withOffset.Success &&
                DateTime.TryParse(
                    withOffset.Groups["body"].Value.Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var local) &&
                int.TryParse(withOffset.Groups["offset"].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var hours))
            {
                var offset = TimeSpan.FromHours(hours);
                return new DateTimeOffset(
                    DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset).UtcDateTime;
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                return parsed.UtcDateTime;
            }

            return null;
        }

        /// <summary>timestamp / user_query タグを除き、指示文だけを残す。</summary>
        private static string? StripPromptTags(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var query = UserQueryTag.Match(text);
            if (query.Success)
                return query.Groups[1].Value.Trim();

            var stripped = TimestampTag.Replace(text, string.Empty).Trim();
            return string.IsNullOrEmpty(stripped) ? null : stripped;
        }

        /// <summary>message.content から tool_use ブロックを取り出す。</summary>
        private static IReadOnlyList<CursorToolUse> ExtractToolUses(JsonElement? message)
        {
            if (message is not { } msg ||
                !msg.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<CursorToolUse>();
            }

            List<CursorToolUse>? uses = null;
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                    continue;
                if (!string.Equals(ReadString(block, "type"), "tool_use", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = ReadString(block, "name");
                if (string.IsNullOrEmpty(name))
                    continue;

                string? command = null;
                if (block.TryGetProperty("input", out var input) &&
                    input.ValueKind == JsonValueKind.Object)
                {
                    command = ReadString(input, "command");
                }

                uses ??= new List<CursorToolUse>();
                uses.Add(new CursorToolUse { Name = name, Command = command });
            }

            return uses ?? (IReadOnlyList<CursorToolUse>)Array.Empty<CursorToolUse>();
        }

        /// <summary>文字列プロパティを読む。</summary>
        private static string? ReadString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
                return null;
            return el.GetString();
        }
    }
}
