using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// GitHub REST API を呼び出して Copilot 使用状況を取得するクライアント。
    /// PAT（Personal Access Token）による Bearer 認証を使用する。
    ///
    /// 取得できる情報:
    ///   - 個人: PAT が有効かどうか・ログイン名
    ///   - 組織（org 名指定時）: 今サイクルのアクティブシート数 / 総シート数
    /// </summary>
    public class GitHubApiClient
    {
        // ────────────────────────────────────────────────────────────────
        // 定数
        // ────────────────────────────────────────────────────────────────

        /// <summary>GitHub REST API のベース URL</summary>
        private const string BaseUrl = "https://api.github.com";

        /// <summary>GitHub が要求する API バージョンヘッダー値</summary>
        private const string ApiVersion = "2022-11-28";

        /// <summary>HTTP 接続タイムアウト（秒）</summary>
        private const int TimeoutSeconds = 15;

        // ────────────────────────────────────────────────────────────────
        // プロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>直前の API 呼び出しで発生したエラーの説明。成功時は null。</summary>
        public string? LastError { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// GitHub API から Copilot 使用状況を非同期で取得する。
        ///
        /// 処理の流れ:
        ///   1. GET /user で PAT を検証し、ログイン名を取得する。
        ///   2. orgName が指定されていれば GET /orgs/{org}/copilot/billing でシート情報を取得する。
        ///
        /// </summary>
        /// <param name="pat">GitHub Personal Access Token（ghp_xxx 形式）</param>
        /// <param name="orgName">GitHub 組織名（個人プランの場合は空文字）</param>
        /// <returns>取得成功時は GitHubCopilotData、PAT 未設定や通信エラー時は null</returns>
        public async Task<GitHubCopilotData?> FetchCopilotDataAsync(string pat, string orgName)
        {
            LastError = null;

            // PAT が未設定の場合は即返却する
            if (string.IsNullOrWhiteSpace(pat))
            {
                LastError = "PAT未設定";
                return null;
            }

            try
            {
                using var client = CreateHttpClient(pat);

                // ── STEP 1: PAT 認証確認・ユーザー情報取得 ──
                var userResponse = await client.GetAsync($"{BaseUrl}/user");

                if (!userResponse.IsSuccessStatusCode)
                {
                    // 401 = PAT 無効 / 期限切れ、それ以外は汎用エラー
                    LastError = userResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "PAT認証エラー（期限切れまたは無効）"
                        : $"APIエラー ({(int)userResponse.StatusCode})";
                    return null;
                }

                var userJson = await userResponse.Content.ReadAsStringAsync();
                var userDoc = JsonDocument.Parse(userJson);
                var login = userDoc.RootElement.GetProperty("login").GetString() ?? "";

                var data = new GitHubCopilotData
                {
                    IsConnected = true,
                    UserLogin   = login
                };

                // ── STEP 2: 組織名が指定されていればシート情報を追加取得 ──
                if (!string.IsNullOrWhiteSpace(orgName))
                    await FetchOrgSeatsAsync(client, orgName.Trim(), data);

                return data;
            }
            catch (TaskCanceledException)
            {
                // タイムアウト（TimeoutSeconds 超過）
                LastError = "タイムアウト";
                return null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 内部ヘルパーメソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// GitHub API 用の認証済み HttpClient を生成する。
        /// 必須ヘッダー（Authorization, Accept, User-Agent, X-GitHub-Api-Version）を設定する。
        /// </summary>
        /// <param name="pat">認証に使う Personal Access Token</param>
        /// <returns>設定済みの HttpClient（呼び出し側で using する）</returns>
        private static HttpClient CreateHttpClient(string pat)
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };

            // Bearer 認証ヘッダー
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", pat);

            // GitHub 推奨 Accept ヘッダー
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            // GitHub は User-Agent ヘッダーを必須とする
            client.DefaultRequestHeaders.Add("User-Agent", "AIUsageOverlay");

            // API バージョン固定（後方互換性のため）
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);

            return client;
        }

        /// <summary>
        /// 組織の Copilot Billing API を呼び出してシート使用状況を data に書き込む。
        /// 権限不足・組織名誤りの場合は data.ErrorMessage に設定し、IsConnected は維持する。
        /// </summary>
        /// <param name="client">認証済み HttpClient（使い回す）</param>
        /// <param name="orgName">GitHub 組織名</param>
        /// <param name="data">結果を書き込む GitHubCopilotData</param>
        private async Task FetchOrgSeatsAsync(
            HttpClient client, string orgName, GitHubCopilotData data)
        {
            var resp = await client.GetAsync(
                $"{BaseUrl}/orgs/{orgName}/copilot/billing");

            if (!resp.IsSuccessStatusCode)
            {
                // 403 = 組織管理者権限不足、404 = 組織名誤り / Copilot 未導入
                data.ErrorMessage = resp.StatusCode switch
                {
                    System.Net.HttpStatusCode.Forbidden  => "組織管理者権限が必要です",
                    System.Net.HttpStatusCode.NotFound   => $"組織「{orgName}」が見つかりません",
                    _ => $"組織情報取得エラー ({(int)resp.StatusCode})"
                };
                return;
            }

            // シート情報を JSON からパースする
            var json = await resp.Content.ReadAsStringAsync();
            var doc  = JsonDocument.Parse(json);
            var breakdown = doc.RootElement.GetProperty("seat_breakdown");

            // total: 組織が購入した総シート数
            // active_this_cycle: 現サイクルで実際に使われているシート数
            data.SeatsTotal = breakdown.GetProperty("total").GetInt32();
            data.SeatsUsed  = breakdown.GetProperty("active_this_cycle").GetInt32();
            data.HasOrgData = true;
        }
    }
}
