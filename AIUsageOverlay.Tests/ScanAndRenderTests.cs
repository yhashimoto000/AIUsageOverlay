using System.IO;
using System.Text.Json;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.SessionStats;

namespace AIUsageOverlay.Tests;

/// <summary>
/// 走査・キャッシュ・HTML 生成の検証（設計書 11.3 の T11〜T14）。
/// 一時ディレクトリを履歴ルートに見立てて実行し、実機の履歴には触れない。
/// </summary>
public class ScanAndRenderTests : IDisposable
{
    /// <summary>このテストが使う一時ディレクトリ。</summary>
    private readonly string _root;

    /// <summary>Claude Code の履歴ルートに見立てるディレクトリ。</summary>
    private readonly string _claudeRoot;

    /// <summary>Codex の履歴ルートに見立てるディレクトリ。</summary>
    private readonly string _codexRoot;

    /// <summary>Cowork の履歴ルートに見立てるディレクトリ。</summary>
    private readonly string _coworkRoot;

    /// <summary>Cowork の走査対象となる .claude/projects。</summary>
    private readonly string _coworkProjects;

    /// <summary>キャッシュの保存先。</summary>
    private readonly string _cacheFolder;

    /// <summary>一時ディレクトリを作り、合成 JSONL を配置する。</summary>
    public ScanAndRenderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AIUsageOverlayTests", Guid.NewGuid().ToString("N"));
        _claudeRoot = Path.Combine(_root, "claude", "projects");
        _codexRoot = Path.Combine(_root, "codex", "sessions", "2026", "08", "10");
        _coworkRoot = Path.Combine(_root, "cowork");
        _coworkProjects = Path.Combine(
            _coworkRoot, "internal-id", "account-id", "local_session", ".claude", "projects");
        _cacheFolder = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(_claudeRoot);
        Directory.CreateDirectory(_codexRoot);
        Directory.CreateDirectory(_coworkProjects);
        Directory.CreateDirectory(_cacheFolder);

        var source = Path.Combine(AppContext.BaseDirectory, "TestData");
        foreach (var file in Directory.GetFiles(source, "claude_*.jsonl")
                     .Concat(Directory.GetFiles(source, "cross_*.jsonl"))
                     .Concat(Directory.GetFiles(source, "injection.jsonl")))
        {
            File.Copy(file, Path.Combine(_claudeRoot, Path.GetFileName(file)));
        }
        foreach (var file in Directory.GetFiles(source, "codex_*.jsonl"))
        {
            // Codex 側は rollout- 始まりのファイル名だけを走査する
            File.Copy(file, Path.Combine(_codexRoot, "rollout-" + Path.GetFileName(file)));
        }
        File.Copy(
            Path.Combine(source, "cowork_normal.jsonl"),
            Path.Combine(_coworkProjects, "cowork_normal.jsonl"));
        // 監査JSONLは既知階層の外に置き、列挙されないことを検証する。
        File.WriteAllText(
            Path.Combine(_coworkRoot, "internal-id", "account-id", "local_session", "audit.jsonl"),
            "{ broken audit json");
    }

    /// <summary>一時ディレクトリを片付ける。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 後始末に失敗してもテスト結果には影響しない
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>テスト対象の走査器を作る。</summary>
    private SessionStatsScanner CreateScanner()
        => new(
            _claudeRoot,
            Path.Combine(_root, "codex", "sessions"),
            cursorRoot: string.Empty,
            coworkRoot: _coworkRoot);

    /// <summary>
    /// 走査すると両ツールのセッションが読み取られ、履歴ルートの状態が記録される。
    /// </summary>
    [Fact]
    public void 両ツールの履歴が走査される()
    {
        var scan = CreateScanner().Scan(cache: null, includePrompt: true);

        Assert.Equal(ToolScanStatus.Ok, scan.ClaudeStatus);
        Assert.Equal(ToolScanStatus.Ok, scan.CoworkStatus);
        Assert.Equal(ToolScanStatus.Ok, scan.CodexStatus);
        Assert.Contains(scan.Sessions, s => s.Tool == SessionStatsRecord.ToolClaudeCode);
        Assert.Contains(scan.Sessions, s => s.Tool == SessionStatsRecord.ToolCowork);
        Assert.Contains(scan.Sessions, s => s.Tool == SessionStatsRecord.ToolCodex);
        Assert.DoesNotContain(scan.Files, f => Path.GetFileName(f.Path) == "audit.jsonl");
        Assert.Equal(scan.Sessions.Count, scan.Sessions.Select(s => s.Key).Distinct().Count());
    }

    /// <summary>
    /// 履歴ディレクトリが存在しない場合は「未使用」とし、エラーにしない（設計書 8.6）。
    /// </summary>
    [Fact]
    public void 履歴ディレクトリが無いツールは未使用として扱われる()
    {
        var scanner = new SessionStatsScanner(_claudeRoot, Path.Combine(_root, "no-such-dir"));

        var scan = scanner.Scan(cache: null, includePrompt: true);

        Assert.Equal(ToolScanStatus.Ok, scan.ClaudeStatus);
        Assert.Equal(ToolScanStatus.NotUsed, scan.CodexStatus);
        Assert.DoesNotContain(scan.Sessions, s => s.Tool == SessionStatsRecord.ToolCodex);
    }

    /// <summary>ルート配下にセッション階層があるが既知のCowork階層が無い場合は未対応とする。</summary>
    [Fact]
    public void Coworkの既知階層が無い場合はunsupportedになる()
    {
        var unsupportedRoot = Path.Combine(_root, "unsupported-cowork");
        Directory.CreateDirectory(Path.Combine(unsupportedRoot, "internal", "account", "session", "outputs"));
        var scanner = new SessionStatsScanner(
            _claudeRoot,
            Path.Combine(_root, "no-codex"),
            cursorRoot: string.Empty,
            coworkRoot: unsupportedRoot);

        var scan = scanner.Scan(cache: null, includePrompt: false);

        Assert.Equal(ToolScanStatus.Unsupported, scan.CoworkStatus);
        Assert.DoesNotContain(scan.Sessions, s => s.Tool == SessionStatsRecord.ToolCowork);
    }

    /// <summary>
    /// T11: 2 回目のスキャンでは変化のないファイルを再パースせず、
    /// 更新したファイルだけを読み直す。
    /// </summary>
    [Fact]
    public void T11_2回目のスキャンは差分のみ再パースする()
    {
        var scanner = CreateScanner();
        var first = scanner.Scan(cache: null, includePrompt: true);
        var totalFiles = first.ParsedFileCount;
        Assert.True(totalFiles > 1, "テストデータが 2 件以上ある前提");

        var cache = new SessionStatsCache
        {
            LastScanUtc = DateTime.UtcNow,
            Files = first.Files,
            Sessions = first.Sessions,
        };

        // 何も変えずに再スキャンすると 1 件も再パースしない
        var second = scanner.Scan(cache, includePrompt: true);
        Assert.Equal(0, second.ParsedFileCount);
        Assert.Equal(totalFiles, second.ReusedFileCount);

        // 1 ファイルだけ更新すると、そのファイルだけ再パースされる
        var target = Path.Combine(_claudeRoot, "claude_normal.jsonl");
        File.AppendAllText(target, "\n");
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddSeconds(5));

        var third = scanner.Scan(cache, includePrompt: true);
        Assert.Equal(1, third.ParsedFileCount);
        Assert.Equal(totalFiles - 1, third.ReusedFileCount);
    }

    /// <summary>
    /// T12: 壊れたキャッシュは .bak へ退避し、全件再パースへ倒す。
    /// </summary>
    [Fact]
    public void T12_壊れたキャッシュはbakへ退避され全件再パースになる()
    {
        var store = new SessionStatsCacheStore(_cacheFolder);
        File.WriteAllText(store.CachePath, "{ this is not json");

        var loaded = store.Load();

        Assert.Null(loaded);
        Assert.True(store.LastLoadWasCorrupted);
        Assert.True(File.Exists(store.CachePath + ".bak"));
        Assert.False(File.Exists(store.CachePath));
    }

    /// <summary>
    /// スキーマ版が違うキャッシュは読み捨てて全件再パースする（利用者に確認しない）。
    /// </summary>
    [Fact]
    public void スキーマ版が違うキャッシュは使わない()
    {
        var store = new SessionStatsCacheStore(_cacheFolder);
        var cache = new SessionStatsCache { SchemaVersion = SessionStatsCache.CurrentSchemaVersion + 1 };
        store.Save(cache);

        Assert.Null(store.Load());
        Assert.False(store.LastLoadWasCorrupted);
    }

    /// <summary>
    /// キャッシュは一時ファイル経由で原子的に置換され、往復しても内容が保たれる。
    /// </summary>
    [Fact]
    public void キャッシュは保存して読み戻せる()
    {
        var scan = CreateScanner().Scan(cache: null, includePrompt: true);
        var store = new SessionStatsCacheStore(_cacheFolder);

        store.Save(new SessionStatsCache
        {
            LastScanUtc = DateTime.UtcNow,
            Files = scan.Files,
            Sessions = scan.Sessions,
        });
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(scan.Sessions.Count, loaded!.Sessions.Count);
        Assert.False(File.Exists(store.CachePath + ".tmp"));
    }

    /// <summary>
    /// T13: 生成した HTML は外部参照を持たない。
    /// CDN・外部フォント・外部スクリプトのいずれも含まないこと。
    /// </summary>
    [Fact]
    public void T13_生成HTMLに外部参照が含まれない()
    {
        // SVG の名前空間 URI は createElementNS の識別子であり、通信を伴わない。
        // 外部参照の検査からは除いて判定する。
        var html = BuildHtml().Replace("http://www.w3.org/2000/svg", "(svg-namespace)");

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//cdn", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// T14: 指示文に「&lt;/script&gt;」や HTML コメント、行終端子が含まれても HTML が壊れない。
    /// 埋め込み JSON 側でエスケープし、埋め込み処理を通らない書き出し経路を作らない。
    /// </summary>
    [Fact]
    public void T14_危険な文字を含む指示文でもHTMLが壊れない()
    {
        var html = BuildHtml();

        // データ部に生の閉じタグや行終端子が残っていないこと
        var start = html.IndexOf("id=\"report-data\">", StringComparison.Ordinal) + "id=\"report-data\">".Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        Assert.True(end > start, "埋め込み JSON の終端が見つかる");

        var json = html[start..end];
        Assert.DoesNotContain("<", json);
        Assert.DoesNotContain(">", json);
        Assert.DoesNotContain("&", json);
        Assert.DoesNotContain("\u2028", json);
        Assert.DoesNotContain("\u2029", json);

        // エスケープ後も JSON として読め、元の文字列へ戻る
        using var doc = JsonDocument.Parse(json);
        var raw = doc.RootElement.GetProperty("sessions").EnumerateArray()
            .Select(s => s.TryGetProperty("prompt", out var p) ? p.GetString() : null)
            .FirstOrDefault(v => v is not null && v.Contains("</script>", StringComparison.Ordinal));
        Assert.NotNull(raw);
        Assert.Contains("<!--", raw!, StringComparison.Ordinal);
    }

    /// <summary>4ツール表示とAPI換算ポイントを含み、通貨・金額表記を出さない。</summary>
    [Fact]
    public void CoworkとAPI換算ポイントを金額非表示で描画する()
    {
        var html = BuildHtml();

        Assert.Contains("Cowork", html, StringComparison.Ordinal);
        Assert.Contains("coworkBillable", html, StringComparison.Ordinal);
        Assert.Contains("API利用相当（参考）", html, StringComparison.Ordinal);
        Assert.Contains("API換算ポイント", html, StringComparison.Ordinal);
        Assert.Contains("value=\"cursor\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"cowork\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("USD", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$", html, StringComparison.Ordinal);
        Assert.DoesNotContain("円換算", html, StringComparison.Ordinal);
        Assert.DoesNotContain("請求額", html, StringComparison.Ordinal);
    }

    /// <summary>entrypoint欠落Coworkは採用し、品質状態をpartialにする。</summary>
    [Fact]
    public void Coworkの識別子欠落はpartialとして品質へ出す()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "claude_normal.jsonl");
        File.Copy(source, Path.Combine(_coworkProjects, "missing-entrypoint.jsonl"));

        var scan = CreateScanner().Scan(cache: null, includePrompt: false);
        var report = SessionStatsAggregator.Build(
            scan, includePrompt: false, appVersion: "test", cacheWasCorrupted: false,
            nowLocal: new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Local));

        Assert.Equal(ToolScanStatus.Partial, scan.CoworkStatus);
        Assert.Equal("partial", report.Quality.CoworkStatus);
        Assert.Equal(1, report.Quality.CoworkIdentityMissingSessions);
    }

    /// <summary>
    /// エスケープ関数そのものの検証。JSON.parse で元へ戻ることを保証する。
    /// </summary>
    [Fact]
    public void 埋め込みエスケープは危険な文字だけを置き換える()
    {
        var escaped = UsageReportRenderer.EscapeForEmbedding("{\"a\":\"</script> & <!-- \u2028\u2029\"}");

        Assert.DoesNotContain("<", escaped);
        Assert.DoesNotContain(">", escaped);
        Assert.DoesNotContain("&", escaped);
        Assert.DoesNotContain("\u2028", escaped);
        Assert.DoesNotContain("\u2029", escaped);

        using var doc = JsonDocument.Parse(escaped);
        Assert.Equal("</script> & <!-- \u2028\u2029", doc.RootElement.GetProperty("a").GetString());
    }

    /// <summary>
    /// 期間セレクタは開始時刻で絞り、全期間には全セッションが入る。
    /// </summary>
    [Fact]
    public void 期間ごとの集計が作られる()
    {
        var scan = CreateScanner().Scan(cache: null, includePrompt: true);
        var report = SessionStatsAggregator.Build(
            scan, includePrompt: true, appVersion: "test", cacheWasCorrupted: false,
            nowLocal: new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Local));

        // 今日 / 7 日 / 30 日 / 全期間 + 履歴に現れる月
        Assert.True(report.Periods.Count > 4, "月別の期間が追加されている");
        Assert.Equal(scan.Sessions.Count, report.Periods["all"].SessionKeys.Count);
        Assert.True(report.Periods["today"].SessionKeys.Count <= report.Periods["7"].SessionKeys.Count);
        Assert.True(report.Periods["7"].SessionKeys.Count <= report.Periods["30"].SessionKeys.Count);
        Assert.True(report.Periods["30"].SessionKeys.Count <= report.Periods["all"].SessionKeys.Count);

        // 「今日」は当日 0 時以降に始まったセッションだけを含む
        var todayKeys = report.Periods["today"].SessionKeys.ToHashSet();
        foreach (var s in scan.Sessions)
        {
            var startedToday = s.StartUtc.ToLocalTime().Date ==
                               new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Local).Date;
            Assert.Equal(startedToday, todayKeys.Contains(s.Key));
        }

        // 分類率は未分類を母数に含めて計算する
        var all = report.Periods["all"];
        var total = all.ByPhase.Values.Sum();
        var unknown = all.ByPhase.GetValueOrDefault(PhaseClassifier.Unknown);
        Assert.Equal(Math.Round((double)(total - unknown) * 100 / total, 1), all.ClassifiedPercent);
    }

    /// <summary>
    /// 履歴に現れる月だけが期間として追加され、その月のセッションだけを含む。
    /// 履歴は保持期間で消えるため、過去の月を後から見返せるようにしている。
    /// </summary>
    [Fact]
    public void 月別の期間が履歴のある月だけ作られる()
    {
        var scan = CreateScanner().Scan(cache: null, includePrompt: true);
        var report = SessionStatsAggregator.Build(
            scan, includePrompt: true, appVersion: "test", cacheWasCorrupted: false,
            nowLocal: new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Local));

        var monthKeys = report.Periods.Keys.Where(k => k.StartsWith("month:", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(monthKeys);

        // 合成データは 2026-08 に集中している
        Assert.Contains("month:2026-08", monthKeys);

        // 各月の期間には、その月に始まったセッションだけが入る
        foreach (var key in monthKeys)
        {
            var yearMonth = key["month:".Length..];
            var keys = report.Periods[key].SessionKeys.ToHashSet();
            foreach (var s in scan.Sessions)
            {
                var actual = s.StartUtc.ToLocalTime().ToString("yyyy-MM");
                Assert.Equal(actual == yearMonth, keys.Contains(s.Key));
            }
        }

        // 月をすべて足すと全期間のセッション数に一致する（取りこぼし・重複がない）
        var sumOfMonths = monthKeys.Sum(k => report.Periods[k].SessionKeys.Count);
        Assert.Equal(report.Periods["all"].SessionKeys.Count, sumOfMonths);
    }

    /// <summary>
    /// 履歴ファイルが消えても、キャッシュに取り込み済みのセッションは残り続ける。
    ///
    /// これが成り立つから、古いレポート HTML を消しても情報は失われない
    /// （次回の生成で同じ期間を作り直せる）。逆にこれが崩れると、
    /// レポートを消した時点で過去分が復元できなくなる。
    /// </summary>
    [Fact]
    public void 履歴ファイルが消えてもキャッシュのセッションは残る()
    {
        var scanner = CreateScanner();
        var first = scanner.Scan(cache: null, includePrompt: true);
        var before = first.Sessions.Count;
        Assert.True(before > 1, "テストデータが 2 件以上ある前提");

        var cache = new SessionStatsCache
        {
            LastScanUtc = DateTime.UtcNow,
            Files = first.Files,
            Sessions = first.Sessions,
        };

        // 履歴ファイルを 1 つ削除する（各ツールの保持期間で消えた状況を再現）
        var removed = Path.Combine(_claudeRoot, "claude_normal.jsonl");
        var removedKey = first.Files.Single(f => f.Path == removed).SessionKey;
        File.Delete(removed);

        var second = scanner.Scan(cache, includePrompt: true);

        // セッションは減らない。消えたファイルの分もキャッシュから復元される
        Assert.Equal(before, second.Sessions.Count);
        Assert.Contains(second.Sessions, s => s.Key == removedKey);

        // 差分判定用のファイル一覧からは消える（次回スキャンで再読み込みしないため）
        Assert.DoesNotContain(second.Files, f => f.Path == removed);
    }

    /// <summary>
    /// 生成のたびにファイルが増え続けないよう、古いレポートは削除して直近だけを残す。
    /// レポートはキャッシュからの派生物なので、消しても次回の生成で作り直せる。
    /// </summary>
    [Fact]
    public void 古いレポートは直近5件だけ残る()
    {
        var scan = CreateScanner().Scan(cache: null, includePrompt: true);
        var report = SessionStatsAggregator.Build(
            scan, includePrompt: true, appVersion: "test", cacheWasCorrupted: false,
            nowLocal: new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Local));

        var folder = Path.Combine(_root, "reports");
        var renderer = new UsageReportRenderer();

        // 8 回生成する。ファイル名は分単位なので、生成時刻をずらして別ファイルにする
        var paths = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var at = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Local).AddMinutes(i);
            paths.Add(renderer.Render(report, folder, at));
        }

        var remaining = Directory.GetFiles(folder, "usage-*.html").Select(Path.GetFileName).ToList();
        Assert.Equal(5, remaining.Count);

        // 残るのは新しい 5 件。最新は必ず残る
        Assert.Contains(Path.GetFileName(paths[^1]), remaining);
        Assert.DoesNotContain(Path.GetFileName(paths[0]), remaining);
    }

    /// <summary>
    /// 整理の対象は本機能が作った usage-*.html だけ。
    /// 利用者が同じフォルダへ置いた別のファイルは消さない。
    /// </summary>
    [Fact]
    public void 整理は自分が生成したファイルだけを対象にする()
    {
        var folder = Path.Combine(_root, "reports-mixed");
        Directory.CreateDirectory(folder);

        foreach (var i in Enumerable.Range(0, 8))
        {
            File.WriteAllText(Path.Combine(folder, $"usage-2026082{i}-1200.html"), "x");
        }
        var keepMe = Path.Combine(folder, "my-notes.html");
        var keepMe2 = Path.Combine(folder, "usage-summary.txt");
        File.WriteAllText(keepMe, "利用者が置いたファイル");
        File.WriteAllText(keepMe2, "利用者が置いたファイル");

        UsageReportRenderer.PruneOldReports(folder, 5);

        Assert.Equal(5, Directory.GetFiles(folder, "usage-*.html").Length);
        Assert.True(File.Exists(keepMe), "無関係な html を消してはいけない");
        Assert.True(File.Exists(keepMe2), "拡張子が違うファイルを消してはいけない");
    }

    /// <summary>合成データからレポート HTML を組み立てる。</summary>
    private string BuildHtml()
    {
        var scan = CreateScanner().Scan(cache: null, includePrompt: true);
        var report = SessionStatsAggregator.Build(
            scan, includePrompt: true, appVersion: "test", cacheWasCorrupted: false,
            nowLocal: new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Local));
        return new UsageReportRenderer().BuildHtml(report);
    }
}
