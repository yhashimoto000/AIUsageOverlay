using System.Text.Json;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// GitHub Releases API の生 JSON を <see cref="UpdateInfo"/> に変換する。
    /// HTTP 通信や状態変更を行わない純粋な Parser とし、不正入力や必須項目欠落時は
    /// 例外を外へ漏らさず null を返す。
    /// </summary>
    public static class GitHubReleaseParser
    {
        /// <summary>
        /// Release ページとアセット URL に許可するホスト。
        /// 部分一致ではなく <see cref="string.Equals(string?, string?, StringComparison)"/>
        /// による完全一致で検証し、類似ドメインへの誘導を防止する。
        /// </summary>
        private static readonly string[] AllowedHosts =
        [
            "github.com",
            "api.github.com",
            "objects.githubusercontent.com"
        ];

        /// <summary>
        /// GitHub Releases API の JSON から更新候補を解析する。
        /// tag_name が厳密な3成分 SemVer でない場合は比較不能なため null を返す。
        /// URL のみが不正な場合はそのフィールドだけを null にし、版数に基づく
        /// 更新検知と通知は継続できるようにする。
        /// </summary>
        /// <param name="json">/releases/latest が返した生 JSON。</param>
        /// <returns>解析済み更新情報。必須項目を解析できない場合は null。</returns>
        public static UpdateInfo? Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !TryGetString(root, "tag_name", out var tagName)
                    || !SemVer.TryParse(tagName, out var latestVersion)
                    || latestVersion is null)
                {
                    return null;
                }

                var htmlUrl = TryGetString(root, "html_url", out var htmlUrlText)
                    ? ParseAllowedHttpsUri(htmlUrlText)
                    : null;

                string? assetName = null;
                Uri? downloadUrl = null;
                long? size = null;

                if (root.TryGetProperty("assets", out var assets)
                    && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.ValueKind != JsonValueKind.Object
                            || !TryGetString(asset, "name", out var candidateName)
                            || !candidateName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        assetName = candidateName;
                        downloadUrl = TryGetString(
                            asset,
                            "browser_download_url",
                            out var downloadUrlText)
                            ? ParseAllowedHttpsUri(downloadUrlText)
                            : null;
                        size = asset.TryGetProperty("size", out var sizeElement)
                            && sizeElement.ValueKind == JsonValueKind.Number
                            && sizeElement.TryGetInt64(out var candidateSize)
                            && candidateSize >= 0
                                ? candidateSize
                                : null;
                        break;
                    }
                }

                return new UpdateInfo
                {
                    LatestVersion = latestVersion,
                    DownloadUrl = downloadUrl,
                    AssetName = assetName,
                    Size = size,
                    HtmlUrl = htmlUrl
                };
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// 指定プロパティが文字列として存在する場合に値を返す。
        /// JSON null や別型は欠落と同様に扱い、GitHub 側のスキーマ変化に寛容にする。
        /// </summary>
        private static bool TryGetString(
            JsonElement element,
            string propertyName,
            out string value)
        {
            value = "";
            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var candidate = property.GetString();
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            value = candidate;
            return true;
        }

        /// <summary>
        /// 文字列が絶対 HTTPS URI であり、ホストが許可リストのいずれかと
        /// 大文字小文字を無視して完全一致する場合だけ URI を返す。
        /// </summary>
        private static Uri? ParseAllowedHttpsUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (var allowedHost in AllowedHosts)
            {
                if (uri.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase))
                    return uri;
            }

            return null;
        }
    }
}
