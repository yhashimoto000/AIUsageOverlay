using System.IO;
using System.Text.Json;
using ClaudeUsageOverlay.Models;

namespace ClaudeUsageOverlay.Services
{
    /// <summary>
    /// Claude の使用量追跡・管理を担当するサービスクラス。
    /// アプリ起動中の経過時間をセッション使用量・週間使用量として記録し、
    /// JSON ファイルに永続化することで再起動後も累計を維持する。
    /// </summary>
    public class UsageService
    {
        // ────────────────────────────────────────────────────────────────
        // 定数 / ファイルパス
        // ────────────────────────────────────────────────────────────────

        /// <summary>設定・使用量ファイルを保存するフォルダパス（%AppData%\ClaudeUsageOverlay）</summary>
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageOverlay");

        /// <summary>設定ファイルのフルパス</summary>
        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");

        /// <summary>使用量記録ファイルのフルパス</summary>
        private static readonly string UsageFilePath = Path.Combine(AppDataFolder, "usage.json");

        /// <summary>JSON シリアライズ時のオプション（インデント付きで可読性を確保）</summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>現在の設定データ（メモリキャッシュ）</summary>
        private AppSettings _settings;

        /// <summary>現在の使用量記録（メモリキャッシュ）</summary>
        private UsageRecord _usageRecord;

        /// <summary>前回 UpdateAndGetUsage() を呼び出した日時（差分時間の計算に使用）</summary>
        private DateTime _lastUpdateTime;

        // ────────────────────────────────────────────────────────────────
        // コンストラクタ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// UsageService を初期化する。
        /// AppData フォルダを作成し、設定・使用量ファイルを読み込む。
        /// </summary>
        public UsageService()
        {
            // AppData フォルダが存在しない場合は作成する
            Directory.CreateDirectory(AppDataFolder);

            // 設定と使用量記録をファイルから読み込む
            _settings = LoadSettings();
            _usageRecord = LoadUsageRecord();
            _lastUpdateTime = DateTime.Now;

            // 週が変わっていれば週間使用量を自動リセットする
            ResetWeeklyIfNeeded();
        }

        // ────────────────────────────────────────────────────────────────
        // フィールド追加
        // ────────────────────────────────────────────────────────────────

        /// <summary>WebView2 を使って claude.ai API を呼び出すクライアント</summary>
        private readonly ClaudeApiClient _apiClient = new();

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 現在のアプリ設定を取得する（読み取り専用アクセス用）
        /// </summary>
        /// <returns>AppSettings オブジェクト</returns>
        public AppSettings GetSettings() => _settings;

        /// <summary>
        /// 直前の API 呼び出しで発生したエラーの説明を取得する。
        /// 成功時は null。例: "未ログイン" / "取得タイムアウト" / "ParseError"
        /// </summary>
        public string? GetLastApiError() => _apiClient.LastError;

        /// <summary>
        /// ログイン用に WebView2 ウィンドウを表示する。
        /// ユーザーがログインするとセッションが永続保存され、次回以降は自動認証される。
        /// </summary>
        public async Task ShowLoginWindowAsync() => await _apiClient.ShowLoginWindowAsync();

        /// <summary>
        /// アプリ設定を更新してファイルに保存する。
        /// SettingsWindow から呼び出される。
        /// </summary>
        /// <param name="settings">保存する設定オブジェクト</param>
        public void SaveSettings(AppSettings settings)
        {
            _settings = settings;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }

        /// <summary>
        /// 使用量データを更新し、UI 表示用の計算済み値を返す。
        /// 前回呼び出しからの経過時間を使用量に加算する。
        /// タイマーのティックごとに ViewModel から呼び出される。
        /// </summary>
        /// <returns>
        /// (sessionRatio, sessionRemainingMinutes, weeklyRatio, weeklyRemainingMinutes) のタプル
        /// - sessionRatio: セッション使用率（0.0 ～ 1.0）
        /// - sessionRemainingMinutes: セッション残り時間（分）
        /// - weeklyRatio: 週間使用率（0.0 ～ 1.0）
        /// - weeklyRemainingMinutes: 週間リセットまでの残り時間（分）
        /// </returns>
        public (double sessionRatio, int sessionRemainingMinutes,
                double weeklyRatio, int weeklyRemainingMinutes) UpdateAndGetUsage()
        {
            var now = DateTime.Now;

            // 前回更新からの経過時間（分）を算出する
            var elapsedMinutes = (now - _lastUpdateTime).TotalMinutes;
            _lastUpdateTime = now;

            // 週をまたいでいれば週間データをリセットする
            ResetWeeklyIfNeeded();

            // ── セッション計算 ──
            // セッション開始からの経過時間（分）
            var sessionElapsed = (now - _usageRecord.SessionStartTime).TotalMinutes;

            // セッション制限を超えていたら自動リセットする
            if (sessionElapsed >= _settings.SessionLimitMinutes)
            {
                ResetSession();
                sessionElapsed = 0;
            }

            // ── 週間計算 ──
            // アプリ起動中の経過時間のみ加算する（アプリ停止中はカウントしない）
            _usageRecord.WeeklyUsedMinutes += elapsedMinutes;
            _usageRecord.LastActiveTime = now;

            // 週間上限を超えないようにクランプする
            if (_usageRecord.WeeklyUsedMinutes > _settings.WeeklyLimitMinutes)
                _usageRecord.WeeklyUsedMinutes = _settings.WeeklyLimitMinutes;

            // 変更をファイルに保存する
            SaveUsageRecord();

            // ── 戻り値を計算 ──

            // セッション使用率と残り時間
            double sessionRatio = Math.Min(1.0, sessionElapsed / _settings.SessionLimitMinutes);
            int sessionRemainingMinutes = Math.Max(0,
                (int)(_settings.SessionLimitMinutes - sessionElapsed));

            // 週間使用率と週末（次の月曜日）までの残り時間
            double weeklyRatio = Math.Min(1.0,
                _usageRecord.WeeklyUsedMinutes / _settings.WeeklyLimitMinutes);
            var nextMonday = _usageRecord.WeekStartDate.AddDays(7);
            int weeklyRemainingMinutes = Math.Max(0, (int)(nextMonday - now).TotalMinutes);

            return (sessionRatio, sessionRemainingMinutes, weeklyRatio, weeklyRemainingMinutes);
        }

        /// <summary>
        /// 使用量データを非同期で取得する。
        /// Cookie と OrganizationId が両方設定されている場合は claude.ai JSON API から実データを取得する。
        /// 未設定またはAPI 失敗時はローカル時間計測にフォールバックする。
        /// ViewModel の Timer Tick から呼び出される主要メソッド。
        /// </summary>
        /// <returns>
        /// (sessionRatio, sessionRemainingMinutes, weeklyRatio, weeklyRemainingMinutes, isFromApi) のタプル。
        /// isFromApi が true の場合は claude.ai からの実データ、false の場合はローカル計測値。
        /// </returns>
        public async Task<(double sessionRatio, int sessionRemainingMinutes,
                           double weeklyRatio, int weeklyRemainingMinutes, bool isFromApi)>
            UpdateAndGetUsageAsync()
        {
            // ── WebView2 で claude.ai から実データを取得する ──
            {
                var scraped = await _apiClient.FetchUsageAsync();
                if (scraped != null)
                {
                    // API 取得成功: 実データをそのまま返す（ローカル追跡は更新しない）
                    return (
                        scraped.SessionPercent / 100.0,
                        scraped.SessionRemainingMinutes,
                        scraped.WeeklyPercent / 100.0,
                        scraped.WeeklyRemainingMinutes,
                        true   // isFromApi = true
                    );
                }
                // API 失敗（Cookie 期限切れ・ネットワークエラー・パースエラー）はフォールバックする
            }

            // ── フォールバック: ローカル時間計測 ──
            var local = UpdateAndGetUsage();
            return (local.sessionRatio, local.sessionRemainingMinutes,
                    local.weeklyRatio, local.weeklyRemainingMinutes,
                    false);  // isFromApi = false
        }

        /// <summary>
        /// セッションを手動リセットする。
        /// セッション開始時刻を現在時刻に更新し、ファイルを保存する。
        /// </summary>
        public void ResetSession()
        {
            _usageRecord.SessionStartTime = DateTime.Now;
            _lastUpdateTime = DateTime.Now;
            SaveUsageRecord();
        }

        /// <summary>
        /// 週間使用量を手動リセットする（デバッグ・テスト用途）。
        /// </summary>
        public void ResetWeekly()
        {
            _usageRecord.WeekStartDate = UsageRecord.GetThisMonday();
            _usageRecord.WeeklyUsedMinutes = 0;
            SaveUsageRecord();
        }

        // ────────────────────────────────────────────────────────────────
        // 内部ヘルパーメソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 現在の日付が記録された WeekStartDate と異なる週であれば、
        /// 週間使用量を自動リセットする。
        /// </summary>
        private void ResetWeeklyIfNeeded()
        {
            var thisMonday = UsageRecord.GetThisMonday();
            if (_usageRecord.WeekStartDate.Date != thisMonday.Date)
            {
                _usageRecord.WeekStartDate = thisMonday;
                _usageRecord.WeeklyUsedMinutes = 0;
                SaveUsageRecord();
            }
        }

        /// <summary>
        /// 設定ファイルを読み込む。
        /// ファイルが存在しない場合・読み込みエラーの場合はデフォルト設定を返す。
        /// </summary>
        /// <returns>読み込んだ AppSettings、または新規デフォルト設定</returns>
        private AppSettings LoadSettings()
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();

            try
            {
                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                // 読み込みエラーはデフォルト設定にフォールバックする
                return new AppSettings();
            }
        }

        /// <summary>
        /// 使用量記録ファイルを読み込む。
        /// ファイルが存在しない場合・読み込みエラーの場合は新規レコードを返す。
        /// </summary>
        /// <returns>読み込んだ UsageRecord、または新規レコード</returns>
        private UsageRecord LoadUsageRecord()
        {
            if (!File.Exists(UsageFilePath))
                return new UsageRecord();

            try
            {
                var json = File.ReadAllText(UsageFilePath);
                return JsonSerializer.Deserialize<UsageRecord>(json) ?? new UsageRecord();
            }
            catch
            {
                // 読み込みエラーは新規レコードにフォールバックする
                return new UsageRecord();
            }
        }

        /// <summary>
        /// 現在の使用量記録をファイルに保存する。
        /// 書き込みエラーは静かに無視する（表示には影響しない）。
        /// </summary>
        private void SaveUsageRecord()
        {
            try
            {
                var json = JsonSerializer.Serialize(_usageRecord, JsonOptions);
                File.WriteAllText(UsageFilePath, json);
            }
            catch
            {
                // ファイル保存エラーは無視する（次回起動時にデータが失われる可能性がある）
            }
        }
    }
}
