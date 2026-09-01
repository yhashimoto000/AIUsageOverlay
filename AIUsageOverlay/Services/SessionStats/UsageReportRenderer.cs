using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>
    /// 集計結果を自己完結 HTML へ書き出す（設計書 8.7 / 8.8）。
    ///
    /// テンプレートは埋め込みリソースとして持ち、外部参照を一切含まない。
    /// 履歴由来の文字列は指示文・パス・コマンドを含むため、
    /// 埋め込み時のエスケープは本クラスの 1 箇所に集約する。
    /// 埋め込み処理を通らない書き出し経路を作らないこと。
    /// </summary>
    public class UsageReportRenderer
    {
        /// <summary>テンプレート内の差し込み位置を示すプレースホルダ。</summary>
        private const string Placeholder = "__REPORT_DATA__";

        /// <summary>埋め込みリソースの論理名。</summary>
        private const string TemplateResourceName = "AIUsageOverlay.Resources.Report.report-template.html";

        /// <summary>レポートの出力先フォルダ名。</summary>
        private const string ReportFolderName = "reports";

        /// <summary>
        /// 保持するレポートの件数。これより古いものは生成時に削除する。
        ///
        /// レポートは session-stats-cache.json からの派生物であり、
        /// キャッシュは履歴ファイルが消えた分の集計も保持し続ける（設計書 6 章）。
        /// そのため古い HTML を消しても、次回の生成で同じ期間を作り直せる。
        /// </summary>
        private const int KeepReportCount = 5;

        /// <summary>レポートの既定の出力先（%AppData% 配下 AIUsageOverlay の reports フォルダ）。</summary>
        public static string DefaultOutputFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIUsageOverlay", ReportFolderName);

        /// <summary>
        /// JSON 化の設定。プロパティ名は HTML 側の参照に合わせて camelCase にする。
        /// Encoder は既定のままとし、非 ASCII を \uXXXX へ展開する
        /// （既存ファイルとエスケープ規則を 1 つに保つ。設計書 3.3.2）。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        /// <summary>
        /// レポートを HTML として書き出し、生成したファイルのパスを返す。
        /// </summary>
        /// <param name="report">埋め込むレポート</param>
        /// <param name="outputFolder">出力先フォルダ。null なら %AppData%\AIUsageOverlay\reports</param>
        /// <param name="nowLocal">ファイル名に使う時刻</param>
        /// <returns>生成した HTML のフルパス</returns>
        public string Render(UsageReport report, string? outputFolder, DateTime nowLocal)
        {
            var folder = outputFolder ?? DefaultOutputFolder;
            Directory.CreateDirectory(folder);

            var html = BuildHtml(report);
            var path = Path.Combine(folder, $"usage-{nowLocal:yyyyMMdd-HHmm}.html");
            File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            PruneOldReports(folder, KeepReportCount);
            return path;
        }

        /// <summary>
        /// 古いレポートを削除し、新しいものから指定件数だけ残す。
        ///
        /// 対象は本機能が生成した「usage-*.html」だけに限定する。
        /// 利用者が同じフォルダへ置いた別のファイルは触らない。
        /// 削除に失敗しても生成そのものは成功しているため、例外は握って続行する。
        /// </summary>
        /// <param name="folder">レポートの出力先</param>
        /// <param name="keep">残す件数</param>
        internal static void PruneOldReports(string folder, int keep)
        {
            List<FileInfo> obsolete;
            try
            {
                // ファイル名に生成日時（usage-yyyyMMdd-HHmm）が入っているため、
                // 名前の降順がそのまま新しい順になる。
                // 更新時刻はコピーや同期で変わり得るので主キーにしない。
                obsolete = Directory.GetFiles(folder, "usage-*.html")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                    .ThenByDescending(f => f.LastWriteTimeUtc)
                    .Skip(Math.Max(1, keep))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var file in obsolete)
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 開かれている等で消せない場合は次回の生成で消えるため、ここでは何もしない
                }
            }
        }

        /// <summary>テンプレートへレポートを差し込んだ HTML 文字列を作る。</summary>
        /// <param name="report">埋め込むレポート</param>
        /// <returns>完成した HTML</returns>
        public string BuildHtml(UsageReport report)
        {
            var template = LoadTemplate();
            var json = EscapeForEmbedding(JsonSerializer.Serialize(report, JsonOptions));
            return template.Replace(Placeholder, json, StringComparison.Ordinal);
        }

        /// <summary>
        /// JSON 文字列を script 要素へ安全に埋め込める形へ変換する（設計書 8.7 / R6）。
        ///
        /// - 小なり記号を潰せば「&lt;/script&gt;」は成立しない
        /// - 大なり記号とアンパサンドは HTML の実体参照として解釈され得る
        /// - U+2028 / U+2029 は JavaScript の行終端子として扱われる
        ///
        /// いずれも JSON としては合法だが、素で書き出すと HTML が壊れる。
        /// 変換後も JSON.parse で元の文字へ戻るため、画面表示は変わらない。
        /// </summary>
        /// <param name="json">シリアライズ済みの JSON</param>
        /// <returns>埋め込み可能な JSON</returns>
        public static string EscapeForEmbedding(string json)
        {
            var sb = new StringBuilder(json.Length + 64);
            foreach (var c in json)
            {
                switch (c)
                {
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    case '&': sb.Append("\\u0026"); break;
                    default:
                        // U+2028 / U+2029 は JavaScript の行終端子として解釈されるため、
                        // JSON としては合法でも素で埋め込むと壊れる。ソースに実体を書かず符号で比較する。
                        if (c == '\u2028') { sb.Append("\\u2028"); }
                        else if (c == '\u2029') { sb.Append("\\u2029"); }
                        else { sb.Append(c); }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>埋め込みリソースからテンプレートを読み出す。</summary>
        /// <returns>テンプレートの HTML</returns>
        /// <exception cref="InvalidOperationException">リソースが見つからない場合</exception>
        private static string LoadTemplate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(TemplateResourceName)
                ?? throw new InvalidOperationException(
                    $"レポートテンプレートが埋め込まれていません: {TemplateResourceName}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
