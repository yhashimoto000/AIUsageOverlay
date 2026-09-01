namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>
    /// ツール実行をフェーズへ写像する分類器（設計書 7.3）。
    ///
    /// 分類は推定である。決められないものを「その他」に押し込まず、
    /// <see cref="Unknown"/> として明示的に数える。分類率は必ず画面に出す（7.3.4）。
    /// 語彙表は本クラス内の定数として持つ。外部設定ファイルにはしない（N12）。
    /// </summary>
    public static class PhaseClassifier
    {
        /// <summary>フェーズ: 調査。</summary>
        public const string Investigate = "investigate";

        /// <summary>フェーズ: 実装。</summary>
        public const string Implement = "implement";

        /// <summary>フェーズ: テスト。</summary>
        public const string Test = "test";

        /// <summary>フェーズ: 対話。</summary>
        public const string Dialogue = "dialogue";

        /// <summary>フェーズ: 未分類。決められなかったもの。</summary>
        public const string Unknown = "unknown";

        /// <summary>画面表示で使うフェーズの並び順。</summary>
        public static readonly string[] AllPhases =
            { Investigate, Implement, Test, Dialogue, Unknown };

        /// <summary>Claude Code の調査系ツール名（設計書 7.3.1）。</summary>
        private static readonly HashSet<string> ClaudeInvestigateTools = new(StringComparer.Ordinal)
        {
            "Read", "Glob", "Grep", "WebFetch", "WebSearch", "NotebookRead", "Task", "Agent", "Explore",
        };

        /// <summary>Claude Code の実装系ツール名。</summary>
        private static readonly HashSet<string> ClaudeImplementTools = new(StringComparer.Ordinal)
        {
            "Edit", "Write", "MultiEdit", "NotebookEdit",
        };

        /// <summary>Claude Code の対話系ツール名。</summary>
        private static readonly HashSet<string> ClaudeDialogueTools = new(StringComparer.Ordinal)
        {
            "AskUserQuestion", "TodoWrite", "TaskCreate", "TaskUpdate", "ExitPlanMode", "EnterPlanMode",
        };

        /// <summary>シェル実行を表す Claude Code のツール名。中身のコマンドで判定する。</summary>
        private static readonly HashSet<string> ClaudeShellTools = new(StringComparer.Ordinal)
        {
            "Bash", "PowerShell",
        };

        /// <summary>
        /// 調査に分類するコマンドの先頭トークン（設計書 7.3.3）。
        /// POSIX / 汎用と PowerShell の両方を持つ。比較は大文字小文字を無視する
        /// （PowerShell のコマンドレットは実際の記録で大小が揺れるため）。
        /// </summary>
        private static readonly HashSet<string> InvestigateVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            // POSIX / 汎用
            "rg", "grep", "ls", "cat", "head", "tail", "find", "fd", "wc", "sed", "awk", "less", "type",
            // PowerShell
            "get-content", "select-string", "get-childitem", "measure-object", "select-object",
            "get-item", "get-location", "resolve-path", "test-path", "get-command", "where-object",
            "compare-object", "get-member", "format-list", "format-table", "out-string",
        };

        /// <summary>
        /// 調査に分類する 2 語コマンド（先頭 2 トークンで判定するもの）。
        /// </summary>
        private static readonly HashSet<string> InvestigateVerbPairs = new(StringComparer.OrdinalIgnoreCase)
        {
            "git log", "git status", "git diff", "git show", "git blame", "git branch", "git ls-files",
            "dotnet --version", "dotnet --info",
        };

        /// <summary>テストに分類するコマンドの先頭トークン（設計書 7.3.3）。</summary>
        private static readonly HashSet<string> TestVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "msbuild", "pytest", "invoke-pester", "ctest", "make", "ninja", "gradle", "mvn",
        };

        /// <summary>テストに分類する 2 語コマンド。</summary>
        private static readonly HashSet<string> TestVerbPairs = new(StringComparer.OrdinalIgnoreCase)
        {
            "dotnet test", "dotnet build", "dotnet run", "dotnet publish", "dotnet restore",
            "npm test", "npm run", "go test", "go build", "cargo test", "cargo build", "cargo run",
            "python -m", "pnpm test", "pnpm run", "yarn test", "yarn build",
        };

        /// <summary>
        /// 実装に分類するコマンドの先頭トークン。
        /// ファイル生成・書き換えを行うものだけを入れる。
        /// </summary>
        private static readonly HashSet<string> ImplementVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "set-content", "add-content", "out-file", "new-item", "copy-item", "move-item",
            "remove-item", "rename-item", "mkdir", "touch", "cp", "mv", "rm", "tee",
        };

        /// <summary>実装に分類する 2 語コマンド。</summary>
        private static readonly HashSet<string> ImplementVerbPairs = new(StringComparer.OrdinalIgnoreCase)
        {
            "git commit", "git add", "git checkout", "git merge", "git apply", "git rebase",
            "git switch", "git restore", "git stash",
        };

        /// <summary>
        /// 単体では意図を持たず、次のコマンドへ判定を委ねる先頭トークン。
        /// これらだけで未分類にすると、分類率が実態より低く出る。
        /// </summary>
        private static readonly HashSet<string> PassThroughVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "cd", "set-location", "pushd", "popd", "chcp", "export", "sudo", "time", "nohup",
        };

        /// <summary>
        /// Claude Code のツール実行 1 件を分類する（設計書 7.3.1）。
        /// </summary>
        /// <param name="toolName">tool_use の name</param>
        /// <param name="command">Bash / PowerShell の場合のコマンド本体。無ければ null</param>
        /// <returns>フェーズ定数</returns>
        public static string ClassifyClaudeTool(string toolName, string? command = null)
        {
            if (string.IsNullOrEmpty(toolName))
            {
                return Unknown;
            }
            if (ClaudeInvestigateTools.Contains(toolName))
            {
                return Investigate;
            }
            if (ClaudeImplementTools.Contains(toolName))
            {
                return Implement;
            }
            if (ClaudeDialogueTools.Contains(toolName))
            {
                return Dialogue;
            }
            if (ClaudeShellTools.Contains(toolName))
            {
                // シェル実行は中身のコマンドで判定する。コマンドが取れなければ未分類
                return command is null ? Unknown : ClassifyCommand(command);
            }
            // mcp__* と未知のツール名は未分類（分母から外さない）
            return Unknown;
        }

        /// <summary>
        /// シェルコマンド 1 件をフェーズへ分類する（設計書 7.3.2 / 7.3.3）。
        ///
        /// 判定は先頭トークンに対して行う。パイプで繋がっている場合は先頭セグメントのみを見る。
        /// このため「dotnet build | Select-String error」はテストと判定される（→ C3）。
        /// ただし cd などの意図を持たない前置きは読み飛ばし、次のコマンドで判定する。
        /// </summary>
        /// <param name="command">コマンド文字列</param>
        /// <returns>フェーズ定数</returns>
        public static string ClassifyCommand(string? command)
        {
            var head = ExtractHeadTokens(command);
            if (head is null)
            {
                return Unknown;
            }

            var (first, second) = head.Value;
            var pair = second is null ? null : $"{first} {second}";

            if (pair is not null)
            {
                if (TestVerbPairs.Contains(pair))
                {
                    return Test;
                }
                if (ImplementVerbPairs.Contains(pair))
                {
                    return Implement;
                }
                if (InvestigateVerbPairs.Contains(pair))
                {
                    return Investigate;
                }
            }

            if (TestVerbs.Contains(first))
            {
                return Test;
            }
            if (ImplementVerbs.Contains(first))
            {
                return Implement;
            }
            if (InvestigateVerbs.Contains(first))
            {
                return Investigate;
            }
            return Unknown;
        }

        /// <summary>
        /// 未分類コマンドの語彙集計に使う代表名を返す。
        /// 引数を落として先頭トークンだけにし、次版で語彙表へ足す判断材料にする（設計書 7.3.4）。
        /// </summary>
        /// <param name="command">コマンド文字列</param>
        /// <returns>先頭トークン。取り出せない場合は「(不明)」</returns>
        public static string ExtractCommandName(string? command)
        {
            var head = ExtractHeadTokens(command);
            if (head is null)
            {
                return "(不明)";
            }
            var (first, second) = head.Value;
            // git / dotnet / npm のようなサブコマンド型は 2 語で見ないと意図が分からない。
            // ただし 2 語目がオプション（-c / --version 等）なら意図を持たないので 1 語に留める
            var hasSubcommand = second is not null &&
                                second.Length > 0 &&
                                second[0] != '-' &&
                                IsSubcommandDriver(first);
            return hasSubcommand ? $"{first} {second}" : first;
        }

        /// <summary>サブコマンドまで見ないと意図が定まらないコマンドかどうか。</summary>
        /// <param name="verb">先頭トークン</param>
        private static bool IsSubcommandDriver(string verb)
            => verb.Equals("git", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("npm", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("pnpm", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("yarn", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("cargo", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("go", StringComparison.OrdinalIgnoreCase) ||
               verb.Equals("python", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// コマンド文字列から判定用の先頭 1〜2 トークンを取り出す。
        /// パイプの先頭セグメントを採り、変数代入や cd などの前置きは読み飛ばす。
        /// </summary>
        /// <param name="command">コマンド文字列</param>
        /// <returns>先頭トークンと 2 番目のトークン。取り出せない場合は null</returns>
        private static (string First, string? Second)? ExtractHeadTokens(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            // パイプの先頭セグメントのみを見る（設計書 C3）。
            // そのうえで、意図を持たない前置き（cd 等・変数代入）は「;」「&&」区切りで読み飛ばす。
            var segment = SplitTop(command, '|')[0];
            foreach (var part in SplitStatements(segment))
            {
                var tokens = Tokenize(part);
                if (tokens.Count == 0)
                {
                    continue;
                }

                var first = NormalizeVerb(tokens[0]);
                if (first.Length == 0 || IsAssignment(tokens[0]))
                {
                    continue;
                }
                if (PassThroughVerbs.Contains(first))
                {
                    continue;
                }
                return (first, tokens.Count > 1 ? tokens[1] : null);
            }
            return null;
        }

        /// <summary>「$var = ...」形式の変数代入かどうか。</summary>
        /// <param name="token">先頭トークン</param>
        private static bool IsAssignment(string token)
            => token.StartsWith('$') || token.Contains('=');

        /// <summary>
        /// 実行ファイルパスからコマンド名だけを取り出す。
        /// 「C:\Program Files\dotnet\dotnet.exe」→「dotnet」。
        /// </summary>
        /// <param name="token">先頭トークン</param>
        private static string NormalizeVerb(string token)
        {
            var v = token.Trim().Trim('"', '\'', '&');
            var slash = v.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0 && slash < v.Length - 1)
            {
                v = v[(slash + 1)..];
            }
            if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                v = v[..^4];
            }
            return v;
        }

        /// <summary>「;」「&amp;&amp;」「||」で文を分割する。引用符の内側では分割しない。</summary>
        /// <param name="s">分割対象</param>
        private static List<string> SplitStatements(string s)
        {
            var result = new List<string>();
            var start = 0;
            var inSingle = false;
            var inDouble = false;

            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }
                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }
                if (inSingle || inDouble)
                {
                    continue;
                }

                var isSemicolon = c == ';';
                var isAndAnd = c == '&' && i + 1 < s.Length && s[i + 1] == '&';
                if (!isSemicolon && !isAndAnd)
                {
                    continue;
                }

                result.Add(s[start..i]);
                i += isAndAnd ? 1 : 0;
                start = i + 1;
            }
            result.Add(s[start..]);
            return result;
        }

        /// <summary>指定文字で分割する。引用符の内側では分割しない。</summary>
        /// <param name="s">分割対象</param>
        /// <param name="separator">区切り文字</param>
        private static List<string> SplitTop(string s, char separator)
        {
            var result = new List<string>();
            var start = 0;
            var inSingle = false;
            var inDouble = false;

            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                }
                else if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                }
                else if (c == separator && !inSingle && !inDouble)
                {
                    result.Add(s[start..i]);
                    start = i + 1;
                }
            }
            result.Add(s[start..]);
            return result;
        }

        /// <summary>空白でトークンに分割する。引用符で囲まれた部分は 1 トークンとして扱う。</summary>
        /// <param name="s">分割対象</param>
        private static List<string> Tokenize(string s)
        {
            var tokens = new List<string>();
            var current = new System.Text.StringBuilder();
            var quote = '\0';

            foreach (var c in s)
            {
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                    else
                    {
                        current.Append(c);
                    }
                    continue;
                }
                if (c is '"' or '\'')
                {
                    quote = c;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }
            return tokens;
        }
    }
}
