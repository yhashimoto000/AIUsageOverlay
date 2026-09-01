using System.IO;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.Parsing;

namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>履歴ディレクトリの読み取り状態。0 件と読めないは意味が違う（設計書 8.6）。</summary>
    public enum ToolScanStatus
    {
        /// <summary>履歴ディレクトリが存在しない。そのツールは未使用として扱う。</summary>
        NotUsed,

        /// <summary>正常に走査できた。</summary>
        Ok,

        /// <summary>ディレクトリはあるが読めない（権限・EFS 等）。未使用と区別する。</summary>
        Unreadable,
    }

    /// <summary>走査の結果。集計とデータ品質タブの両方がここを入力にする。</summary>
    public class ScanResult
    {
        /// <summary>読み取れたセッション（キャッシュから流用したものを含む）。</summary>
        public List<SessionStatsRecord> Sessions { get; } = new();

        /// <summary>差分判定用のファイル情報。次回のキャッシュに保存する。</summary>
        public List<ScannedHistoryFile> Files { get; } = new();

        /// <summary>Claude Code 履歴の読み取り状態。</summary>
        public ToolScanStatus ClaudeStatus { get; set; } = ToolScanStatus.NotUsed;

        /// <summary>Codex 履歴の読み取り状態。</summary>
        public ToolScanStatus CodexStatus { get; set; } = ToolScanStatus.NotUsed;

        /// <summary>今回パースしたファイル数（キャッシュ流用を除く）。</summary>
        public int ParsedFileCount { get; set; }

        /// <summary>キャッシュから流用したファイル数。</summary>
        public int ReusedFileCount { get; set; }

        /// <summary>ワークフローの journal.jsonl の件数。トークンは持たないが存在は数える（設計書 4.3）。</summary>
        public int JournalFileCount { get; set; }

        /// <summary>走査に要した時間（ミリ秒）。Q6 の計測値。</summary>
        public long ElapsedMs { get; set; }

        /// <summary>走査した Claude Code の履歴ルート。データ品質タブに出す。</summary>
        public string ClaudeRootPath { get; set; } = string.Empty;

        /// <summary>走査した Codex の履歴ルート。データ品質タブに出す。</summary>
        public string CodexRootPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 履歴ファイルの走査・差分判定・ストリーム読みを担当する（設計書 3.4 の [1]〜[3]）。
    /// 1 行を構造体へ変換する純関数は Services/Parsing のパーサに置き、本クラスは I/O と集約を持つ。
    /// </summary>
    public class SessionStatsScanner
    {
        /// <summary>Codex の履歴ルートを差し替える環境変数名。</summary>
        private const string CodexHomeVariable = "CODEX_HOME";

        /// <summary>このインスタンスが走査する Claude Code の履歴ルート。</summary>
        private readonly string _claudeRoot;

        /// <summary>このインスタンスが走査する Codex の履歴ルート。</summary>
        private readonly string _codexRoot;

        /// <summary>実機の履歴ルートで初期化する。</summary>
        public SessionStatsScanner() : this(ClaudeRoot, CodexRoot)
        {
        }

        /// <summary>走査するルートを指定して初期化する（テスト用）。</summary>
        /// <param name="claudeRoot">Claude Code の履歴ルート</param>
        /// <param name="codexRoot">Codex の履歴ルート</param>
        public SessionStatsScanner(string claudeRoot, string codexRoot)
        {
            _claudeRoot = claudeRoot;
            _codexRoot = codexRoot;
        }

        /// <summary>走査対象の Claude Code 履歴ルート。</summary>
        public string ClaudeRootPath => _claudeRoot;

        /// <summary>走査対象の Codex 履歴ルート。</summary>
        public string CodexRootPath => _codexRoot;

        /// <summary>指示文として切り出す最大文字数（設計書 3.5 R1）。</summary>
        private const int PromptMaxLength = 64;

        /// <summary>未分類コマンドとして保持する上限件数（設計書 6 章）。</summary>
        private const int UnknownCommandsLimit = 20;

        /// <summary>Claude Code の履歴ルート（%USERPROFILE%\.claude\projects）。</summary>
        public static string ClaudeRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        /// <summary>
        /// Codex の履歴ルート。CODEX_HOME が設定されていればそちらを優先する（設計書 8.6）。
        /// </summary>
        public static string CodexRoot
        {
            get
            {
                var home = Environment.GetEnvironmentVariable(CodexHomeVariable);
                if (!string.IsNullOrWhiteSpace(home))
                {
                    return Path.Combine(home, "sessions");
                }
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            }
        }

        /// <summary>
        /// 両ツールの履歴を走査し、セッションレコードを作る。
        /// 変化のないファイルは既存キャッシュのレコードを流用する（設計書 3.4 の [2]）。
        /// </summary>
        /// <param name="cache">前回のキャッシュ。null なら全件パースする</param>
        /// <param name="includePrompt">指示文の先頭 64 文字を保持するか（S11）</param>
        /// <param name="progress">進捗通知（処理済みファイル数 / 総数）</param>
        /// <param name="cancellationToken">キャンセル用トークン</param>
        /// <returns>走査結果</returns>
        public ScanResult Scan(
            SessionStatsCache? cache,
            bool includePrompt,
            IProgress<(int Done, int Total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new ScanResult
            {
                ClaudeRootPath = _claudeRoot,
                CodexRootPath = _codexRoot,
            };

            // スキーマ版が違えば流用しない（設計書 6 章）
            var reusable = cache is not null && cache.SchemaVersion == SessionStatsCache.CurrentSchemaVersion
                ? BuildReuseIndex(cache)
                : new Dictionary<string, (ScannedHistoryFile File, SessionStatsRecord Session)>(
                    StringComparer.OrdinalIgnoreCase);

            var claudeFiles = EnumerateFiles(_claudeRoot, "*.jsonl", out var claudeStatus);
            var codexFiles = EnumerateFiles(_codexRoot, "rollout-*.jsonl", out var codexStatus);
            result.ClaudeStatus = claudeStatus;
            result.CodexStatus = codexStatus;

            var total = claudeFiles.Count + codexFiles.Count;
            var done = 0;
            // 同じキーのセッションを取り違えないための索引。
            // 実運用では衝突しないが、衝突しても後勝ちで消さないために持つ
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in claudeFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(path).Equals("journal.jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    // ワークフローの進行記録。トークンを持たないが存在は数える（設計書 4.3）
                    result.JournalFileCount++;
                    progress?.Report((++done, total));
                    continue;
                }
                ProcessFile(path, SessionStatsRecord.ToolClaudeCode, includePrompt, reusable, result, seenKeys);
                progress?.Report((++done, total));
            }

            foreach (var path in codexFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessFile(path, SessionStatsRecord.ToolCodex, includePrompt, reusable, result, seenKeys);
                progress?.Report((++done, total));
            }

            // 削除された履歴ファイルの分も過去分として残す（設計書 6 章）
            if (cache is not null && cache.SchemaVersion == SessionStatsCache.CurrentSchemaVersion)
            {
                var known = new HashSet<string>(result.Sessions.Select(s => s.Key), StringComparer.Ordinal);
                foreach (var old in cache.Sessions)
                {
                    if (known.Add(old.Key))
                    {
                        result.Sessions.Add(old);
                    }
                }
            }

            SessionNormalizer.ResolveParentLinks(result.Sessions);
            SessionNormalizer.UnifyProjectDisplayNames(result.Sessions);

            stopwatch.Stop();
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        /// <summary>キャッシュからパスをキーにした流用索引を作る。</summary>
        /// <param name="cache">前回のキャッシュ</param>
        private static Dictionary<string, (ScannedHistoryFile File, SessionStatsRecord Session)>
            BuildReuseIndex(SessionStatsCache cache)
        {
            var sessions = new Dictionary<string, SessionStatsRecord>(StringComparer.Ordinal);
            foreach (var s in cache.Sessions)
            {
                sessions[s.Key] = s;
            }

            var index = new Dictionary<string, (ScannedHistoryFile, SessionStatsRecord)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var f in cache.Files)
            {
                if (f.SessionKey is null || !sessions.TryGetValue(f.SessionKey, out var session))
                {
                    continue;
                }
                index[f.Path] = (f, session);
            }
            return index;
        }

        /// <summary>
        /// ディレクトリを再帰的に列挙する。存在しなければ未使用、
        /// 存在するが読めなければ読み取り不可として区別する（設計書 8.6）。
        /// </summary>
        /// <param name="root">走査するルート</param>
        /// <param name="pattern">ファイル名パターン</param>
        /// <param name="status">読み取り状態の出力</param>
        private static List<string> EnumerateFiles(string root, string pattern, out ToolScanStatus status)
        {
            if (!Directory.Exists(root))
            {
                status = ToolScanStatus.NotUsed;
                return new List<string>();
            }

            try
            {
                var files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).ToList();
                status = ToolScanStatus.Ok;
                return files;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                status = ToolScanStatus.Unreadable;
                return new List<string>();
            }
        }

        /// <summary>
        /// 履歴ファイル 1 件を処理する。差分が無ければキャッシュを流用し、
        /// あればパースする。読み取り後に更新されていたらキャッシュへ登録しない（設計書 3.4）。
        /// </summary>
        /// <param name="path">履歴ファイルのパス</param>
        /// <param name="tool">ツール識別子</param>
        /// <param name="includePrompt">指示文を保持するか</param>
        /// <param name="reusable">流用索引</param>
        /// <param name="result">結果の出力先</param>
        /// <param name="seenKeys">すでに採用したセッションキー</param>
        private void ProcessFile(
            string path,
            string tool,
            bool includePrompt,
            IReadOnlyDictionary<string, (ScannedHistoryFile File, SessionStatsRecord Session)> reusable,
            ScanResult result,
            HashSet<string> seenKeys)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                result.Files.Add(new ScannedHistoryFile
                {
                    Path = path,
                    SkipReason = $"ファイル情報を取得できません: {ex.Message}",
                });
                return;
            }

            var sizeBefore = info.Length;
            var mtimeBefore = info.LastWriteTimeUtc;

            // 差分判定: サイズと最終更新時刻が一致していれば前回のレコードを流用する
            if (reusable.TryGetValue(path, out var cached) &&
                cached.File.SizeBytes == sizeBefore &&
                cached.File.MtimeUtc == mtimeBefore)
            {
                if (seenKeys.Add(cached.Session.Key))
                {
                    result.Sessions.Add(cached.Session);
                }
                result.Files.Add(cached.File);
                result.ReusedFileCount++;
                return;
            }

            SessionStatsRecord? record;
            try
            {
                record = tool == SessionStatsRecord.ToolClaudeCode
                    ? ParseClaudeFile(path, includePrompt)
                    : ParseCodexFile(path, includePrompt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Files.Add(new ScannedHistoryFile
                {
                    Path = path,
                    SizeBytes = sizeBefore,
                    MtimeUtc = mtimeBefore,
                    SkipReason = $"読み取りに失敗しました: {ex.Message}",
                });
                return;
            }

            result.ParsedFileCount++;

            if (record is null)
            {
                result.Files.Add(new ScannedHistoryFile
                {
                    Path = path,
                    SizeBytes = sizeBefore,
                    MtimeUtc = mtimeBefore,
                    SkipReason = "集計対象の記録がありません",
                });
                return;
            }

            if (!seenKeys.Add(record.Key))
            {
                // 別ファイルが同じキーを主張した場合。実運用では起きないが、
                // 後勝ちで消すと消費が落ちるため、ファイル名を足して別セッションとして残す
                record.Key = $"{record.Key}@{Path.GetFileNameWithoutExtension(path)}";
                seenKeys.Add(record.Key);
            }
            result.Sessions.Add(record);

            // 読み取り中に追記されていないかを確認する。変わっていたらキャッシュに登録せず、
            // 次回スキャンで読み直す（登録すると追記分が恒久的に欠落する）
            try
            {
                info.Refresh();
                if (info.Length != sizeBefore || info.LastWriteTimeUtc != mtimeBefore)
                {
                    return;
                }
            }
            catch (IOException)
            {
                return;
            }

            result.Files.Add(new ScannedHistoryFile
            {
                Path = path,
                SizeBytes = sizeBefore,
                MtimeUtc = mtimeBefore,
                SessionKey = record.Key,
            });
        }

        /// <summary>履歴ファイルを共有読みで開く。各ツールの動作を壊さないため読み取り専用（R3）。</summary>
        /// <param name="path">ファイルパス</param>
        private static StreamReader OpenRead(string path)
            => new(new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, FileOptions.SequentialScan));

        // ────────────────────────────────────────────────────────────────
        // Claude Code
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Claude Code の履歴ファイル 1 件をセッションレコードへ変換する。
        /// 重複排除は (requestId, message.id) の組で行い、鍵の無い行は必ず加算する（設計書 5.1）。
        /// </summary>
        /// <param name="path">履歴ファイルのパス</param>
        /// <param name="includePrompt">指示文を保持するか</param>
        /// <returns>セッションレコード。集計対象が無ければ null</returns>
        internal static SessionStatsRecord? ParseClaudeFile(string path, bool includePrompt)
        {
            var builder = new SessionBuilder(SessionStatsRecord.ToolClaudeCode);
            var seen = new HashSet<(string, string)>();
            string? sessionId = null;
            string? cwd = null;
            var isSidechain = false;
            var hasAnyLine = false;
            string? lastLine = null;

            using (var reader = OpenRead(path))
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    lastLine = line;
                    var parsed = ClaudeCodeHistoryParser.ParseLine(line);
                    if (parsed is null)
                    {
                        builder.ParseErrors++;
                        continue;
                    }
                    hasAnyLine = true;

                    sessionId ??= parsed.SessionId;
                    cwd ??= parsed.Cwd;
                    isSidechain |= parsed.IsSidechain;
                    builder.ObserveTimestamp(parsed.TimestampUtc);

                    switch (parsed.Type)
                    {
                        case "user":
                            builder.BeginTurn(parsed.TimestampUtc);
                            if (includePrompt && builder.FirstPrompt is null)
                            {
                                builder.FirstPrompt = SessionNormalizer.TrimPrompt(
                                    parsed.UserText, PromptMaxLength);
                            }
                            break;

                        case "assistant":
                            if (parsed.HasUsage)
                            {
                                builder.Dedup.RawRecords++;
                                var dedupKey = (parsed.RequestId ?? string.Empty,
                                                parsed.MessageId ?? string.Empty);
                                var keyless = parsed.RequestId is null;
                                if (keyless)
                                {
                                    // 鍵を持たない行は「一意」として必ず加算する（設計書 5.1）
                                    builder.Dedup.KeylessRecords++;
                                }
                                if (keyless || seen.Add(dedupKey))
                                {
                                    builder.Dedup.AcceptedRecords++;
                                    builder.AddTokens(
                                        parsed.Model ?? SessionNormalizer.UnknownModel,
                                        parsed.TimestampUtc,
                                        parsed.InputTokens,
                                        parsed.OutputTokens,
                                        parsed.CacheCreationTokens,
                                        parsed.CacheReadTokens,
                                        reasoning: 0);
                                }
                            }
                            foreach (var use in parsed.ToolUses)
                            {
                                builder.CountTool(use.Name);
                                var phase = PhaseClassifier.ClassifyClaudeTool(use.Name, use.Command);
                                builder.CountPhase(phase);
                                if (phase == PhaseClassifier.Unknown && use.Command is not null)
                                {
                                    builder.CountUnknownCommand(
                                        PhaseClassifier.ExtractCommandName(use.Command));
                                }
                            }
                            break;
                    }
                }
            }

            if (!hasAnyLine)
            {
                return null;
            }

            // 末尾行が壊れていないかを確認する（設計書 8.6）
            if (lastLine is not null && ClaudeCodeHistoryParser.ParseLine(lastLine) is null)
            {
                builder.LastLineBroken = true;
            }

            var (key, parentKey, agentLabel) = BuildClaudeKeys(path, sessionId);
            var record = builder.Build(key, sessionId ?? Path.GetFileNameWithoutExtension(path), cwd);
            record.ParentKey = parentKey;
            record.IsSubagent = parentKey is not null || isSidechain;
            record.AgentLabel = agentLabel;
            record.RootId = sessionId;
            return record;
        }

        /// <summary>
        /// Claude Code のファイルパスからセッションキー・親キー・表示名を決める。
        ///
        /// サブエージェントの履歴は「セッションUUID/subagents/…」に置かれ、
        /// 中の sessionId は親と同一である（実測 200/200 で一致）。
        /// このため sessionId だけを鍵にすると親と全サブエージェントが 1 件へ潰れる。
        /// サブエージェントはパスを鍵へ含めて区別する。
        /// </summary>
        /// <param name="path">履歴ファイルのパス</param>
        /// <param name="sessionId">行から読んだ sessionId</param>
        private static (string Key, string? ParentKey, string? AgentLabel) BuildClaudeKeys(
            string path, string? sessionId)
        {
            var id = sessionId ?? Path.GetFileNameWithoutExtension(path);
            var normalized = path.Replace('\\', '/');
            var marker = normalized.IndexOf("/subagents/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                return ($"claude:{id}", null, null);
            }

            var relative = normalized[(marker + "/subagents/".Length)..];
            if (relative.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative[..^6];
            }
            return ($"claude:{id}#{relative}", $"claude:{id}", relative);
        }

        // ────────────────────────────────────────────────────────────────
        // Codex
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Codex の rollout ファイル 1 件をセッションレコードへ変換する。
        ///
        /// トークンは total_token_usage の最終値を採る（累積値のため行ごとに加算しない）。
        /// 一意化の鍵は session_meta.id（スレッド固有 ID）であり、
        /// session_id（会話ツリーのルート）ではない。B0/Q2 の実測で、
        /// session_id を鍵にするとサブエージェント分（全体の 75%）が落ちることを確認した。
        /// </summary>
        /// <param name="path">rollout ファイルのパス</param>
        /// <param name="includePrompt">指示文を保持するか</param>
        /// <returns>セッションレコード。集計対象が無ければ null</returns>
        internal static SessionStatsRecord? ParseCodexFile(string path, bool includePrompt)
        {
            var builder = new SessionBuilder(SessionStatsRecord.ToolCodex);
            string? sessionId = null, threadId = null, parentThreadId = null;
            string? cwd = null, agentNickname = null, threadSource = null;
            var currentModel = SessionNormalizer.UnknownModel;
            long totalInput = 0, totalOutput = 0, totalCached = 0, totalCacheWrite = 0, totalReasoning = 0;
            var hasAnyLine = false;
            string? lastLine = null;

            using (var reader = OpenRead(path))
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    lastLine = line;
                    var parsed = CodexRolloutParser.ParseLine(line);
                    if (parsed is null)
                    {
                        builder.ParseErrors++;
                        continue;
                    }
                    hasAnyLine = true;
                    builder.ObserveTimestamp(parsed.TimestampUtc);

                    switch (parsed.Kind)
                    {
                        case CodexLineKind.SessionMeta:
                            sessionId      ??= parsed.SessionId;
                            threadId       ??= parsed.ThreadId;
                            parentThreadId ??= parsed.ParentThreadId ?? parsed.ForkedFromId;
                            cwd            ??= parsed.Cwd;
                            agentNickname  ??= parsed.AgentNickname;
                            threadSource   ??= parsed.ThreadSource;
                            break;

                        case CodexLineKind.TurnContext:
                            if (!string.IsNullOrEmpty(parsed.Model))
                            {
                                currentModel = parsed.Model;
                            }
                            cwd ??= parsed.Cwd;
                            break;

                        case CodexLineKind.UserMessage:
                            builder.BeginTurn(parsed.TimestampUtc);
                            if (includePrompt && builder.FirstPrompt is null)
                            {
                                builder.FirstPrompt = SessionNormalizer.TrimPrompt(
                                    parsed.UserText, PromptMaxLength);
                            }
                            break;

                        case CodexLineKind.TokenCount:
                            builder.Dedup.RawRecords++;
                            // total_* は累積値。最終行の値だけを採る（設計書 4.2）
                            totalInput      = parsed.TotalInputTokens;
                            totalOutput     = parsed.TotalOutputTokens;
                            totalCached     = parsed.TotalCachedInputTokens;
                            totalCacheWrite = parsed.TotalCacheWriteTokens;
                            totalReasoning  = parsed.TotalReasoningTokens;
                            // ターン差分は日別・モデル別の按分比率として溜める
                            builder.AddTurnWeight(
                                currentModel,
                                parsed.TimestampUtc,
                                parsed.LastTurnInputTokens,
                                parsed.LastTurnOutputTokens,
                                parsed.LastTurnCachedInputTokens,
                                parsed.LastTurnReasoningTokens);
                            break;

                        case CodexLineKind.CustomToolCall:
                            if (parsed.IsPatchApply)
                            {
                                // パッチ適用は実装として確定できる（設計書 7.3.2）
                                builder.CountTool("apply_patch");
                                builder.CountPhase(PhaseClassifier.Implement);
                                break;
                            }
                            builder.CountTool(parsed.ToolName ?? "exec");
                            var phase = PhaseClassifier.ClassifyCommand(parsed.ShellCommand);
                            builder.CountPhase(phase);
                            if (phase == PhaseClassifier.Unknown)
                            {
                                builder.CountUnknownCommand(
                                    PhaseClassifier.ExtractCommandName(parsed.ShellCommand));
                            }
                            break;

                        case CodexLineKind.PatchApplyEnd:
                            // 結果イベント。要求側（custom_tool_call）で既に実装として数えているため、
                            // ここで数えると 1 回のパッチ適用が 2 回の実行になる。
                            // 回数の基準を「要求」に揃えるため、フェーズには計上しない。
                            break;

                        case CodexLineKind.McpToolCallEnd:
                            builder.CountTool("mcp_tool_call");
                            builder.CountPhase(PhaseClassifier.Unknown);
                            break;

                        case CodexLineKind.ContextCompacted:
                            builder.CountTool("context_compacted");
                            break;
                    }
                }
            }

            if (!hasAnyLine)
            {
                return null;
            }

            if (lastLine is not null && CodexRolloutParser.ParseLine(lastLine) is null)
            {
                builder.LastLineBroken = true;
            }

            builder.Dedup.AcceptedRecords = builder.Dedup.RawRecords > 0 ? 1 : 0;
            builder.SetCodexTotals(totalInput, totalOutput, totalCached, totalCacheWrite, totalReasoning);

            var id = threadId ?? Path.GetFileNameWithoutExtension(path);
            var record = builder.Build($"codex:{id}", id, cwd);
            record.RootId = sessionId;
            record.ParentKey = string.IsNullOrEmpty(parentThreadId) ? null : $"codex:{parentThreadId}";
            record.IsSubagent = string.Equals(threadSource, "subagent", StringComparison.OrdinalIgnoreCase);
            record.AgentLabel = agentNickname;
            return record;
        }

        // ────────────────────────────────────────────────────────────────
        // セッション組み立て
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 1 ファイル分の読み取り途中の状態を保持し、最後にレコードへ畳む内部ビルダ。
        /// Claude Code は行ごとの実額を積み、Codex は累積値を按分比率で割り振る。
        /// </summary>
        private sealed class SessionBuilder
        {
            /// <summary>ツール識別子。</summary>
            private readonly string _tool;

            /// <summary>観測した時刻の最小値。</summary>
            private DateTime? _minUtc;

            /// <summary>観測した時刻の最大値。</summary>
            private DateTime? _maxUtc;

            /// <summary>観測した相異なる時刻の数（2 未満なら経過時間を測定不可とする）。</summary>
            private readonly HashSet<DateTime> _distinctTimestamps = new();

            /// <summary>現在のターンが属するローカル日付。</summary>
            private string? _currentDay;

            /// <summary>モデル別の実額（Claude Code）または按分比率（Codex）。</summary>
            private readonly Dictionary<string, TokenTotals> _byModel = new(StringComparer.Ordinal);

            /// <summary>モデル別のターン数。</summary>
            private readonly Dictionary<string, int> _modelTurns = new(StringComparer.Ordinal);

            /// <summary>日別の実額（Claude Code）または按分比率（Codex）。</summary>
            private readonly Dictionary<string, TokenTotals> _byDay = new(StringComparer.Ordinal);

            /// <summary>日別のターン数。</summary>
            private readonly Dictionary<string, int> _dayTurns = new(StringComparer.Ordinal);

            /// <summary>セッション全体のトークン。</summary>
            private readonly TokenTotals _totals = new();

            /// <summary>ツール別の実行回数。</summary>
            private readonly Dictionary<string, int> _toolCounts = new(StringComparer.Ordinal);

            /// <summary>フェーズ別の実行回数。</summary>
            private readonly Dictionary<string, int> _phaseCounts = new(StringComparer.Ordinal);

            /// <summary>未分類コマンドの出現回数。</summary>
            private readonly Dictionary<string, int> _unknownCommands = new(StringComparer.Ordinal);

            /// <summary>ターン数。</summary>
            private int _turns;

            /// <summary>Codex の累積値を按分する場合の合計。null なら実額積み上げ。</summary>
            private TokenTotals? _codexTotals;

            /// <summary>コンストラクタ。</summary>
            /// <param name="tool">ツール識別子</param>
            public SessionBuilder(string tool) => _tool = tool;

            /// <summary>JSON として壊れていた行数。</summary>
            public int ParseErrors { get; set; }

            /// <summary>末尾行が壊れていたか。</summary>
            public bool LastLineBroken { get; set; }

            /// <summary>最初の指示文（先頭 64 文字）。</summary>
            public string? FirstPrompt { get; set; }

            /// <summary>重複排除の実績。</summary>
            public DedupStats Dedup { get; } = new();

            /// <summary>時刻を観測し、開始・終了・測定可否の材料にする。</summary>
            /// <param name="utc">行の時刻</param>
            public void ObserveTimestamp(DateTime? utc)
            {
                if (utc is not { } t)
                {
                    return;
                }
                _minUtc = _minUtc is null || t < _minUtc ? t : _minUtc;
                _maxUtc = _maxUtc is null || t > _maxUtc ? t : _maxUtc;
                if (_distinctTimestamps.Count < 4)
                {
                    _distinctTimestamps.Add(t);
                }
            }

            /// <summary>
            /// ターンの開始を記録する。日別集計はこの時刻が属する日へ計上する（設計書 7.4）。
            /// モデル別のターン数は、そのターンで最初に観測したモデルへ 1 回だけ数える。
            /// </summary>
            /// <param name="utc">ターン開始時刻</param>
            public void BeginTurn(DateTime? utc)
            {
                _turns++;
                if (utc is { } t)
                {
                    _currentDay = ToLocalDay(t);
                }
                if (_currentDay is not null)
                {
                    _dayTurns[_currentDay] = _dayTurns.GetValueOrDefault(_currentDay) + 1;
                }
                _turnModelCounted = false;
            }

            /// <summary>現在のターンでモデル別ターン数を数え済みか。</summary>
            private bool _turnModelCounted;

            /// <summary>そのターンで最初に観測したモデルとして 1 回だけ数える。</summary>
            /// <param name="model">モデル名</param>
            private void CountTurnForModel(string model)
            {
                if (_turnModelCounted)
                {
                    return;
                }
                _modelTurns[model] = _modelTurns.GetValueOrDefault(model) + 1;
                _turnModelCounted = true;
            }

            /// <summary>Claude Code の 1 レコード分のトークンを加算する。</summary>
            /// <param name="model">モデル名</param>
            /// <param name="utc">記録時刻</param>
            /// <param name="input">入力トークン</param>
            /// <param name="output">出力トークン</param>
            /// <param name="cacheCreate">キャッシュ作成トークン</param>
            /// <param name="cacheRead">キャッシュ読込トークン</param>
            /// <param name="reasoning">推論トークン</param>
            public void AddTokens(
                string model, DateTime? utc,
                long input, long output, long cacheCreate, long cacheRead, long reasoning)
            {
                var delta = new TokenTotals
                {
                    Input = input,
                    Output = output,
                    CacheCreate = cacheCreate,
                    CacheRead = cacheRead,
                    Reasoning = reasoning,
                };
                _totals.Add(delta);
                Bucket(_byModel, model).Add(delta);
                Bucket(_byDay, _currentDay ?? (utc is { } t ? ToLocalDay(t) : "(不明)")).Add(delta);
                CountTurnForModel(model);
            }

            /// <summary>
            /// Codex のターン差分を按分比率として溜める。
            /// セッション合計は最終行の累積値を使い、日別・モデル別はこの比率で割り振る。
            /// </summary>
            /// <param name="model">そのターンのモデル名</param>
            /// <param name="utc">記録時刻</param>
            /// <param name="input">ターン差分の入力トークン</param>
            /// <param name="output">ターン差分の出力トークン</param>
            /// <param name="cached">ターン差分のキャッシュ読込トークン</param>
            /// <param name="reasoning">ターン差分の推論トークン</param>
            public void AddTurnWeight(
                string model, DateTime? utc, long input, long output, long cached, long reasoning)
            {
                var delta = new TokenTotals
                {
                    Input = input,
                    Output = output,
                    CacheRead = cached,
                    Reasoning = reasoning,
                };
                var day = _currentDay ?? (utc is { } t ? ToLocalDay(t) : "(不明)");
                Bucket(_byModel, model).Add(delta);
                Bucket(_byDay, day).Add(delta);
                CountTurnForModel(model);
            }

            /// <summary>Codex のセッション合計（最終行の累積値）を設定する。</summary>
            /// <param name="input">累積入力トークン</param>
            /// <param name="output">累積出力トークン</param>
            /// <param name="cached">累積キャッシュ読込トークン</param>
            /// <param name="cacheWrite">累積キャッシュ書込トークン</param>
            /// <param name="reasoning">累積推論トークン</param>
            public void SetCodexTotals(
                long input, long output, long cached, long cacheWrite, long reasoning)
                => _codexTotals = new TokenTotals
                {
                    Input = input,
                    Output = output,
                    CacheRead = cached,
                    CacheCreate = cacheWrite,
                    Reasoning = reasoning,
                };

            /// <summary>ツールの実行回数を 1 加算する。</summary>
            /// <param name="name">ツール名</param>
            public void CountTool(string name)
                => _toolCounts[name] = _toolCounts.GetValueOrDefault(name) + 1;

            /// <summary>フェーズの実行回数を 1 加算する。</summary>
            /// <param name="phase">フェーズ定数</param>
            public void CountPhase(string phase)
                => _phaseCounts[phase] = _phaseCounts.GetValueOrDefault(phase) + 1;

            /// <summary>未分類コマンドの出現回数を 1 加算する。</summary>
            /// <param name="name">コマンドの代表名</param>
            public void CountUnknownCommand(string name)
                => _unknownCommands[name] = _unknownCommands.GetValueOrDefault(name) + 1;

            /// <summary>集めた状態をセッションレコードへ畳む。</summary>
            /// <param name="key">一意キー</param>
            /// <param name="sessionId">ツール側のセッション ID</param>
            /// <param name="cwd">作業ディレクトリ</param>
            public SessionStatsRecord Build(string key, string sessionId, string? cwd)
            {
                var totals = _codexTotals ?? _totals;
                if (_codexTotals is not null)
                {
                    // Codex は累積値が真であり、ターン差分の合算とは一致しないことがある。
                    // 日別・モデル別は差分を比率として使い、合計が累積値に一致するよう割り振る。
                    Redistribute(_byModel, _codexTotals);
                    Redistribute(_byDay, _codexTotals);
                }

                var start = _minUtc ?? DateTime.UnixEpoch;
                var end = _maxUtc ?? start;
                var measurable = _distinctTimestamps.Count >= 2;

                return new SessionStatsRecord
                {
                    Key = key,
                    Tool = _tool,
                    SessionId = sessionId,
                    ProjectKey = SessionNormalizer.NormalizeProjectKey(cwd),
                    ProjectDisplay = SessionNormalizer.NormalizeProjectDisplay(cwd),
                    StartUtc = start,
                    EndUtc = end,
                    ElapsedSec = measurable ? (long)(end - start).TotalSeconds : 0,
                    ElapsedMeasurable = measurable,
                    FirstPrompt = FirstPrompt,
                    Turns = _turns,
                    Tokens = totals,
                    ByModel = _byModel
                        .Where(kv => !IsEmpty(kv.Value))
                        .OrderByDescending(kv => kv.Value.Output + kv.Value.Input)
                        .Select(kv => new ModelBreakdown
                        {
                            Model = kv.Key,
                            Turns = _modelTurns.GetValueOrDefault(kv.Key),
                            Tokens = kv.Value,
                        })
                        .ToList(),
                    ByDay = _byDay
                        .Where(kv => !IsEmpty(kv.Value))
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => new DayBreakdown
                        {
                            DateLocal = kv.Key,
                            Turns = _dayTurns.GetValueOrDefault(kv.Key),
                            Tokens = kv.Value,
                        })
                        .ToList(),
                    ToolCounts = _toolCounts,
                    PhaseCounts = _phaseCounts,
                    UnknownCommands = _unknownCommands
                        .OrderByDescending(kv => kv.Value)
                        .Take(UnknownCommandsLimit)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                    Dedup = Dedup,
                    ParseErrors = ParseErrors,
                    LastLineBroken = LastLineBroken,
                };
            }

            /// <summary>
            /// 内訳の各枠を、現在の値を比率として合計へ割り振り直す。
            /// 端数は最大の枠へ寄せ、内訳の合計が必ず合計値と一致するようにする（T7 / T15）。
            /// </summary>
            /// <param name="buckets">割り振る対象の内訳</param>
            /// <param name="totals">一致させる合計値</param>
            private static void Redistribute(Dictionary<string, TokenTotals> buckets, TokenTotals totals)
            {
                if (buckets.Count == 0)
                {
                    if (!IsEmpty(totals))
                    {
                        buckets["(不明)"] = Clone(totals);
                    }
                    return;
                }
                if (buckets.Count == 1)
                {
                    buckets[buckets.Keys.First()] = Clone(totals);
                    return;
                }

                var keys = buckets.Keys.ToList();
                RedistributeField(buckets, keys, totals.Input, (t, v) => t.Input = v, t => t.Input);
                RedistributeField(buckets, keys, totals.Output, (t, v) => t.Output = v, t => t.Output);
                RedistributeField(buckets, keys, totals.CacheRead, (t, v) => t.CacheRead = v, t => t.CacheRead);
                RedistributeField(buckets, keys, totals.CacheCreate, (t, v) => t.CacheCreate = v, t => t.CacheCreate);
                RedistributeField(buckets, keys, totals.Reasoning, (t, v) => t.Reasoning = v, t => t.Reasoning);
            }

            /// <summary>1 つの指標について比率按分を行い、端数を最大の枠へ寄せる。</summary>
            /// <param name="buckets">割り振る対象</param>
            /// <param name="keys">枠のキー一覧</param>
            /// <param name="total">割り振る合計値</param>
            /// <param name="setter">枠へ値を書く関数</param>
            /// <param name="getter">枠から現在の比率値を読む関数</param>
            private static void RedistributeField(
                Dictionary<string, TokenTotals> buckets,
                List<string> keys,
                long total,
                Action<TokenTotals, long> setter,
                Func<TokenTotals, long> getter)
            {
                var weights = keys.Select(k => getter(buckets[k])).ToList();
                var weightSum = weights.Sum();

                if (total == 0 || weightSum <= 0)
                {
                    // 比率が取れない場合は最初の枠へ全額を置く（落とさない）
                    foreach (var k in keys)
                    {
                        setter(buckets[k], 0);
                    }
                    if (total != 0)
                    {
                        setter(buckets[keys[0]], total);
                    }
                    return;
                }

                long assigned = 0;
                var maxIndex = 0;
                for (var i = 0; i < keys.Count; i++)
                {
                    if (weights[i] > weights[maxIndex])
                    {
                        maxIndex = i;
                    }
                    var share = (long)((double)total * weights[i] / weightSum);
                    setter(buckets[keys[i]], share);
                    assigned += share;
                }
                // 端数は最大の枠へ寄せる
                var remainder = total - assigned;
                if (remainder != 0)
                {
                    var t = buckets[keys[maxIndex]];
                    setter(t, getter(t) + remainder);
                }
            }

            /// <summary>指定キーの枠を取り出す。無ければ作る。</summary>
            /// <param name="map">対象の辞書</param>
            /// <param name="key">キー</param>
            private static TokenTotals Bucket(Dictionary<string, TokenTotals> map, string key)
            {
                if (!map.TryGetValue(key, out var t))
                {
                    t = new TokenTotals();
                    map[key] = t;
                }
                return t;
            }

            /// <summary>すべての指標が 0 かどうか。</summary>
            /// <param name="t">判定対象</param>
            private static bool IsEmpty(TokenTotals t)
                => t.Input == 0 && t.Output == 0 && t.CacheCreate == 0 &&
                   t.CacheRead == 0 && t.Reasoning == 0;

            /// <summary>内訳を複製する。</summary>
            /// <param name="t">複製元</param>
            private static TokenTotals Clone(TokenTotals t)
                => new()
                {
                    Input = t.Input,
                    Output = t.Output,
                    CacheCreate = t.CacheCreate,
                    CacheRead = t.CacheRead,
                    Reasoning = t.Reasoning,
                };

            /// <summary>UTC の時刻をローカル日付（yyyy-MM-dd）へ変換する（設計書 7.4）。</summary>
            /// <param name="utc">UTC の時刻</param>
            private static string ToLocalDay(DateTime utc)
                => utc.ToLocalTime().ToString("yyyy-MM-dd");
        }
    }
}
