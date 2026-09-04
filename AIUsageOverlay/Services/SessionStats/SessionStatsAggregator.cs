using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>レポート全体。HTML へ埋め込む JSON の最上位（設計書 3.2）。</summary>
    public class UsageReport
    {
        /// <summary>生成時刻（ローカル、表示用の文字列）。</summary>
        public string GeneratedAt { get; set; } = string.Empty;

        /// <summary>生成したアプリのバージョン。</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>指示文を出力しているか（S11）。false のとき画面は説明を出す。</summary>
        public bool IncludePrompt { get; set; }

        /// <summary>データ品質の情報（設計書 8.5）。</summary>
        public QualityInfo Quality { get; set; } = new();

        /// <summary>セッション一覧。期間による絞り込みは periods 側のキー一覧で行う。</summary>
        public List<SessionView> Sessions { get; set; } = new();

        /// <summary>期間ごとの集計。キーは "7" / "30" / "all"。</summary>
        public Dictionary<string, PeriodView> Periods { get; set; } = new();

        /// <summary>
        /// Cursor の月別トークン累積（cursor-tokens.json 由来）。
        /// キーは "yyyy-MM"。ローカル履歴にはトークン情報が無いため
        /// ダッシュボード API 傍受で取得した値を別途注入する。
        /// </summary>
        public Dictionary<string, Models.CursorMonthlyTokens> CursorMonthly { get; set; } = new();
    }

    /// <summary>セッション一覧の 1 行。</summary>
    public class SessionView
    {
        /// <summary>一意キー。</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>ツール識別子。</summary>
        public string Tool { get; set; } = string.Empty;

        /// <summary>親セッションのキー。無ければ null。</summary>
        public string? ParentKey { get; set; }

        /// <summary>サブエージェントかどうか。</summary>
        public bool IsSubagent { get; set; }

        /// <summary>表示用のエージェント名。</summary>
        public string? AgentLabel { get; set; }

        /// <summary>プロジェクトの表示名。</summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>開始時刻（ローカル、表示用）。</summary>
        public string Start { get; set; } = string.Empty;

        /// <summary>開始日（yyyy-MM-dd、絞り込み用）。</summary>
        public string StartDay { get; set; } = string.Empty;

        /// <summary>経過時間（秒）。測定不可なら 0。</summary>
        public long ElapsedSec { get; set; }

        /// <summary>経過時間を測定できたか。false なら「測定不可」バッジを出す。</summary>
        public bool ElapsedMeasurable { get; set; }

        /// <summary>末尾行が壊れていたか。true なら過小計上の可能性バッジを出す。</summary>
        public bool LastLineBroken { get; set; }

        /// <summary>最初の指示文（先頭 64 文字）。出力しない設定なら null。</summary>
        public string? Prompt { get; set; }

        /// <summary>ターン数。</summary>
        public int Turns { get; set; }

        /// <summary>主なモデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>課金相当トークン（単独値）。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークン（単独値）。</summary>
        public long Processed { get; set; }

        /// <summary>課金相当トークン（自身 + 配下の全サブエージェント）。</summary>
        public long BillableCumulative { get; set; }

        /// <summary>総処理トークン（自身 + 配下の全サブエージェント）。</summary>
        public long ProcessedCumulative { get; set; }

        /// <summary>配下のサブエージェント数（間接を含む）。</summary>
        public int SubagentCount { get; set; }

        /// <summary>フェーズ別の実行回数。</summary>
        public Dictionary<string, int> Phases { get; set; } = new();

        // ── 明細（行を開いたときに出す）──────────────────────────

        /// <summary>終了時刻（ローカル、表示用）。</summary>
        public string End { get; set; } = string.Empty;

        /// <summary>会話ツリーのルート ID。同じツリーの兄弟を辿る手掛かり。</summary>
        public string? RootId { get; set; }

        /// <summary>トークンの内訳。どこに量が寄っているかを見るために持つ。</summary>
        public TokenDetailView Tokens { get; set; } = new();

        /// <summary>モデル別の内訳。セッション内でモデルが変わる場合に効く。</summary>
        public List<SessionModelView> ByModel { get; set; } = new();

        /// <summary>日別の内訳。日をまたいだセッションで 2 件以上になる。</summary>
        public List<SessionDayView> ByDay { get; set; } = new();

        /// <summary>ツール別の実行回数（多い順）。</summary>
        public List<CountView> Tools { get; set; } = new();

        /// <summary>判別できなかったコマンド（多い順・上位のみ）。</summary>
        public List<CountView> UnknownCommands { get; set; } = new();

        /// <summary>読み取った課金レコード数（重複排除前）。</summary>
        public int RawRecords { get; set; }

        /// <summary>採用した課金レコード数（重複排除後）。</summary>
        public int AcceptedRecords { get; set; }

        /// <summary>鍵が無く無条件に加算した行数。</summary>
        public int KeylessRecords { get; set; }

        /// <summary>JSON として壊れていた行数。</summary>
        public int ParseErrors { get; set; }
    }

    /// <summary>トークンの内訳。画面では「どこに量が寄っているか」を見るために使う。</summary>
    public class TokenDetailView
    {
        /// <summary>入力トークン。</summary>
        public long Input { get; set; }

        /// <summary>出力トークン。</summary>
        public long Output { get; set; }

        /// <summary>キャッシュ作成トークン。</summary>
        public long CacheCreate { get; set; }

        /// <summary>キャッシュ読込トークン。</summary>
        public long CacheRead { get; set; }

        /// <summary>推論トークン（Codex のみ。出力の内数）。</summary>
        public long Reasoning { get; set; }
    }

    /// <summary>セッション明細のモデル別 1 行。</summary>
    public class SessionModelView
    {
        /// <summary>モデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>そのモデルで処理したターン数。</summary>
        public int Turns { get; set; }

        /// <summary>消費トークン。</summary>
        public long Billable { get; set; }

        /// <summary>処理トークン。</summary>
        public long Processed { get; set; }
    }

    /// <summary>セッション明細の日別 1 行。</summary>
    public class SessionDayView
    {
        /// <summary>ローカル日付。</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>その日のターン数。</summary>
        public int Turns { get; set; }

        /// <summary>その日の消費トークン。</summary>
        public long Billable { get; set; }
    }

    /// <summary>期間 1 つ分の集計結果。</summary>
    public class PeriodView
    {
        /// <summary>画面に出す期間名。</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>集計に含めたセッション数。</summary>
        public int SessionCount { get; set; }

        /// <summary>集計に含めたターン数。</summary>
        public int TurnCount { get; set; }

        /// <summary>プロジェクト数。</summary>
        public int ProjectCount { get; set; }

        /// <summary>課金相当トークンの合計（セッション基準）。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークンの合計（セッション基準）。</summary>
        public long Processed { get; set; }

        /// <summary>キャッシュ読込トークンの合計。</summary>
        public long CacheRead { get; set; }

        /// <summary>推論トークンの合計（Codex のみ）。</summary>
        public long Reasoning { get; set; }

        /// <summary>経過時間の合計（秒）。測定可能なセッションのみ。作業時間ではない。</summary>
        public long ElapsedSec { get; set; }

        /// <summary>経過時間を測定できなかったセッション数。</summary>
        public int ElapsedUnmeasurableCount { get; set; }

        /// <summary>フェーズ分類率（0〜100）。母数に未分類を含む。</summary>
        public double ClassifiedPercent { get; set; }

        /// <summary>日別の推移（ターン基準）。</summary>
        public List<DayView> ByDay { get; set; } = new();

        /// <summary>プロジェクト別（セッション基準、単独値の総和）。</summary>
        public List<ProjectView> ByProject { get; set; } = new();

        /// <summary>フェーズ別の実行回数。</summary>
        public Dictionary<string, int> ByPhase { get; set; } = new();

        /// <summary>モデル別。</summary>
        public List<ModelView> ByModel { get; set; } = new();

        /// <summary>ツール別の実行回数。</summary>
        public List<CountView> ByTool { get; set; } = new();

        /// <summary>未分類コマンドの上位。次版で語彙表へ足す判断材料（設計書 7.3.4）。</summary>
        public List<CountView> UnknownCommands { get; set; } = new();

        /// <summary>この期間に含まれるセッションのキー一覧。</summary>
        public List<string> SessionKeys { get; set; } = new();
    }

    /// <summary>日別の 1 点。</summary>
    public class DayView
    {
        /// <summary>ローカル日付（yyyy-MM-dd）。</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>ツール別の課金相当トークン。積み上げ棒の各段になる。</summary>
        public long ClaudeBillable { get; set; }

        /// <summary>Codex の課金相当トークン。</summary>
        public long CodexBillable { get; set; }

        /// <summary>Cursor の課金相当トークン。ローカル履歴にトークンが無いため通常は 0。</summary>
        public long CursorBillable { get; set; }

        /// <summary>その日のターン数。</summary>
        public int Turns { get; set; }
    }

    /// <summary>プロジェクト別の 1 行。</summary>
    public class ProjectView
    {
        /// <summary>プロジェクトの表示名（実パス）。</summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>セッション数。</summary>
        public int SessionCount { get; set; }

        /// <summary>課金相当トークン。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークン。</summary>
        public long Processed { get; set; }

        /// <summary>経過時間の合計（秒）。</summary>
        public long ElapsedSec { get; set; }

        /// <summary>フェーズ別の実行回数（構成比バー用）。</summary>
        public Dictionary<string, int> Phases { get; set; } = new();
    }

    /// <summary>モデル別の 1 行。</summary>
    public class ModelView
    {
        /// <summary>モデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>課金相当トークン。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークン。</summary>
        public long Processed { get; set; }

        /// <summary>ターン数。</summary>
        public int Turns { get; set; }
    }

    /// <summary>名前と件数だけの汎用行。</summary>
    public class CountView
    {
        /// <summary>名前。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>件数。</summary>
        public int Count { get; set; }
    }

    /// <summary>データ品質タブに出す情報（設計書 8.5）。</summary>
    public class QualityInfo
    {
        /// <summary>Claude Code 履歴の状態（notUsed / ok / unreadable）。</summary>
        public string ClaudeStatus { get; set; } = string.Empty;

        /// <summary>Codex 履歴の状態。</summary>
        public string CodexStatus { get; set; } = string.Empty;

        /// <summary>Cursor 履歴の状態。</summary>
        public string CursorStatus { get; set; } = string.Empty;

        /// <summary>Claude Code の履歴ルート。</summary>
        public string ClaudeRoot { get; set; } = string.Empty;

        /// <summary>Codex の履歴ルート。</summary>
        public string CodexRoot { get; set; } = string.Empty;

        /// <summary>Cursor の履歴ルート。</summary>
        public string CursorRoot { get; set; } = string.Empty;

        /// <summary>今回パースしたファイル数。</summary>
        public int ParsedFiles { get; set; }

        /// <summary>キャッシュから流用したファイル数。</summary>
        public int ReusedFiles { get; set; }

        /// <summary>読めなかったファイルの一覧（パスと理由）。</summary>
        public List<SkippedFileView> SkippedFiles { get; set; } = new();

        /// <summary>ワークフローの journal.jsonl の件数。</summary>
        public int JournalFiles { get; set; }

        /// <summary>走査に要した時間（ミリ秒）。Q6 の計測値。</summary>
        public long ScanElapsedMs { get; set; }

        /// <summary>Claude Code で重複排除前に読んだ課金レコード数。</summary>
        public int ClaudeRawRecords { get; set; }

        /// <summary>Claude Code で採用した課金レコード数。</summary>
        public int ClaudeAcceptedRecords { get; set; }

        /// <summary>Claude Code で鍵が無く無条件加算した行数。</summary>
        public int ClaudeKeylessRecords { get; set; }

        /// <summary>Codex のスレッド数（= 一意化後のセッション数）。</summary>
        public int CodexThreads { get; set; }

        /// <summary>Codex のサブエージェント数。</summary>
        public int CodexSubagents { get; set; }

        /// <summary>Cursor のセッション数。</summary>
        public int CursorSessions { get; set; }

        /// <summary>Cursor のサブエージェント数。</summary>
        public int CursorSubagents { get; set; }

        /// <summary>JSON として壊れていた行の総数。</summary>
        public int ParseErrors { get; set; }

        /// <summary>末尾行が壊れていたセッション数（静かな過小計上の恐れ）。</summary>
        public int LastLineBrokenSessions { get; set; }

        /// <summary>経過時間を測定できなかったセッション数。</summary>
        public int UnmeasurableSessions { get; set; }

        /// <summary>キャッシュが壊れていて全件再パースしたか。</summary>
        public bool CacheWasCorrupted { get; set; }

        /// <summary>履歴の最古の日付（保持期間の目安。Q3）。</summary>
        public string OldestRecord { get; set; } = string.Empty;

        /// <summary>履歴の最新の日付。</summary>
        public string NewestRecord { get; set; } = string.Empty;
    }

    /// <summary>読めなかったファイル 1 件。</summary>
    public class SkippedFileView
    {
        /// <summary>ファイルパス。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>読めなかった理由。</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// セッションレコードを画面用の集計へ畳む（設計書 7 章）。
    ///
    /// 日別はターン基準、プロジェクト別はセッション基準で切る。
    /// 両者の合計は一致しない。これは意図した差であり、画面にも明記する（設計書 7.4 / C9）。
    /// </summary>
    public static class SessionStatsAggregator
    {
        /// <summary>
        /// 期間セレクタの定義（キー、ラベル、遡る日数。日数 0 は全期間）。
        /// 「今日」は当日分だけを見たいという要求のために置く（既定の表示期間）。
        /// </summary>
        private static readonly (string Key, string Label, int Days)[] Periods =
        {
            ("today", "今日", 1),
            ("7", "直近 7 日", 7),
            ("30", "直近 30 日", 30),
            ("all", "全期間", 0),
        };

        /// <summary>ツール別・未分類コマンドの一覧に載せる上限件数。</summary>
        private const int TopCountLimit = 20;

        /// <summary>
        /// セッション明細に載せるツールの上限件数。
        /// 全セッション分を HTML へ埋め込むため、件数を絞ってサイズを抑える。
        /// </summary>
        private const int SessionToolLimit = 12;

        /// <summary>セッション明細に載せる未分類コマンドの上限件数。</summary>
        private const int SessionUnknownLimit = 5;

        /// <summary>
        /// 走査結果からレポートを組み立てる。
        /// </summary>
        /// <param name="scan">走査結果</param>
        /// <param name="includePrompt">指示文を出力しているか</param>
        /// <param name="appVersion">生成したアプリのバージョン</param>
        /// <param name="cacheWasCorrupted">キャッシュ破損により全件再パースしたか</param>
        /// <param name="nowLocal">現在時刻（ローカル）。期間の起点に使う</param>
        /// <returns>HTML へ埋め込むレポート</returns>
        public static UsageReport Build(
            ScanResult scan,
            bool includePrompt,
            string appVersion,
            bool cacheWasCorrupted,
            DateTime nowLocal)
        {
            var sessions = scan.Sessions;
            var cumulative = BuildCumulative(sessions);

            var report = new UsageReport
            {
                GeneratedAt = nowLocal.ToString("yyyy-MM-dd HH:mm"),
                AppVersion = appVersion,
                IncludePrompt = includePrompt,
                Quality = BuildQuality(scan, cacheWasCorrupted),
                Sessions = sessions
                    .OrderByDescending(s => s.StartUtc)
                    .Select(s => ToView(s, cumulative))
                    .ToList(),
            };

            foreach (var (key, label, days) in Periods)
            {
                var from = days == 0 ? DateTime.MinValue : nowLocal.Date.AddDays(-(days - 1));
                report.Periods[key] = BuildPeriod(label, sessions, from, DateTime.MaxValue);
            }

            AddMonthlyPeriods(report, sessions);
            return report;
        }

        /// <summary>
        /// 履歴に現れる月を新しい順に期間として追加する。
        ///
        /// 履歴は各ツールの保持期間で消えるため、過去の月を後から見返す用途がある。
        /// キーは「month:yyyy-MM」。存在しない月は作らない（空の選択肢を並べない）。
        /// </summary>
        /// <param name="report">追加先のレポート</param>
        /// <param name="sessions">全セッション</param>
        private static void AddMonthlyPeriods(
            UsageReport report, IReadOnlyCollection<SessionStatsRecord> sessions)
        {
            var months = sessions
                .Select(s => s.StartUtc.ToLocalTime())
                .Select(d => new DateTime(d.Year, d.Month, 1))
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            foreach (var month in months)
            {
                var label = $"{month.Year} 年 {month.Month} 月";
                report.Periods[$"month:{month:yyyy-MM}"] =
                    BuildPeriod(label, sessions, month, month.AddMonths(1));
            }
        }

        /// <summary>
        /// 親子を辿って累計値（自身 + 配下の全サブエージェント）を求める（設計書 5.4）。
        /// プロジェクト別の集計には単独値を使う。累計値を足すと二重計上になる。
        /// </summary>
        /// <param name="sessions">対象セッション</param>
        private static Dictionary<string, (long Billable, long Processed, int Count)> BuildCumulative(
            IReadOnlyCollection<SessionStatsRecord> sessions)
        {
            var children = new Dictionary<string, List<SessionStatsRecord>>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                if (string.IsNullOrEmpty(s.ParentKey))
                {
                    continue;
                }
                if (!children.TryGetValue(s.ParentKey, out var list))
                {
                    list = new List<SessionStatsRecord>();
                    children[s.ParentKey] = list;
                }
                list.Add(s);
            }

            var result = new Dictionary<string, (long, long, int)>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                long billable = s.Tokens.Billable(s.Tool);
                long processed = s.Tokens.Processed(s.Tool);
                var count = 0;

                // 幅優先で配下を辿る。循環は SessionNormalizer が事前に断ち切っている
                var queue = new Queue<string>();
                queue.Enqueue(s.Key);
                var visited = new HashSet<string>(StringComparer.Ordinal) { s.Key };
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!children.TryGetValue(current, out var list))
                    {
                        continue;
                    }
                    foreach (var child in list)
                    {
                        if (!visited.Add(child.Key))
                        {
                            continue;
                        }
                        billable += child.Tokens.Billable(child.Tool);
                        processed += child.Tokens.Processed(child.Tool);
                        count++;
                        queue.Enqueue(child.Key);
                    }
                }
                result[s.Key] = (billable, processed, count);
            }
            return result;
        }

        /// <summary>セッションレコードを画面用の行へ変換する。</summary>
        /// <param name="s">セッションレコード</param>
        /// <param name="cumulative">累計値の索引</param>
        private static SessionView ToView(
            SessionStatsRecord s,
            IReadOnlyDictionary<string, (long Billable, long Processed, int Count)> cumulative)
        {
            var local = s.StartUtc.ToLocalTime();
            var cum = cumulative.TryGetValue(s.Key, out var c) ? c : (0L, 0L, 0);
            return new SessionView
            {
                Key = s.Key,
                Tool = s.Tool,
                ParentKey = s.ParentKey,
                IsSubagent = s.IsSubagent,
                AgentLabel = s.AgentLabel,
                Project = s.ProjectDisplay,
                Start = local.ToString("yyyy-MM-dd HH:mm"),
                StartDay = local.ToString("yyyy-MM-dd"),
                ElapsedSec = s.ElapsedSec,
                ElapsedMeasurable = s.ElapsedMeasurable,
                LastLineBroken = s.LastLineBroken,
                Prompt = s.FirstPrompt,
                Turns = s.Turns,
                Model = s.ByModel.Count > 0 ? s.ByModel[0].Model : SessionNormalizer.UnknownModel,
                Billable = s.Tokens.Billable(s.Tool),
                Processed = s.Tokens.Processed(s.Tool),
                BillableCumulative = cum.Item1,
                ProcessedCumulative = cum.Item2,
                SubagentCount = cum.Item3,
                Phases = s.PhaseCounts,

                End = s.EndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                RootId = s.RootId,
                Tokens = new TokenDetailView
                {
                    Input = s.Tokens.Input,
                    Output = s.Tokens.Output,
                    CacheCreate = s.Tokens.CacheCreate,
                    CacheRead = s.Tokens.CacheRead,
                    Reasoning = s.Tokens.Reasoning,
                },
                ByModel = s.ByModel
                    .Select(m => new SessionModelView
                    {
                        Model = m.Model,
                        Turns = m.Turns,
                        Billable = m.Tokens.Billable(s.Tool),
                        Processed = m.Tokens.Processed(s.Tool),
                    })
                    .ToList(),
                ByDay = s.ByDay
                    .Select(d => new SessionDayView
                    {
                        Date = d.DateLocal,
                        Turns = d.Turns,
                        Billable = d.Tokens.Billable(s.Tool),
                    })
                    .ToList(),
                Tools = s.ToolCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(SessionToolLimit)
                    .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                    .ToList(),
                UnknownCommands = s.UnknownCommands
                    .OrderByDescending(kv => kv.Value)
                    .Take(SessionUnknownLimit)
                    .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                    .ToList(),
                RawRecords = s.Dedup.RawRecords,
                AcceptedRecords = s.Dedup.AcceptedRecords,
                KeylessRecords = s.Dedup.KeylessRecords,
                ParseErrors = s.ParseErrors,
            };
        }

        /// <summary>期間 1 つ分を集計する。</summary>
        /// <param name="label">期間名</param>
        /// <param name="sessions">全セッション</param>
        /// <param name="fromLocal">期間の開始日（ローカル）。MinValue なら全期間</param>
        /// <param name="toLocalExclusive">期間の終端（ローカル・この日を含まない）。MaxValue なら上限なし</param>
        private static PeriodView BuildPeriod(
            string label,
            IReadOnlyCollection<SessionStatsRecord> sessions,
            DateTime fromLocal,
            DateTime toLocalExclusive)
        {
            var view = new PeriodView { Label = label };

            // セッション基準: 開始時刻が期間内のもの（設計書 7.4）
            var inPeriod = sessions
                .Where(s =>
                {
                    var day = s.StartUtc.ToLocalTime().Date;
                    return day >= fromLocal.Date &&
                           (toLocalExclusive == DateTime.MaxValue || day < toLocalExclusive.Date);
                })
                .ToList();

            var projects = new Dictionary<string, ProjectView>(StringComparer.Ordinal);
            var models = new Dictionary<string, ModelView>(StringComparer.Ordinal);
            var tools = new Dictionary<string, int>(StringComparer.Ordinal);
            var phases = new Dictionary<string, int>(StringComparer.Ordinal);
            var unknownCommands = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var s in inPeriod)
            {
                var billable = s.Tokens.Billable(s.Tool);
                var processed = s.Tokens.Processed(s.Tool);

                view.SessionCount++;
                view.TurnCount += s.Turns;
                view.Billable += billable;
                view.Processed += processed;
                view.CacheRead += s.Tokens.CacheRead;
                view.Reasoning += s.Tokens.Reasoning;
                view.SessionKeys.Add(s.Key);

                if (s.ElapsedMeasurable)
                {
                    view.ElapsedSec += s.ElapsedSec;
                }
                else
                {
                    view.ElapsedUnmeasurableCount++;
                }

                if (!projects.TryGetValue(s.ProjectKey, out var project))
                {
                    project = new ProjectView { Project = s.ProjectDisplay };
                    projects[s.ProjectKey] = project;
                }
                project.SessionCount++;
                project.Billable += billable;
                project.Processed += processed;
                if (s.ElapsedMeasurable)
                {
                    project.ElapsedSec += s.ElapsedSec;
                }
                Merge(project.Phases, s.PhaseCounts);

                foreach (var m in s.ByModel)
                {
                    if (!models.TryGetValue(m.Model, out var model))
                    {
                        model = new ModelView { Model = m.Model };
                        models[m.Model] = model;
                    }
                    model.Billable += m.Tokens.Billable(s.Tool);
                    model.Processed += m.Tokens.Processed(s.Tool);
                    model.Turns += m.Turns;
                }

                Merge(tools, s.ToolCounts);
                Merge(phases, s.PhaseCounts);
                Merge(unknownCommands, s.UnknownCommands);
            }

            // 日別はターン基準。期間内の日付に属する内訳をすべて拾う（設計書 7.4 / C9）
            var days = new Dictionary<string, DayView>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                foreach (var d in s.ByDay)
                {
                    if (!DateTime.TryParse(d.DateLocal, out var date) || date.Date < fromLocal.Date)
                    {
                        continue;
                    }
                    if (toLocalExclusive != DateTime.MaxValue && date.Date >= toLocalExclusive.Date)
                    {
                        continue;
                    }
                    if (!days.TryGetValue(d.DateLocal, out var day))
                    {
                        day = new DayView { Date = d.DateLocal };
                        days[d.DateLocal] = day;
                    }
                    var billable = d.Tokens.Billable(s.Tool);
                    if (s.Tool == SessionStatsRecord.ToolCodex)
                    {
                        day.CodexBillable += billable;
                    }
                    else if (s.Tool == SessionStatsRecord.ToolCursor)
                    {
                        day.CursorBillable += billable;
                    }
                    else
                    {
                        day.ClaudeBillable += billable;
                    }
                    day.Turns += d.Turns;
                }
            }

            view.ProjectCount = projects.Count;
            view.ByProject = projects.Values.OrderByDescending(p => p.Billable).ToList();
            view.ByModel = models.Values.OrderByDescending(m => m.Billable).ToList();
            view.ByPhase = phases;
            view.ByTool = tools
                .OrderByDescending(kv => kv.Value)
                .Take(TopCountLimit)
                .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                .ToList();
            view.UnknownCommands = unknownCommands
                .OrderByDescending(kv => kv.Value)
                .Take(TopCountLimit)
                .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                .ToList();
            view.ByDay = days.Values.OrderBy(d => d.Date, StringComparer.Ordinal).ToList();

            var totalPhase = phases.Values.Sum();
            var unknown = phases.GetValueOrDefault(PhaseClassifier.Unknown);
            view.ClassifiedPercent = totalPhase == 0
                ? 0
                : Math.Round((double)(totalPhase - unknown) * 100 / totalPhase, 1);

            return view;
        }

        /// <summary>データ品質タブ用の情報を組み立てる。</summary>
        /// <param name="scan">走査結果</param>
        /// <param name="cacheWasCorrupted">キャッシュ破損により全件再パースしたか</param>
        private static QualityInfo BuildQuality(ScanResult scan, bool cacheWasCorrupted)
        {
            var claude = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolClaudeCode).ToList();
            var codex = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCodex).ToList();
            var cursor = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCursor).ToList();
            var withTime = scan.Sessions.Where(s => s.StartUtc > DateTime.UnixEpoch).ToList();

            return new QualityInfo
            {
                ClaudeStatus = StatusText(scan.ClaudeStatus),
                CodexStatus = StatusText(scan.CodexStatus),
                CursorStatus = StatusText(scan.CursorStatus),
                ClaudeRoot = scan.ClaudeRootPath,
                CodexRoot = scan.CodexRootPath,
                CursorRoot = scan.CursorRootPath,
                ParsedFiles = scan.ParsedFileCount,
                ReusedFiles = scan.ReusedFileCount,
                JournalFiles = scan.JournalFileCount,
                ScanElapsedMs = scan.ElapsedMs,
                SkippedFiles = scan.Files
                    .Where(f => f.SkipReason is not null)
                    .Select(f => new SkippedFileView { Path = f.Path, Reason = f.SkipReason! })
                    .ToList(),
                ClaudeRawRecords = claude.Sum(s => s.Dedup.RawRecords),
                ClaudeAcceptedRecords = claude.Sum(s => s.Dedup.AcceptedRecords),
                ClaudeKeylessRecords = claude.Sum(s => s.Dedup.KeylessRecords),
                CodexThreads = codex.Count,
                CodexSubagents = codex.Count(s => s.IsSubagent),
                CursorSessions = cursor.Count,
                CursorSubagents = cursor.Count(s => s.IsSubagent),
                ParseErrors = scan.Sessions.Sum(s => s.ParseErrors),
                LastLineBrokenSessions = scan.Sessions.Count(s => s.LastLineBroken),
                UnmeasurableSessions = scan.Sessions.Count(s => !s.ElapsedMeasurable),
                CacheWasCorrupted = cacheWasCorrupted,
                OldestRecord = withTime.Count == 0
                    ? "-"
                    : withTime.Min(s => s.StartUtc).ToLocalTime().ToString("yyyy-MM-dd"),
                NewestRecord = withTime.Count == 0
                    ? "-"
                    : withTime.Max(s => s.EndUtc).ToLocalTime().ToString("yyyy-MM-dd"),
            };
        }

        /// <summary>読み取り状態を画面用の文字列へ変換する。</summary>
        /// <param name="status">読み取り状態</param>
        private static string StatusText(ToolScanStatus status) => status switch
        {
            ToolScanStatus.Ok => "ok",
            ToolScanStatus.Unreadable => "unreadable",
            _ => "notUsed",
        };

        /// <summary>件数辞書を加算合成する。</summary>
        /// <param name="target">加算先</param>
        /// <param name="source">加算元</param>
        private static void Merge(Dictionary<string, int> target, IReadOnlyDictionary<string, int> source)
        {
            foreach (var kv in source)
            {
                target[kv.Key] = target.GetValueOrDefault(kv.Key) + kv.Value;
            }
        }
    }
}
