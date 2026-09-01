using System.IO;
using System.Text.Json;
using System.Threading;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>
    /// セッション統計キャッシュの読み書きを担当する（設計書 6 章 / 8.6）。
    ///
    /// 書き込みは一時ファイルへ出してから置換する。多重起動が実際に観測されているため
    /// （learnings.md 2026-08-26）、書き込みは名前付き Mutex で保護する。
    /// </summary>
    public class SessionStatsCacheStore
    {
        /// <summary>多重起動時の同時集計を防ぐ名前付き Mutex の名前。</summary>
        private const string MutexName = @"Global\AIUsageOverlay.SessionStats";

        /// <summary>キャッシュのファイル名。既存の history.json とは別物（設計書 3.1）。</summary>
        private const string CacheFileName = "session-stats-cache.json";

        /// <summary>
        /// キャッシュの書式。サイズを優先して整形しない。
        /// Encoder は既定のままとし、エスケープ規則を既存ファイルと 1 つに保つ（設計書 3.3.2）。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        /// <summary>設定・計測ファイルを置くフォルダ。</summary>
        private readonly string _folder;

        /// <summary>キャッシュ本体のフルパス。</summary>
        private readonly string _cachePath;

        /// <summary>破損キャッシュの退避先。世代管理はしない（毎回上書き）。</summary>
        private readonly string _backupPath;

        /// <summary>置換前に書き出す一時ファイル。</summary>
        private readonly string _tempPath;

        /// <summary>既定の保存先（%AppData%\AIUsageOverlay）で初期化する。</summary>
        public SessionStatsCacheStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AIUsageOverlay"))
        {
        }

        /// <summary>保存先フォルダを指定して初期化する（テスト用）。</summary>
        /// <param name="folder">キャッシュを置くフォルダ</param>
        public SessionStatsCacheStore(string folder)
        {
            _folder = folder;
            _cachePath = Path.Combine(folder, CacheFileName);
            _backupPath = _cachePath + ".bak";
            _tempPath = _cachePath + ".tmp";
        }

        /// <summary>キャッシュ本体のフルパス。</summary>
        public string CachePath => _cachePath;

        /// <summary>直近の読み込みでキャッシュが壊れていたか（データ品質タブに出す）。</summary>
        public bool LastLoadWasCorrupted { get; private set; }

        /// <summary>
        /// 集計処理を保護する名前付き Mutex を取得する。
        /// 取得できなければ null を返す。呼び出し側はレポート生成を中止する（設計書 8.6）。
        /// </summary>
        /// <returns>取得できた Mutex。取得できなければ null</returns>
        public static Mutex? TryAcquireLock()
        {
            var mutex = new Mutex(false, MutexName);
            try
            {
                if (mutex.WaitOne(TimeSpan.Zero))
                {
                    return mutex;
                }
            }
            catch (AbandonedMutexException)
            {
                // 前のプロセスが解放せずに落ちた場合。所有権は得られているので続行してよい
                return mutex;
            }

            mutex.Dispose();
            return null;
        }

        /// <summary>
        /// キャッシュを読み込む。存在しない場合は null。
        /// 壊れていた場合は .bak へ上書き退避してから null を返し、全件再パースさせる（設計書 8.6）。
        /// </summary>
        /// <returns>読み込んだキャッシュ。無い・壊れている場合は null</returns>
        public SessionStatsCache? Load()
        {
            LastLoadWasCorrupted = false;
            CleanupTempFile();

            if (!File.Exists(_cachePath))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(_cachePath);
                var cache = JsonSerializer.Deserialize<SessionStatsCache>(stream);
                if (cache is null)
                {
                    QuarantineCorruptedCache();
                    return null;
                }
                if (cache.SchemaVersion != SessionStatsCache.CurrentSchemaVersion)
                {
                    // 版が違えば全件再パースする。利用者には確認しない（可逆であり結果は同じ）
                    return null;
                }
                return cache;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                QuarantineCorruptedCache();
                return null;
            }
        }

        /// <summary>
        /// キャッシュを保存する。一時ファイルへ書いてから置換し、
        /// 書き込み途中で落ちても既存キャッシュを壊さないようにする。
        /// </summary>
        /// <param name="cache">保存するキャッシュ</param>
        public void Save(SessionStatsCache cache)
        {
            Directory.CreateDirectory(_folder);

            using (var stream = File.Create(_tempPath))
            {
                JsonSerializer.Serialize(stream, cache, JsonOptions);
            }

            if (File.Exists(_cachePath))
            {
                // 置換前の内容を .bak へ退避する（世代管理はしない）
                File.Replace(_tempPath, _cachePath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(_tempPath, _cachePath);
            }
        }

        /// <summary>壊れたキャッシュを .bak へ上書き退避する。</summary>
        private void QuarantineCorruptedCache()
        {
            LastLoadWasCorrupted = true;
            try
            {
                File.Copy(_cachePath, _backupPath, overwrite: true);
                File.Delete(_cachePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 退避に失敗しても全件再パースは可能なので、処理は続行する
            }
        }

        /// <summary>置換前に落ちて残った一時ファイルを削除する（設計書 8.6）。</summary>
        private void CleanupTempFile()
        {
            if (!File.Exists(_tempPath))
            {
                return;
            }
            try
            {
                File.Delete(_tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 削除できなくても書き込み時に上書きされるため、処理は続行する
            }
        }
    }
}
