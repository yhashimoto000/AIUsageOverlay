using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// Codex 使用量の Web 取得が失敗したとき、原因切り分け用の診断ログを書き出すヘルパー。
    ///
    /// ChatGPT 側の API 変更（エンドポイント移動・JSON 構造変更）が表示消失の主因になるため、
    /// 「取得元パス・HTTP 状態・JSON のキー階層」だけを残して、実応答を見ずに原因を追えるようにする。
    ///
    /// 機密配慮:
    ///   使用率・メールアドレス・ユーザー ID・アカウント ID などの「値」は一切書き出さない。
    ///   書き出すのはキー名・JSON 型名・配列長・HTTP 状態・本文長のみ。
    ///   クエリ文字列・ヘッダー・Cookie も保持しない（スクレイパー側で安全なパスのみ付与済み）。
    /// </summary>
    internal static class CodexWebDiagnostics
    {
        /// <summary>診断ログの出力先（設定フォルダ直下・毎回上書き）。</summary>
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIUsageOverlay", "codex-web-diagnostic.log");

        /// <summary>Codex 専用の使用制限 API を示す取得元プレフィックス。</summary>
        private const string WhamUsagePrefix = "__URL__:/backend-api/wham/usage";

        /// <summary>wham usage 候補のキー階層として書き出す最大エントリ数。</summary>
        private const int MaxShapeEntries = 160;

        /// <summary>
        /// wham 以外の候補のキー階層上限。言語リソースのような巨大 JSON が候補に混ざるため、
        /// 取得元の判別に足りる数だけに絞ってログ肥大を防ぐ。
        /// </summary>
        private const int MaxSummaryShapeEntries = 24;

        /// <summary>キー階層をたどる最大の深さ。</summary>
        private const int MaxDepth = 5;

        /// <summary>
        /// 取得失敗時の診断ログを書き出す。書き出しに失敗しても本処理は止めない。
        /// </summary>
        /// <param name="candidates">スクレイパーが収集した生候補（ヘッダー行付き）</param>
        /// <param name="lastError">スクレイパーが組み立てたエラー説明（値を含まない文言のみ）</param>
        public static void WriteFailureLog(IReadOnlyList<string> candidates, string? lastError)
        {
            try
            {
                var builder = new StringBuilder();
                builder.AppendLine($"timestampUtc={DateTimeOffset.UtcNow:O}");
                builder.AppendLine("outcome=failure");
                builder.AppendLine($"candidateCount={candidates.Count}");
                builder.AppendLine($"lastError={lastError}");

                // wham usage 応答は本命のため、構造を最優先で記録する
                var wham = candidates.FirstOrDefault(
                    c => c.StartsWith(WhamUsagePrefix, StringComparison.OrdinalIgnoreCase));
                builder.AppendLine(wham == null
                    ? "whamUsage=absent"
                    : $"whamUsage=present {DescribeCandidate(wham, detailed: true)}");

                // 収集できた全候補の取得元とキー階層（浅め）を一覧にする
                foreach (var candidate in candidates)
                    builder.AppendLine($"candidate {DescribeCandidate(candidate, detailed: false)}");

                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(LogPath, builder.ToString(), new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // 診断ログは補助情報のため、書き出し失敗は無視する
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// 候補 1 件を「取得元・HTTP 状態・本文長・キー階層」に要約する。
        /// </summary>
        /// <param name="candidate">ヘッダー行付きの生候補</param>
        /// <param name="detailed">true なら全キー階層、false なら最上位キーのみ</param>
        private static string DescribeCandidate(string candidate, bool detailed)
        {
            var lineBreak = candidate.IndexOf('\n');
            var header = lineBreak >= 0 ? candidate[..lineBreak] : "(header-less)";
            var body = lineBreak >= 0 ? candidate[(lineBreak + 1)..] : candidate;

            // DOM テキスト候補は構造を持たないため長さだけ記録する
            if (candidate.StartsWith("__PAGETEXT__:", StringComparison.Ordinal))
                return $"source=__PAGETEXT__ kind=text length={candidate.Length}";

            var trimmed = body.TrimStart();
            if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
                return $"{header} kind=text length={body.Length}";

            try
            {
                using var document = JsonDocument.Parse(body);
                var shape = new List<string>();
                AppendShape(
                    document.RootElement,
                    "$",
                    shape,
                    detailed ? MaxDepth : 1,
                    detailed ? MaxShapeEntries : MaxSummaryShapeEntries);
                return $"{header} kind=json length={body.Length} shape={string.Join(',', shape)}";
            }
            catch (JsonException)
            {
                return $"{header} kind=json-invalid length={body.Length}";
            }
        }

        /// <summary>
        /// JSON のキー階層を「パス:型名」の列へ変換する。値そのものは出力しない。
        /// </summary>
        /// <param name="element">対象要素</param>
        /// <param name="path">現在のパス表記</param>
        /// <param name="shape">出力先</param>
        /// <param name="remainingDepth">残り探索深さ</param>
        /// <param name="maxEntries">出力する最大エントリ数</param>
        private static void AppendShape(
            JsonElement element,
            string path,
            List<string> shape,
            int remainingDepth,
            int maxEntries)
        {
            if (shape.Count >= maxEntries || remainingDepth <= 0)
                return;

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (shape.Count >= maxEntries)
                            return;
                        var childPath = $"{path}.{property.Name}";
                        shape.Add($"{childPath}:{property.Value.ValueKind}");
                        AppendShape(property.Value, childPath, shape, remainingDepth - 1, maxEntries);
                    }
                    break;

                case JsonValueKind.Array:
                    // 配列は要素数のみ記録し、先頭要素の構造だけたどる
                    var length = element.GetArrayLength();
                    shape.Add($"{path}[]:len={length}");
                    if (length > 0)
                        AppendShape(element[0], $"{path}[0]", shape, remainingDepth - 1, maxEntries);
                    break;
            }
        }
    }
}
