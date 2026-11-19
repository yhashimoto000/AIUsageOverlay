using System.IO;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.SessionStats;

namespace AIUsageOverlay.Tests;

/// <summary>
/// 実機の履歴に対する計測（設計書 Q6 / B4 の Done 条件）。
///
/// 実履歴を読むため通常のテスト実行では動かさない。
/// 環境変数 AIUSAGE_REAL_SCAN=1 を設定したときだけ計測し、結果を標準出力へ書く。
/// 読み取り専用であり、履歴ファイルにもキャッシュにも書き込まない。
/// </summary>
public class RealHistoryScanTests
{
    /// <summary>計測を有効にする環境変数名。</summary>
    private const string EnableVariable = "AIUSAGE_REAL_SCAN";

    /// <summary>レポート生成を有効にする環境変数名。</summary>
    private const string ReportVariable = "AIUSAGE_REAL_REPORT";

    /// <summary>レポートの出力先を指定する環境変数名。未指定なら一時フォルダ。</summary>
    private const string ReportFolderVariable = "AIUSAGE_REAL_REPORT_DIR";

    /// <summary>
    /// 実機の全履歴を走査し、所要時間・件数・分類率を出力する。
    /// 数値の判断は人間が行う（自動での合否判定はしない）。
    /// </summary>
    [Fact]
    public void 実機履歴のフルスキャンを計測する()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        var scanner = new SessionStatsScanner();
        var scan = scanner.Scan(cache: null, includePrompt: false);

        var report = SessionStatsAggregator.Build(
            scan, includePrompt: false, appVersion: "measure", cacheWasCorrupted: false,
            nowLocal: DateTime.Now);
        var all = report.Periods["all"];

        var claude = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolClaudeCode).ToList();
        var cowork = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCowork).ToList();
        var codex = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCodex).ToList();
        var cursor = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCursor).ToList();

        var lines = new List<string>
        {
            "===== 実機フルスキャン計測 =====",
            $"所要時間              : {scan.ElapsedMs / 1000.0:F1} 秒",
            $"パースしたファイル    : {scan.ParsedFileCount}",
            $"journal.jsonl         : {scan.JournalFileCount}",
            $"読めなかったファイル  : {scan.Files.Count(f => f.SkipReason is not null)}",
            $"セッション            : {scan.Sessions.Count}"
                + $"（Claude {claude.Count} / Cowork {cowork.Count} / Codex {codex.Count} / Cursor {cursor.Count}）",
            $"Cowork状態            : {report.Quality.CoworkStatus}",
            $"Cowork対象ファイル    : {report.Quality.CoworkFiles}",
            $"Cowork識別子欠落      : {report.Quality.CoworkIdentityMissingSessions}",
            $"Coworkキー衝突        : {report.Quality.CoworkKeyCollisions}",
            $"サブエージェント      : {scan.Sessions.Count(s => s.IsSubagent)}",
            $"壊れた行              : {scan.Sessions.Sum(s => s.ParseErrors)}",
            $"末尾破損セッション    : {scan.Sessions.Count(s => s.LastLineBroken)}",
            $"経過時間 測定不可     : {scan.Sessions.Count(s => !s.ElapsedMeasurable)}",
            "",
            "----- 全期間の集計 -----",
            $"課金相当トークン      : {all.Billable:N0}",
            $"総処理トークン        : {all.Processed:N0}",
            $"キャッシュ読込        : {all.CacheRead:N0}",
            $"プロジェクト          : {all.ProjectCount}",
            $"フェーズ分類率        : {all.ClassifiedPercent:F1} %",
            "",
            "----- フェーズ別（回数）-----",
        };

        foreach (var phase in PhaseClassifier.AllPhases)
        {
            lines.Add($"  {phase,-12}: {all.ByPhase.GetValueOrDefault(phase):N0}");
        }

        lines.Add("");
        lines.Add("----- ツール別の分類率 -----");
        foreach (var (label, group) in new[]
                 {
                     ("Claude Code", claude), ("Cowork", cowork),
                     ("Codex", codex), ("Cursor", cursor),
                 })
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var s in group)
            {
                foreach (var kv in s.PhaseCounts)
                {
                    counts[kv.Key] = counts.GetValueOrDefault(kv.Key) + kv.Value;
                }
            }
            var total = counts.Values.Sum();
            var unknown = counts.GetValueOrDefault(PhaseClassifier.Unknown);
            var rate = total == 0 ? 0 : (double)(total - unknown) * 100 / total;
            lines.Add($"  {label,-12}: {rate:F1} %（実行 {total:N0} 回 / 未分類 {unknown:N0} 回）");
        }

        var text = string.Join(Environment.NewLine, lines);
        var outputPath = Path.Combine(Path.GetTempPath(), "aiusage-real-scan.txt");
        File.WriteAllText(outputPath, text);

        Assert.True(scan.Sessions.Count > 0, "実機履歴が 1 件も読めなかった");
        Assert.All(scan.Sessions, s => Assert.Null(s.FirstPrompt));
    }

    /// <summary>
    /// 実機の全履歴からレポート HTML を生成し、目視確認用に書き出す。
    ///
    /// 効果・稼働タブは合成データでは出ない見え方（枠の偏り、履歴が消えた月の欠け、
    /// 単価表に無いモデルの量）を含むため、生成物を人間が開いて確かめる必要がある。
    /// 読み取り専用であり、キャッシュには書き込まない。
    /// 出力先は AIUSAGE_REAL_REPORT_DIR で指定する（未指定なら一時フォルダ）。
    /// </summary>
    [Fact]
    public void 実機履歴からレポートを生成する()
    {
        if (Environment.GetEnvironmentVariable(ReportVariable) != "1")
        {
            return;
        }

        var scanner = new SessionStatsScanner();
        var scan = scanner.Scan(cache: null, includePrompt: false);

        var now = DateTime.Now;
        var report = SessionStatsAggregator.Build(
            scan, includePrompt: false, appVersion: "preview", cacheWasCorrupted: false,
            nowLocal: now, assumptions: new ReportAssumptions(5000, 2.0, 150.0));

        var folder = Environment.GetEnvironmentVariable(ReportFolderVariable);
        var path = new UsageReportRenderer().Render(
            report,
            string.IsNullOrWhiteSpace(folder) ? Path.GetTempPath() : folder,
            now);

        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "aiusage-real-report-path.txt"), path);

        var impact = report.Periods["all"].Impact;
        Assert.True(File.Exists(path));
        Assert.True(impact.TotalActiveMinutes > 0, "稼働枠が 1 つも記録されなかった");
        Assert.NotEmpty(impact.Hourly);
    }
}
