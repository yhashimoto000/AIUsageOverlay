using System.Text.Json;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>Codex 履歴 JSONL の行種別。payload.type を集計上の区分へ写像したもの。</summary>
    public enum CodexLineKind
    {
        /// <summary>集計対象外。</summary>
        Other,

        /// <summary>セッション属性（cwd / id / fork 情報 / サブエージェント情報）。</summary>
        SessionMeta,

        /// <summary>トークンの抽出元。total_token_usage は累積値。</summary>
        TokenCount,

        /// <summary>モデル名の抽出元。ターンごとに変わり得る。</summary>
        TurnContext,

        /// <summary>ターン境界・指示文の抽出元。</summary>
        UserMessage,

        /// <summary>シェル実行。フェーズ分類の材料。</summary>
        CustomToolCall,

        /// <summary>ファイル編集の完了イベント。実装フェーズの確定材料。</summary>
        PatchApplyEnd,

        /// <summary>MCP 呼び出しの完了イベント。</summary>
        McpToolCallEnd,

        /// <summary>文脈圧縮の発生。参考指標。</summary>
        ContextCompacted,
    }

    /// <summary>
    /// Codex 履歴 JSONL の 1 行をパースした結果。
    /// ファイル単位の集約は SessionStatsScanner が行う。
    /// </summary>
    public class CodexRolloutLine
    {
        /// <summary>集計上の行種別。</summary>
        public CodexLineKind Kind { get; init; }

        /// <summary>記録時刻（UTC）。取得できなければ null。</summary>
        public DateTime? TimestampUtc { get; init; }

        // ── session_meta の項目 ──────────────────────────────────────

        /// <summary>会話ツリーのルート ID。重複排除の鍵には使わない（B0/Q2）。</summary>
        public string? SessionId { get; init; }

        /// <summary>スレッド固有 ID。重複排除の鍵はこちら（B0/Q2）。</summary>
        public string? ThreadId { get; init; }

        /// <summary>fork 元のスレッド ID。</summary>
        public string? ForkedFromId { get; init; }

        /// <summary>親スレッド ID。</summary>
        public string? ParentThreadId { get; init; }

        /// <summary>スレッドの出自（user / subagent / automation）。</summary>
        public string? ThreadSource { get; init; }

        /// <summary>サブエージェントの表示名。</summary>
        public string? AgentNickname { get; init; }

        /// <summary>作業ディレクトリ。</summary>
        public string? Cwd { get; init; }

        // ── token_count の項目 ───────────────────────────────────────

        /// <summary>累積入力トークン（total_token_usage）。最終行の値だけを採る。</summary>
        public long TotalInputTokens { get; init; }

        /// <summary>累積出力トークン（total_token_usage）。</summary>
        public long TotalOutputTokens { get; init; }

        /// <summary>累積キャッシュ読込トークン。input の内数（B0/Q1 で確定）。</summary>
        public long TotalCachedInputTokens { get; init; }

        /// <summary>累積キャッシュ書込トークン。実測時点では常に 0 だが取り込みは省略しない。</summary>
        public long TotalCacheWriteTokens { get; init; }

        /// <summary>累積推論トークン。output の内数。</summary>
        public long TotalReasoningTokens { get; init; }

        /// <summary>ターン差分の合計トークン（last_token_usage）。日別按分に使う。</summary>
        public long LastTurnTotalTokens { get; init; }

        /// <summary>ターン差分の入力トークン。</summary>
        public long LastTurnInputTokens { get; init; }

        /// <summary>ターン差分の出力トークン。</summary>
        public long LastTurnOutputTokens { get; init; }

        /// <summary>ターン差分のキャッシュ読込トークン。</summary>
        public long LastTurnCachedInputTokens { get; init; }

        /// <summary>ターン差分の推論トークン。</summary>
        public long LastTurnReasoningTokens { get; init; }

        // ── その他 ───────────────────────────────────────────────────

        /// <summary>turn_context のモデル名。</summary>
        public string? Model { get; init; }

        /// <summary>user_message の本文。</summary>
        public string? UserText { get; init; }

        /// <summary>custom_tool_call のツール名（実測ではすべて exec）。</summary>
        public string? ToolName { get; init; }

        /// <summary>custom_tool_call から取り出したシェルコマンド本体。</summary>
        public string? ShellCommand { get; init; }
    }

    /// <summary>
    /// Codex の rollout JSONL を 1 行ずつ構造体へ変換する純関数パーサ。
    /// ファイル I/O は持たない（CLAUDE.md の規約でパースは Services/Parsing に置く）。
    /// </summary>
    public static class CodexRolloutParser
    {
        /// <summary>custom_tool_call の input を包んでいる JS ラッパの呼び出し名。</summary>
        private const string ShellWrapperPrefix = "tools.shell_command(";

        /// <summary>
        /// JSONL の 1 行をパースする。壊れた行は null を返し、呼び出し側でスキップ数に計上する。
        /// </summary>
        /// <param name="line">JSONL の 1 行</param>
        /// <returns>パース結果。JSON として壊れている場合は null</returns>
        public static CodexRolloutLine? ParseLine(string line)
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

                var timestamp = ReadTimestamp(root, "timestamp");
                if (!root.TryGetProperty("payload", out var payload) ||
                    payload.ValueKind != JsonValueKind.Object)
                {
                    return new CodexRolloutLine { Kind = CodexLineKind.Other, TimestampUtc = timestamp };
                }

                var payloadType = ReadString(payload, "type");
                return payloadType switch
                {
                    "session_meta"     => ParseSessionMeta(payload, timestamp),
                    "token_count"      => ParseTokenCount(payload, timestamp),
                    "turn_context"     => new CodexRolloutLine
                    {
                        Kind = CodexLineKind.TurnContext,
                        TimestampUtc = timestamp,
                        Model = ReadString(payload, "model"),
                        Cwd = ReadString(payload, "cwd"),
                    },
                    "user_message"     => new CodexRolloutLine
                    {
                        Kind = CodexLineKind.UserMessage,
                        TimestampUtc = timestamp,
                        UserText = ReadString(payload, "message"),
                    },
                    "custom_tool_call" => ParseCustomToolCall(payload, timestamp),
                    "patch_apply_end"  => new CodexRolloutLine
                    {
                        Kind = CodexLineKind.PatchApplyEnd,
                        TimestampUtc = timestamp,
                    },
                    "mcp_tool_call_end" => new CodexRolloutLine
                    {
                        Kind = CodexLineKind.McpToolCallEnd,
                        TimestampUtc = timestamp,
                    },
                    "context_compacted" => new CodexRolloutLine
                    {
                        Kind = CodexLineKind.ContextCompacted,
                        TimestampUtc = timestamp,
                    },
                    _ => new CodexRolloutLine { Kind = CodexLineKind.Other, TimestampUtc = timestamp },
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>session_meta 行を読む。payload 直下と payload.payload の 2 形式に備える。</summary>
        /// <param name="payload">payload 要素</param>
        /// <param name="timestamp">行の時刻</param>
        private static CodexRolloutLine ParseSessionMeta(JsonElement payload, DateTime? timestamp)
        {
            // 一部の版では session_meta の中身が payload.payload に入る形式がある
            var body = payload.TryGetProperty("payload", out var inner) &&
                       inner.ValueKind == JsonValueKind.Object
                ? inner
                : payload;

            return new CodexRolloutLine
            {
                Kind           = CodexLineKind.SessionMeta,
                TimestampUtc   = timestamp ?? ReadTimestamp(body, "timestamp"),
                SessionId      = ReadString(body, "session_id"),
                ThreadId       = ReadString(body, "id"),
                ForkedFromId   = ReadString(body, "forked_from_id"),
                ParentThreadId = ReadString(body, "parent_thread_id"),
                ThreadSource   = ReadString(body, "thread_source"),
                AgentNickname  = ReadString(body, "agent_nickname"),
                Cwd            = ReadString(body, "cwd"),
            };
        }

        /// <summary>token_count 行を読む。total は累積、last はターン差分。</summary>
        /// <param name="payload">payload 要素</param>
        /// <param name="timestamp">行の時刻</param>
        private static CodexRolloutLine ParseTokenCount(JsonElement payload, DateTime? timestamp)
        {
            if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            {
                return new CodexRolloutLine { Kind = CodexLineKind.Other, TimestampUtc = timestamp };
            }

            var total = info.TryGetProperty("total_token_usage", out var t) &&
                        t.ValueKind == JsonValueKind.Object
                ? (JsonElement?)t
                : null;
            var last = info.TryGetProperty("last_token_usage", out var l) &&
                       l.ValueKind == JsonValueKind.Object
                ? (JsonElement?)l
                : null;

            if (total is null)
            {
                return new CodexRolloutLine { Kind = CodexLineKind.Other, TimestampUtc = timestamp };
            }

            return new CodexRolloutLine
            {
                Kind                      = CodexLineKind.TokenCount,
                TimestampUtc              = timestamp,
                TotalInputTokens          = ReadLong(total.Value, "input_tokens"),
                TotalOutputTokens         = ReadLong(total.Value, "output_tokens"),
                TotalCachedInputTokens    = ReadLong(total.Value, "cached_input_tokens"),
                TotalCacheWriteTokens     = ReadLong(total.Value, "cache_write_input_tokens"),
                TotalReasoningTokens      = ReadLong(total.Value, "reasoning_output_tokens"),
                LastTurnTotalTokens       = last is { } lv ? ReadLong(lv, "total_tokens") : 0,
                LastTurnInputTokens       = last is { } li ? ReadLong(li, "input_tokens") : 0,
                LastTurnOutputTokens      = last is { } lo ? ReadLong(lo, "output_tokens") : 0,
                LastTurnCachedInputTokens = last is { } lc ? ReadLong(lc, "cached_input_tokens") : 0,
                LastTurnReasoningTokens   = last is { } lr ? ReadLong(lr, "reasoning_output_tokens") : 0,
            };
        }

        /// <summary>custom_tool_call 行を読み、JS ラッパからシェルコマンドを取り出す。</summary>
        /// <param name="payload">payload 要素</param>
        /// <param name="timestamp">行の時刻</param>
        private static CodexRolloutLine ParseCustomToolCall(JsonElement payload, DateTime? timestamp)
            => new()
            {
                Kind         = CodexLineKind.CustomToolCall,
                TimestampUtc = timestamp,
                ToolName     = ReadString(payload, "name"),
                ShellCommand = ExtractShellCommand(ReadString(payload, "input")),
            };

        /// <summary>
        /// custom_tool_call の input からシェルコマンド本体を取り出す。
        ///
        /// input は「const r = await tools.shell_command({"command":"...","workdir":"..."}); text(r)」
        /// という JS ラッパ内に JSON が埋まった二重構造である（設計書 4.2）。
        /// コマンド内にエスケープされた二重引用符が含まれるため、
        /// 正規表現で command を直接拾わず、括弧の対応を数えて JSON 部分を切り出す。
        /// </summary>
        /// <param name="input">custom_tool_call の input 文字列</param>
        /// <returns>コマンド本体。取り出せない場合は null</returns>
        public static string? ExtractShellCommand(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            var start = input.IndexOf(ShellWrapperPrefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            var argStart = start + ShellWrapperPrefix.Length;
            var end = FindMatchingParen(input, argStart);
            if (end < 0)
            {
                return null;
            }

            var json = input.Substring(argStart, end - argStart).Trim();
            if (json.Length == 0)
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    ? ReadString(doc.RootElement, "command")
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// 開き括弧の直後の位置から、対応する閉じ括弧の位置を探す。
        /// 文字列リテラル内（エスケープを含む）の括弧は数えない。
        /// </summary>
        /// <param name="s">走査対象</param>
        /// <param name="from">開き括弧の直後の位置</param>
        /// <returns>対応する閉じ括弧の位置。見つからなければ -1</returns>
        private static int FindMatchingParen(string s, int from)
        {
            var depth = 1;
            var inString = false;
            var escaped = false;

            for (var i = from; i < s.Length; i++)
            {
                var c = s[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '(':
                        depth++;
                        break;
                    case ')':
                        depth--;
                        if (depth == 0)
                        {
                            return i;
                        }
                        break;
                }
            }
            return -1;
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
