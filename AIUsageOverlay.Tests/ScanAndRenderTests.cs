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

    /// <summary>キャッシュの保存先。</summary>
    private readonly string _cacheFolder;

    /// <summary>一時ディレクトリを作り、合成 JSONL を配置する。</summary>
    public ScanAndRenderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AIUsageOverlayTests", Guid.NewGuid().ToString("N"));
        _claudeRoot = Path.Combine(_root, "claude", "projects");
        _codexRoot = Path.Combine(_root, "codex", "sessions", "2026", "08", "10");
        _cacheFolder = Path.Combine(_root, "appdata");
        Directory.CreateDirectory(_claudeRoot);
        Directory.CreateDirectory(_codexRoot);
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
        => new(_claudeRoot, Path.Combine(_root, "codex", "sessions"));

    /// <summary>
    /// 走査すると両ツールのセッションが読み取られ、履歴ルートの状態が記録される。
    /// </summary>
    [Fact]
    public void 両ツールの履歴が走査される()
    {
        var scan = CreateScanner().Scan(cache: null, includePrompt: true);

        Assert.Equal(ToolScanStatus.Ok, scan.ClaudeStatus);
        Assert.Equal(ToolScanStatus.Ok, scan.CodexStatus);
        Assert.Contains(scan.Sessions, s => s.Tool == SessionStatsRecord.ToolClaudeCode);
        Assert.Contains(scan.Sessions, s => s.Tool == SessionStatsRecord.ToolCodex);
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

        Assert.Equal(3, report.Periods.Count);
        Assert.Equal(scan.Sessions.Count, report.Periods["all"].SessionKeys.Count);
        Assert.True(report.Periods["7"].SessionKeys.Count <= report.Periods["30"].SessionKeys.Count);
        Assert.True(report.Periods["30"].SessionKeys.Count <= report.Periods["all"].SessionKeys.Count);

        // 分類率は未分類を母数に含めて計算する
        var all = report.Periods["all"];
        var total = all.ByPhase.Values.Sum();
        var unknown = all.ByPhase.GetValueOrDefault(PhaseClassifier.Unknown);
        Assert.Equal(Math.Round((double)(total - unknown) * 100 / total, 1), all.ClassifiedPercent);
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
