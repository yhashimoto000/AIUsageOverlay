using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// WebView2 の生存確認と「致命的エラーか」の判定をまとめた共有ヘルパー。
    ///
    /// 背景:
    ///   WebView2 のブラウザプロセス（msedgewebview2.exe）は、ランタイム自動更新・スリープ復帰・
    ///   メモリ逼迫などで落ちることがある。落ちても C# 側の CoreWebView2 オブジェクト参照は残るため、
    ///   「初期化済み」と誤判定したまま Navigate / ExecuteScript が全て失敗し続ける。
    ///   その状態ではログイン画面も同じ死んだ Environment を共有するため黒画面になり、
    ///   アプリを再起動するまで復旧しない。この判定でプロセス死を検知して作り直す。
    ///
    /// 判定の方針:
    ///   誤って「死んでいる」と判定した場合のコストは WebView2 の再作成のみ（Cookie は
    ///   ユーザーデータフォルダに永続するのでログインは失われない）。逆に「生きている」と
    ///   誤判定すると恒久的に固まるため、判定できないときだけ生存側に倒す。
    /// </summary>
    internal static class WebView2Health
    {
        /// <summary>WebView2 ブラウザプロセスの実行ファイル名（PID 再利用による誤判定を防ぐために照合する）。</summary>
        private const string BrowserProcessName = "msedgewebview2";

        /// <summary>
        /// WebView2 のブラウザプロセスがまだ生きているかを判定する。
        /// </summary>
        /// <param name="browserProcessId">
        /// CoreWebView2.BrowserProcessId で取得した PID。0 以下（未取得）の場合は判定材料が
        /// ないため true（生存扱い）を返す。
        /// </param>
        /// <returns>生存している、または判定できない場合 true。終了済みと確認できた場合 false。</returns>
        public static bool IsBrowserProcessAlive(int browserProcessId)
        {
            if (browserProcessId <= 0) return true;

            try
            {
                using var process = Process.GetProcessById(browserProcessId);
                if (process.HasExited) return false;

                // PID は OS が再利用するため、同一 PID が別プロセスに割り当てられている可能性がある。
                // プロセス名まで一致した場合のみ「同じ WebView2 が生きている」とみなす。
                return string.Equals(process.ProcessName, BrowserProcessName,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                // 該当 PID のプロセスが存在しない（＝終了済み）
                return false;
            }
            catch (InvalidOperationException)
            {
                // プロセスオブジェクトが既に無効（＝終了済み扱い）
                return false;
            }
            catch (Win32Exception)
            {
                // 権限不足などで問い合わせできない場合は、無駄な再作成を避けるため生存扱いにする
                return true;
            }
        }

        /// <summary>
        /// 例外が「WebView2 を掴み直さないと回復しない種類」かを判定する。
        /// これらが出た後は同じ CoreWebView2 を使い続けても失敗し続けるため、再作成を要求する。
        /// </summary>
        /// <param name="ex">取得処理で捕捉した例外</param>
        /// <returns>WebView2 の再作成が必要なら true</returns>
        public static bool IsFatalWebViewError(Exception ex)
            => ex is COMException            // ブラウザプロセス断・RPC 切断
                  or SEHException            // ネイティブ層の異常終了
                  or ObjectDisposedException // 破棄済みコントロールへの操作
                  or InvalidOperationException; // CoreWebView2 未初期化・無効状態への操作
    }
}
