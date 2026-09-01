using System.IO;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.Parsing;
using AIUsageOverlay.Services.SessionStats;

namespace AIUsageOverlay.Tests;

/// <summary>
/// 履歴の読み取り・重複排除・正規化の検証（設計書 11.3 の T1〜T8・T10・T15〜T16）。
/// 実履歴は業務情報を含むため使わず、TestData の合成 JSONL に対して検証する。
/// </summary>
public class ParsingTests
{
    /// <summary>合成 JSONL の置き場所。</summary>
    private static string DataPath(string name)
        => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    /// <summary>Claude Code の 1 ファイルを読み、レコードへ畳む。</summary>
    private static SessionStatsRecord ParseClaude(string name, bool includePrompt = true)
        => SessionStatsScanner.ParseClaudeFile(DataPath(name), includePrompt)
           ?? throw new InvalidOperationException($"レコードが作られなかった: {name}");

    /// <summary>Codex の 1 ファイルを読み、レコードへ畳む。</summary>
    private static SessionStatsRecord ParseCodex(string name, bool includePrompt = true)
        => SessionStatsScanner.ParseCodexFile(DataPath(name), includePrompt)
           ?? throw new InvalidOperationException($"レコードが作られなかった: {name}");

    /// <summary>
    /// T1: 同一 requestId の行は 1 件だけ採用する。
    /// 4 行に分かれた同一 usage を全部足すと 4 倍になる。
    /// </summary>
    [Fact]
    public void T1_同一requestIdの重複行は1件だけ採用される()
    {
        var record = ParseClaude("claude_normal.jsonl");

        // req_1 が 4 行 + req_2 が 1 行 = 生 5 件、採用は 2 件
        Assert.Equal(5, record.Dedup.RawRecords);
        Assert.Equal(2, record.Dedup.AcceptedRecords);

        // 採用した 2 件ぶんだけが合計になる（4 行を足していたら output は 450 になる）
        Assert.Equal(150, record.Tokens.Output);
        Assert.Equal(15, record.Tokens.Input);
        Assert.Equal(260, record.Tokens.CacheCreate);
        Assert.Equal(3700, record.Tokens.CacheRead);
    }

    /// <summary>
    /// T1 補: iterations と cache_creation の入れ子は同じ値の再掲であり、加算に使わない。
    /// </summary>
    [Fact]
    public void T1_入れ子のiterationsは加算されない()
    {
        var line = "{\"type\":\"assistant\",\"requestId\":\"r\",\"message\":{\"id\":\"m\"," +
                   "\"usage\":{\"input_tokens\":10,\"output_tokens\":20," +
                   "\"iterations\":[{\"input_tokens\":10,\"output_tokens\":20}]," +
                   "\"cache_creation\":{\"ephemeral_1h_input_tokens\":5}," +
                   "\"cache_creation_input_tokens\":5}}}";

        var parsed = ClaudeCodeHistoryParser.ParseLine(line);

        Assert.NotNull(parsed);
        Assert.Equal(10, parsed!.InputTokens);
        Assert.Equal(20, parsed.OutputTokens);
        Assert.Equal(5, parsed.CacheCreationTokens);
    }

    /// <summary>
    /// T2: requestId を持たない行は「一意」として必ず加算する。
    /// 鍵が無いことを理由に捨てると、実在する消費を落とす。
    /// </summary>
    [Fact]
    public void T2_requestIdを持たない行も加算される()
    {
        var record = ParseClaude("claude_no_requestid.jsonl");

        Assert.Equal(3, record.Dedup.RawRecords);
        Assert.Equal(3, record.Dedup.AcceptedRecords);
        Assert.Equal(2, record.Dedup.KeylessRecords);
        // 10 + 20 + 30。鍵なし 2 件を捨てていたら 10 になる
        Assert.Equal(60, record.Tokens.Output);
    }

    /// <summary>
    /// T3: 同一 session_id を共有する 3 ファイルは、複製ではなく独立した 3 スレッドである。
    /// B0/Q2 の実測に基づき、一意化キーは session_meta.id とする。
    /// session_id で最大値のみを採ると、サブエージェントの消費が丸ごと落ちる。
    /// </summary>
    [Fact]
    public void T3_同一sessionIdの複数スレッドは別々に集計される()
    {
        var records = new[]
        {
            ParseCodex("codex_forked_1.jsonl"),
            ParseCodex("codex_forked_2.jsonl"),
            ParseCodex("codex_forked_3.jsonl"),
        };

        // キーはスレッド固有 ID で分かれる
        Assert.Equal(3, records.Select(r => r.Key).Distinct().Count());
        Assert.All(records, r => Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001", r.RootId));

        // 合計は 3 スレッドの和。最大値だけを採ると 25,000 になり 45,300 を落とす
        var total = records.Sum(r => r.Tokens.Input + r.Tokens.Output);
        Assert.Equal((20000 + 1000) + (20000 + 5000) + (20000 + 300), total);

        // 2 件目・3 件目はサブエージェントとして親に紐づく
        Assert.False(records[0].IsSubagent);
        Assert.True(records[1].IsSubagent);
        Assert.Equal("codex:thread-0001", records[1].ParentKey);
        Assert.Equal("agent2", records[1].AgentLabel);
    }

    /// <summary>
    /// T4: 時刻が 1 種類しかないセッションは経過時間を測定不可とする。
    /// トークンは集計する（0 分と表示してはいけない）。
    /// </summary>
    [Fact]
    public void T4_同一timestampのみのセッションは測定不可になる()
    {
        var record = ParseCodex("codex_single_ts.jsonl");

        Assert.False(record.ElapsedMeasurable);
        Assert.Equal(0, record.ElapsedSec);
        Assert.Equal(5000, record.Tokens.Input);
        Assert.Equal(400, record.Tokens.Output);
    }

    /// <summary>
    /// T5: 壊れた行はその行だけスキップし、parseErrors に計上する。
    /// 1 行の破損でセッション全体を捨てない。
    /// </summary>
    [Fact]
    public void T5_壊れた行はスキップして続行する()
    {
        var record = ParseClaude("broken.jsonl");

        Assert.Equal(1, record.ParseErrors);
        Assert.False(record.LastLineBroken);
        Assert.Equal(40, record.Tokens.Output);
    }

    /// <summary>
    /// T16: 末尾行だけが壊れている場合はフラグを立てる。エラーにはしない。
    /// Codex は最終行の累積値を採るため、末尾破損は静かな過小計上になる。
    /// </summary>
    [Fact]
    public void T16_末尾行の破損はフラグで示される()
    {
        var record = ParseClaude("broken_lastline.jsonl");

        Assert.True(record.LastLineBroken);
        Assert.Equal(1, record.ParseErrors);
        Assert.Equal(60, record.Tokens.Output);
    }

    /// <summary>
    /// T6: cwd の大文字小文字・区切り・末尾の揺れを吸収し、1 プロジェクトに束ねる。
    /// 表示は最初に観測した表記を使う。
    /// </summary>
    [Fact]
    public void T6_cwdの表記揺れは1プロジェクトに束ねられる()
    {
        var upper = ParseCodex("codex_case_upper.jsonl");
        var lower = ParseCodex("codex_case_lower.jsonl");

        Assert.Equal(upper.ProjectKey, lower.ProjectKey);

        // 表示名は開始時刻が早い方（大文字ドライブ）に揃う
        var all = new List<SessionStatsRecord> { lower, upper };
        SessionNormalizer.UnifyProjectDisplayNames(all);
        Assert.Equal("C:\\Work\\SampleApp", upper.ProjectDisplay);
        Assert.Equal("C:\\Work\\SampleApp", lower.ProjectDisplay);
    }

    /// <summary>
    /// T7: 日をまたぐセッションは byDay が 2 日に分かれ、合計がセッション合計に一致する。
    /// </summary>
    [Fact]
    public void T7_日またぎのセッションは日別に分割され合計が一致する()
    {
        var record = ParseClaude("cross_midnight.jsonl");

        Assert.Equal(2, record.ByDay.Count);
        Assert.Equal(record.Tokens.Output, record.ByDay.Sum(d => d.Tokens.Output));
        Assert.Equal(record.Tokens.Input, record.ByDay.Sum(d => d.Tokens.Input));
        Assert.Equal(record.Turns, record.ByDay.Sum(d => d.Turns));
    }

    /// <summary>
    /// T8: PowerShell のコマンドをフェーズへ分類する。
    /// 決められないものは未分類として数え、unknownCommands に載せる。
    /// </summary>
    [Fact]
    public void T8_PowerShellのコマンドが分類される()
    {
        var record = ParseCodex("codex_powershell.jsonl");

        // Get-Content と（cd を読み飛ばした先の）Get-ChildItem が調査
        Assert.Equal(2, record.PhaseCounts[PhaseClassifier.Investigate]);
        // dotnet test がテスト
        Assert.Equal(1, record.PhaseCounts[PhaseClassifier.Test]);
        // patch_apply_end が実装
        Assert.Equal(1, record.PhaseCounts[PhaseClassifier.Implement]);
        // Get-Random は未分類として数え、語彙追加の手掛かりとして残す
        Assert.Equal(1, record.PhaseCounts[PhaseClassifier.Unknown]);
        Assert.Equal(1, record.UnknownCommands["Get-Random"]);
    }

    /// <summary>
    /// T8 補: shell_command の引数がキー無し引用符の JS リテラルでも取り出せる。
    /// 実データはこの形であり、厳密な JSON ではない。
    /// </summary>
    [Theory]
    [InlineData("const r = await tools.shell_command({command:\"git status\"}); text(r)", "git status")]
    [InlineData("const r = await tools.shell_command({\"command\":\"git status\"}); text(r)", "git status")]
    public void T8_shell_commandの二重構造からコマンドを取り出せる(string input, string expected)
    {
        Assert.Equal(expected, CodexRolloutParser.ExtractShellCommand(input));
    }

    /// <summary>
    /// T8 補: コマンド内にエスケープされた二重引用符があっても途中で切れない。
    /// 正規表現の非貪欲マッチで拾うと壊れるケース。
    /// </summary>
    [Fact]
    public void T8_エスケープされた引用符を含むコマンドを完全に取り出せる()
    {
        const string input =
            "const r = await tools.shell_command({command:\"$p='x'; \\\"A=$p\\\"; " +
            "Get-Content 'a.txt'\",workdir:\"C:\\\\Work\"}); text(r)";

        var command = CodexRolloutParser.ExtractShellCommand(input);

        Assert.NotNull(command);
        Assert.EndsWith("Get-Content 'a.txt'", command);
        Assert.Contains("\"A=$p\"", command);
    }

    /// <summary>
    /// T15: セッション内でモデルが変わると byModel が 2 件になり、合計がセッション合計に一致する。
    /// Codex は累積値が真であるため、内訳はターン差分の比率で割り振る。
    /// </summary>
    [Fact]
    public void T15_モデル別内訳の合計がセッション合計に一致する()
    {
        var record = ParseCodex("codex_multimodel.jsonl");

        Assert.Equal(2, record.ByModel.Count);
        Assert.Equal(record.Tokens.Input, record.ByModel.Sum(m => m.Tokens.Input));
        Assert.Equal(record.Tokens.Output, record.ByModel.Sum(m => m.Tokens.Output));
        Assert.Equal(record.Tokens.CacheRead, record.ByModel.Sum(m => m.Tokens.CacheRead));
        // 累積値の最終行がセッション合計になる
        Assert.Equal(30000, record.Tokens.Input);
        Assert.Equal(1500, record.Tokens.Output);
    }

    /// <summary>
    /// T10: 親の累計値は子の単独値を含む。プロジェクト集計では単独値を使うため二重計上しない。
    /// </summary>
    [Fact]
    public void T10_親子の累計値と単独値が両方得られる()
    {
        var records = new List<SessionStatsRecord>
        {
            ParseCodex("codex_forked_1.jsonl"),
            ParseCodex("codex_forked_2.jsonl"),
            ParseCodex("codex_forked_3.jsonl"),
        };
        SessionNormalizer.ResolveParentLinks(records);

        var scan = new ScanResult();
        scan.Sessions.AddRange(records);
        var report = SessionStatsAggregator.Build(
            scan, includePrompt: true, appVersion: "test", cacheWasCorrupted: false,
            nowLocal: new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Local));

        var parent = report.Sessions.Single(s => s.Key == "codex:thread-0001");
        var child = report.Sessions.Single(s => s.Key == "codex:thread-0002");

        Assert.Equal(2, parent.SubagentCount);
        Assert.Equal(parent.Billable + child.Billable +
                     report.Sessions.Single(s => s.Key == "codex:thread-0003").Billable,
                     parent.BillableCumulative);

        // プロジェクト別は単独値の総和。累計値を足すと二重計上になる
        var period = report.Periods["all"];
        var project = Assert.Single(period.ByProject);
        Assert.Equal(report.Sessions.Sum(s => s.Billable), project.Billable);
    }

    /// <summary>
    /// T9: 指示文は改行を空白へ畳み、先頭 64 文字で切る。
    /// 設定で出力を抑止した場合は保持しない。
    /// </summary>
    [Fact]
    public void T9_指示文は64文字で切られ改行が畳まれる()
    {
        var trimmed = SessionNormalizer.TrimPrompt("一行目\n\n二行目\tタブ  連続空白");
        Assert.Equal("一行目 二行目 タブ 連続空白", trimmed);

        var longText = new string('あ', 100);
        Assert.Equal(64, SessionNormalizer.TrimPrompt(longText)!.Length);

        var record = ParseClaude("claude_normal.jsonl", includePrompt: false);
        Assert.Null(record.FirstPrompt);
    }

    /// <summary>
    /// 課金相当と総処理の式がツールごとに切り替わる（設計書 7.1）。
    /// Codex は cached_input が input の内数であるため差し引く（B0/Q1 で確定）。
    /// </summary>
    [Fact]
    public void トークン指標の式がツールごとに切り替わる()
    {
        var tokens = new TokenTotals
        {
            Input = 1000, Output = 100, CacheCreate = 50, CacheRead = 800,
        };

        Assert.Equal(1150, tokens.Billable(SessionStatsRecord.ToolClaudeCode));
        Assert.Equal(1950, tokens.Processed(SessionStatsRecord.ToolClaudeCode));
        Assert.Equal(300, tokens.Billable(SessionStatsRecord.ToolCodex));
        Assert.Equal(1100, tokens.Processed(SessionStatsRecord.ToolCodex));
    }

    /// <summary>
    /// フェーズ分類の基本規則。パイプは先頭のみを見て、cd などの前置きは読み飛ばす。
    /// </summary>
    [Theory]
    [InlineData("Get-Content a.txt", PhaseClassifier.Investigate)]
    [InlineData("rg -n foo src", PhaseClassifier.Investigate)]
    [InlineData("git status --short", PhaseClassifier.Investigate)]
    [InlineData("dotnet build -c Release", PhaseClassifier.Test)]
    [InlineData("dotnet build | Select-String error", PhaseClassifier.Test)]
    [InlineData("cd C:\\Work && dotnet test", PhaseClassifier.Test)]
    [InlineData("git commit -m 'x'", PhaseClassifier.Implement)]
    [InlineData("Get-Random", PhaseClassifier.Unknown)]
    [InlineData("", PhaseClassifier.Unknown)]
    public void コマンドのフェーズ分類(string command, string expected)
    {
        Assert.Equal(expected, PhaseClassifier.ClassifyCommand(command));
    }

    /// <summary>Claude Code のツール名からの分類（設計書 7.3.1）。</summary>
    [Theory]
    [InlineData("Read", null, PhaseClassifier.Investigate)]
    [InlineData("Edit", null, PhaseClassifier.Implement)]
    [InlineData("AskUserQuestion", null, PhaseClassifier.Dialogue)]
    [InlineData("Bash", "dotnet test", PhaseClassifier.Test)]
    [InlineData("Bash", "Get-Random", PhaseClassifier.Unknown)]
    [InlineData("mcp__something__do", null, PhaseClassifier.Unknown)]
    public void ツール名のフェーズ分類(string tool, string? command, string expected)
    {
        Assert.Equal(expected, PhaseClassifier.ClassifyClaudeTool(tool, command));
    }
}
