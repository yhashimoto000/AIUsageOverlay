using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>
    /// プロジェクトパスの正規化と親子の紐付けを担当する（設計書 5.3 / 5.4）。
    ///
    /// 重複排除そのもの（Claude Code の requestId、Codex のスレッド ID）は
    /// ファイルを読む段階で行う。本クラスは全セッションが揃ったあとの後処理を受け持つ。
    /// </summary>
    public static class SessionNormalizer
    {
        /// <summary>cwd が記録されていないセッションをまとめる表示名。</summary>
        public const string UnknownProject = "(プロジェクト不明)";

        /// <summary>モデル名が記録されていない場合の表示名。</summary>
        public const string UnknownModel = "(モデル不明)";

        /// <summary>
        /// プロジェクトパスの比較用キーを作る（設計書 5.3）。
        ///
        /// 1. ドライブレターを大文字化する
        /// 2. 区切りを円記号に統一する
        /// 3. 末尾の区切りを除去する
        /// 4. 大文字小文字は 1 以外では変換しない（表示が読みにくくなるため）
        ///
        /// Windows のファイルシステムは大文字小文字を区別しないため、
        /// 比較は最終的に大文字化した文字列で行う。表示用の原文は別に保持する。
        /// </summary>
        /// <param name="cwd">履歴に記録された作業ディレクトリ</param>
        /// <returns>比較用キー。cwd が空なら「(プロジェクト不明)」</returns>
        public static string NormalizeProjectKey(string? cwd)
        {
            var display = NormalizeProjectDisplay(cwd);
            return display == UnknownProject ? UnknownProject : display.ToUpperInvariant();
        }

        /// <summary>
        /// プロジェクトパスの表示用文字列を作る。
        /// 区切りの統一と末尾除去、ドライブレターの大文字化までは行い、
        /// それ以外の大文字小文字は原文のまま残す。
        /// </summary>
        /// <param name="cwd">履歴に記録された作業ディレクトリ</param>
        /// <returns>表示用文字列。cwd が空なら「(プロジェクト不明)」</returns>
        public static string NormalizeProjectDisplay(string? cwd)
        {
            if (string.IsNullOrWhiteSpace(cwd))
            {
                return UnknownProject;
            }

            var s = cwd.Trim().Replace('/', '\\');
            while (s.Length > 1 && s.EndsWith('\\'))
            {
                s = s[..^1];
            }
            // ドライブレターのみ大文字化する（c:\… と C:\… を同一視するため）
            if (s.Length >= 2 && s[1] == ':' && char.IsLetter(s[0]))
            {
                s = char.ToUpperInvariant(s[0]) + s[1..];
            }
            return s.Length == 0 ? UnknownProject : s;
        }

        /// <summary>
        /// 全セッションのプロジェクト表示名を「最初に観測した表記」へ揃える（設計書 5.3 / C1）。
        /// 最初に観測した方を採るため、開始時刻の昇順で走査する。
        /// </summary>
        /// <param name="sessions">対象セッション</param>
        public static void UnifyProjectDisplayNames(IEnumerable<SessionStatsRecord> sessions)
        {
            var firstSeen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in sessions.OrderBy(x => x.StartUtc))
            {
                if (string.IsNullOrEmpty(s.ProjectKey))
                {
                    continue;
                }
                if (firstSeen.TryGetValue(s.ProjectKey, out var display))
                {
                    s.ProjectDisplay = display;
                }
                else
                {
                    firstSeen[s.ProjectKey] = s.ProjectDisplay;
                }
            }
        }

        /// <summary>
        /// 親子の紐付けを解決する（設計書 5.4）。
        ///
        /// パース時点では親を「ツール側の ID」で持っている。ここで実在するセッションの
        /// キーへ解決し、親が見つからない子は独立したセッションとして扱う。
        /// サブエージェントは重複ではなく独立した消費者であるため、除外はしない（B0/Q2）。
        /// </summary>
        /// <param name="sessions">対象セッション</param>
        public static void ResolveParentLinks(IReadOnlyCollection<SessionStatsRecord> sessions)
        {
            var byKey = new Dictionary<string, SessionStatsRecord>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                byKey[s.Key] = s;
            }

            foreach (var s in sessions)
            {
                if (string.IsNullOrEmpty(s.ParentKey))
                {
                    continue;
                }
                // 親が履歴に残っていない（削除された・保持期間外）場合は独立扱いにする
                if (!byKey.ContainsKey(s.ParentKey) || s.ParentKey == s.Key)
                {
                    s.ParentKey = null;
                }
            }

            // 循環参照が残ると累計値の算出が無限ループになるため、ここで断ち切る
            foreach (var s in sessions)
            {
                if (HasCycle(s, byKey))
                {
                    s.ParentKey = null;
                }
            }
        }

        /// <summary>親を辿ったときに循環しているかどうかを判定する。</summary>
        /// <param name="start">起点のセッション</param>
        /// <param name="byKey">キーからセッションを引く辞書</param>
        private static bool HasCycle(
            SessionStatsRecord start,
            IReadOnlyDictionary<string, SessionStatsRecord> byKey)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start.Key };
            var current = start;
            while (!string.IsNullOrEmpty(current.ParentKey) &&
                   byKey.TryGetValue(current.ParentKey, out var parent))
            {
                if (!visited.Add(parent.Key))
                {
                    return true;
                }
                current = parent;
            }
            return false;
        }

        /// <summary>
        /// 指示文を表示用に整える。改行を空白へ畳み、先頭 64 文字で切る（設計書 3.5 R1 / T9）。
        /// </summary>
        /// <param name="text">指示文の本文</param>
        /// <param name="maxLength">切り出す最大文字数</param>
        /// <returns>整形後の文字列。入力が空なら null</returns>
        public static string? TrimPrompt(string? text, int maxLength = 64)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var sb = new System.Text.StringBuilder(text.Length);
            var lastWasSpace = false;
            foreach (var c in text)
            {
                // 改行・タブ・行区切り文字（U+2028 / U+2029）はすべて 1 個の空白へ畳む
                var isSpace = char.IsWhiteSpace(c) || c == '\u2028' || c == '\u2029';
                if (isSpace)
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                    }
                    lastWasSpace = true;
                    continue;
                }
                sb.Append(c);
                lastWasSpace = false;
            }

            var s = sb.ToString().TrimEnd();
            if (s.Length == 0)
            {
                return null;
            }
            return s.Length <= maxLength ? s : s[..maxLength];
        }
    }
}
