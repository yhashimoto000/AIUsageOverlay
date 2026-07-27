using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.Parsing;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// GitHub Releases API から公開済み安定版を取得し、実行中アプリの版と比較する。
    /// P5 ではメタデータの GET と比較だけを担当し、アセットのダウンロードや
    /// インストール先への書き込みは行わない。
    /// </summary>
    public sealed class UpdateCheckService
    {
        /// <summary>公開リポジトリの最新安定版を返す GitHub Releases API。</summary>
        private static readonly Uri LatestReleaseUri = new(
            "https://api.github.com/repos/yhashimoto000/AIUsageOverlay/releases/latest");

        /// <summary>AssemblyInformationalVersion から取得した実行中アプリの版。</summary>
        private static readonly SemVer? CurrentVersion = ReadCurrentVersion();

        /// <summary>
        /// 接続再利用とソケット枯渇防止のため全チェックで共有する HTTP クライアント。
        /// GitHub API 必須の User-Agent、推奨 Accept、短いタイムアウトを固定する。
        /// </summary>
        private static readonly HttpClient HttpClient = CreateHttpClient();

        /// <summary>
        /// 直前の更新確認で発生した利用者向けエラー。成功時は null。
        /// 自動確認では表示せず、手動確認 UI が結果を説明するために参照する。
        /// </summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// 実行中アセンブリのバージョンを返す。
        /// InformationalVersion が不正な場合は null を返し、誤った更新比較を行わない。
        /// </summary>
        public static SemVer? GetCurrentVersion() => CurrentVersion;

        /// <summary>
        /// GitHub の最新安定版を確認し、現在版より新しい場合だけ更新情報を返す。
        /// ネットワーク・HTTP・JSON・バージョン解析の失敗は診断ログへ理由を残して
        /// null を返し、アプリを停止させず次回チェックへ委ねる。
        /// </summary>
        /// <param name="cancellationToken">チェックを中止するためのトークン。</param>
        /// <returns>更新がある場合は更新情報。それ以外または失敗時は null。</returns>
        public async Task<UpdateInfo?> CheckForUpdateAsync(
            CancellationToken cancellationToken = default)
        {
            LastError = null;

            if (CurrentVersion is null)
            {
                LastError = "現在のバージョンを確認できないため、更新を確認できませんでした。";
                Trace.TraceWarning(
                    "Update check skipped: AssemblyInformationalVersion is not a valid three-part SemVer.");
                return null;
            }

            try
            {
                using var response = await HttpClient.GetAsync(
                    LatestReleaseUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var updateInfo = GitHubReleaseParser.Parse(json);
                if (updateInfo is null)
                {
                    LastError = "GitHub Release の更新情報を解析できませんでした。";
                    Trace.TraceWarning(
                        "Update check failed: GitHub release metadata could not be parsed.");
                    return null;
                }

                return updateInfo.LatestVersion > CurrentVersion
                    ? updateInfo
                    : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                LastError = "更新確認がキャンセルされました。";
                Trace.TraceInformation("Update check canceled by the caller.");
                return null;
            }
            catch (OperationCanceledException ex)
            {
                LastError = "更新確認がタイムアウトしました。";
                Trace.TraceWarning(
                    $"Update check timed out: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            catch (HttpRequestException ex)
            {
                LastError = "GitHub に接続できなかったため、更新を確認できませんでした。";
                Trace.TraceWarning(
                    $"Update check HTTP failure: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LastError = "予期しないエラーにより、更新を確認できませんでした。";
                Trace.TraceWarning(
                    $"Update check unexpected failure: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// GitHub API 用の共有 HTTP クライアントを生成する。
        /// 認証情報や利用状況データは付与せず、公開メタデータへの GET だけに使用する。
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            var version = CurrentVersion?.ToString() ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("AIUsageOverlay", version));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        /// <summary>
        /// SingleFile でも利用できる AssemblyInformationalVersionAttribute から自バージョンを読む。
        /// SDK が付加する +githash などの build metadata は比較前に除去する。
        /// </summary>
        private static SemVer? ReadCurrentVersion()
        {
            var informationalVersion = typeof(UpdateCheckService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informationalVersion))
                return null;

            var buildMetadataSeparator = informationalVersion.IndexOf('+');
            var versionText = buildMetadataSeparator >= 0
                ? informationalVersion[..buildMetadataSeparator]
                : informationalVersion;

            return SemVer.TryParse(versionText, out var version)
                ? version
                : null;
        }
    }
}
