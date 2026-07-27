using AIUsageOverlay.Services.Parsing;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// GitHub Releases API から取得した更新候補を表す。
    /// URL は Parser の HTTPS・許可ホスト検証を通過した値だけを保持し、
    /// 不正または欠落している場合は null のまま通知側へ渡す。
    /// </summary>
    public sealed class UpdateInfo
    {
        /// <summary>Release の tag_name から解析した最新バージョン。</summary>
        public required SemVer LatestVersion { get; init; }

        /// <summary>検証済み zip アセットのダウンロード URL。利用できない場合は null。</summary>
        public Uri? DownloadUrl { get; init; }

        /// <summary>選択した zip アセット名。zip が存在しない場合は null。</summary>
        public string? AssetName { get; init; }

        /// <summary>GitHub API が返した zip アセットのバイト数。欠落時は null。</summary>
        public long? Size { get; init; }

        /// <summary>検証済み GitHub Release ページ URL。利用できない場合は null。</summary>
        public Uri? HtmlUrl { get; init; }
    }
}
