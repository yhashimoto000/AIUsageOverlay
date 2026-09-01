## 2026-07-27
### Facts（事実）
- 自動アップデート P5（F-18〜F-24）は `feature/auto-update` で実装され、Release ビルドはタグ由来の SemVer を exe・zip 名へ注入する。
- 2026-07-27 時点の GitHub Releases API `/releases/latest` は旧形式タグ `v1.60` を返すため、3成分 SemVer の初回リリースまでは更新候補として解析されない。
- 自動更新用 Release には self-contained zip と、その zip の SHA256 を記載した `checksums.txt` を添付する。
### Policy（方針）
- 自動アップデートが参照する資産は `release.yml` 産の self-contained zip に限定し、`build-release.bat` 産の framework-dependent zip は GitHub Release へ手動添付しない。
- 自動確認は既定 ON とし、起動30秒後・6時間タイマー・24時間ゲートで GitHub の公開メタデータだけを GET する。利用データ・認証情報・テレメトリは送信しない。
- P6（ダウンロード・自己差し替え・updater.exe）は P5 の運用実績確認と人間の明示承認を得てから着手する。

## 2026-07-28
### Facts（事実）
- v2.0.1リリース準備では、`AIUsageOverlay.csproj`の開発用既定バージョンを`2.0.1`へ更新した。
- Release構成のビルドは警告0・エラー0で成功し、生成Assembly情報に`2.0.1`が反映された。
- GitHub Actionsの公式Releaseビルドはタグ版数を`-p:Version`で注入するため、csproj既定値とタグ版数を一致させつつ、タグをリリース成果物の真実源として維持できる。
### Policy（方針）
- リリース準備ではタグだけでなくcsprojの開発用既定バージョンも同じ版へ更新し、引数なしのローカルビルドによる誤った更新判定を防ぐ。
- 自動アップデートの実リリース検証は、旧版での新版検知と、更新後の新版で再通知されないことの両方を確認する。

## 2026-08-26
### Facts（事実）
- Codex CLI 0.144.4 の `-p, --profile <name>` は「`$CODEX_HOME/<name>.config.toml` をベースのユーザー設定にレイヤする」仕様（`codex --help` で確認）。`config.toml` 内に `[profiles.<name>]` テーブルを書く方式ではない。
- `codex doctor --json` は cwd が `C:\ToolCreate\Claude-UsageTool` でも、ロードした設定を `C:\Users\ndq23402\.codex\config.toml` のみと報告した。プロジェクトルートの `.codex/` は Codex の設定探索対象外。
- 選択可能な Codex モデルは `~/.codex/models_cache.json` に実データがある。現行は gpt-5.6-sol(prio1) / gpt-5.6-terra(2) / gpt-5.6-luna(3) / gpt-5.5(7) / gpt-5.4(16) / gpt-5.4-mini(23)。luna の説明は "Fast and affordable agentic coding model"。
- CLAUDE.md の「`.gitattributes` / `.editorconfig` は未配置」という記述は誤りだった。`.gitattributes` は配置済みで、`*.md` `*.cs` 等は `eol=lf`、`*.bat` `*.cmd` は `eol=crlf`。
- `.claude/settings.local.json` は `.gitignore` に未登録。`.claude/` をまとめて `git add` すると、個人のローカル許可リストごと commit される。
- Git Bash の `grep -c $'\r'` は CRLF 判定に使えない（LF のみのファイルで全行マッチした）。改行判定は `git ls-files --eol` か Python のバイト検査が確実。
- `AIUsageOverlay` の Codex 表示は F-13（commit bf928c1）で 5時間枠→週間枠へ組み替えられていたが、データ層（`CodexUsageData.Session*` / `CodexUsageParser` の期間判定）は据え置きだった。5時間枠の復活は ViewModel の表示ロジック差し替えだけで足りた。
- 3 スクレイパ（Claude / GitHub / Codex）の `IsInitialized()` は `_webView?.CoreWebView2 != null` しか見ていない。ブラウザプロセス（msedgewebview2.exe）が落ちても C# 側の参照は残るため「初期化済み」と誤判定し続け、ログイン画面も同じ Environment を共有するため黒画面になり、アプリ再起動以外で復旧しなかった。
- `CoreWebView2.BrowserProcessId` の型は `uint`。`Process.GetProcessById(int)` に渡すにはキャストが要る（CS0266 でビルドが落ちた）。
- 実機で確認した WebView2 のプロセス名は `msedgewebview2`。PID 再利用による生存誤判定を避けるため名前照合に使える。
- WPF の `ProgressBar.Value`（`RangeBase.Value`）は **既定で TwoWay バインド**。`private set` の ViewModel プロパティに結ぶと、起動時に「TwoWay または OneWayToSource バインドは、読み取り専用プロパティでは動作できません」で例外になりアプリが起動しない。**ビルドは警告0で通る**ため、ビルド成功は起動可否の担保にならない。
- MainWindow.xaml の全 `{Binding}` パスと MainViewModel の public プロパティを突き合わせる簡易スクリプトで、この事故を検出できた（修正前は `CodexWeeklyPercent` を検出、修正後は 0 件）。
- 検証時点で `AIUsageOverlay.exe` が 2 プロセス常駐していた。多重起動制御（Mutex）は実装されていないため、動作確認で起動するとインスタンスが増え、WebView2 のユーザーデータフォルダを別プロセスと共有することになる。
### Policy（方針）
- Codex のプロファイルは `~/.codex/<name>.config.toml` に置く。リポジトリ同梱の `.codex/light.config.toml` は雛型であり、使う人が `~/.codex/` へコピーする前提で運用する。
- AGENTS.md は CLAUDE.md を入力とした機械変換で生成し、手打ちで書き起こさない。片方だけ改訂されるドリフトが最も起きやすいため。
- 規約ファイルに委譲先・参照先（サブエージェント・プロファイル・ディレクトリ）を書くときは、実体の存在をコマンドで確認してから書く。存在しないものへの委譲指示は規約として空文になる。
- ユーザーレベル共有ファイルと重複させる事故防止系ルール（ファイル編集 / Git 操作）は `shared-rules` マーカーで囲み、出典と確認日を残して差分検証可能にする。
- Codex の枠表示は「5時間枠が取れれば 5時間、取れなければ週間」の二段構えにする。OpenAI 側の仕様変更で枠が増減しても表示が空にならない。
- WebView2 を常駐利用する箇所では `ProcessFailed` の購読とブラウザプロセスの生存確認をセットで入れ、致命例外時は Environment ごと作り直す。参照が non-null であることを初期化済みの根拠にしない。
- XAML バインディングを追加したら、TwoWay 既定の DP（ProgressBar/Slider の `Value`、`TextBox.Text`、`CheckBox.IsChecked`、Selector の `Selected*`）に `private set` のプロパティを結んでいないかをコミット前に確認する。ビルドでは検出できず実行時に落ちる。
- WPF の変更は「ビルド成功」を完了条件にしない。起動確認が取れない場合は、その旨を未確認として明示する。

## 2026-08-31
### Facts（事実）
- Claude Code の履歴 `~/.claude/projects/**/*.jsonl` は、1 つの API 応答が複数 content ブロックを持つと **同じ `message.usage` を持つ `assistant` 行がブロック数だけ書き出される**。実測サンプル（726 行）では assistant 357 行に対しユニーク `requestId` 145 件、複数行を持つ 120 件は **全行の usage が完全一致**。全行合算すると output_tokens が 2.34 倍、cache_read が 2.45 倍、cache_creation が 2.71 倍に膨らむ。`(requestId, message.id)` で一意化が必須。
- Codex の `event_msg/token_count` は `total_token_usage` が**累積値**（最終行のみ採る）、`last_token_usage` がターン差分。実測で `total_tokens = input_tokens + output_tokens` が厳密一致。`cached_input_tokens` と `reasoning_output_tokens` は内数と整合するが**内数であることは未検証**。
- Codex の `token_count` には `rate_limits`（5時間枠 `primary` / 週枠 `secondary` の `used_percent`・`resets_at`・`plan_type`）が含まれる。既存 `CodexWebScraper` の取得値をローカルファイル読みで代替できる可能性があるが、更新は Codex 実行時のみ。
- Codex の `custom_tool_call` は全て `name=exec` で、`input` が `tools.shell_command({...})` という **JS ラッパ内の二重 JSON**。中身は Windows では PowerShell（`Get-Content` / `Select-String`）。POSIX 語彙（`cat`/`grep`）前提の分類ルールを全 505 ファイルに当てたところ未分類 12,206 / 実装 4,103 / 調査 780 / テスト 299 で、分類率 41.7%。Claude Code は `tool_use` の `name` が直接出るため 94.1%。
- 実測環境（Linux + Python3）で Codex 505 ファイル・593 MB・269,076 行の全パースに 21.6 秒、最大 RSS 15.6 MB。Windows/.NET は未計測。
- `cwd` はドライブレターの大小が揺れる（`C:\ToolCreate\Y_SmartApp` と `c:\ToolCreate\Y_SmartApp` が別集計になった）。正規化キーと表示用原文を分けて持つ必要がある。
- Cowork の履歴（`AppData\Roaming\Claude\local-agent-mode-sessions`）はアプリ内部保存領域として保護されており、外部から読めない。`~/.claude` も保護対象でマウントできない（`~/.codex` は可）。
- `design-doc-authoring` の生成器は **h2 / h3 にしか id を振らない**。`####`（h4）で 3 階層見出しを書くと「本文の節参照が実在」検査が NG になる。3 階層でも `###` で書く。
- 同スキルの `verify_design_html.py` の実行時検証（jsdom）は、`node_modules` があるディレクトリで実行しないと「jsdom が無い」として黙って省略される。省略されても `=== 全項目 PASS ===` と出るため、**PASS 表示だけでは実行時検証を通したことにならない**（`（1 件は省略）` の有無で判別する）。
- 設計書本文に U+2028 / U+2029 の実体を書くと、生成 HTML の JS が行終端子として解釈して壊れる。エスケープ表記（`\u2028`）で書く。
- サンドボックスの bash は接続フォルダ内のファイルを削除できない（`Operation not permitted`）。ツールが作った一時ファイルの後始末が必要な場合は、書き込み先を接続フォルダ外にする。

### Policy（方針）
- ローカル履歴からの使用量集計では、**重複排除を最優先で設計する**。Claude Code は `(requestId, message.id)`、Codex は `session_meta.id`（スレッド固有 ID）で一意化し、除外実績を画面に出す。除外を隠すと数字が実態より確からしく見える。
- 鍵（`requestId` 等）を持たない行は「一意」として**必ず加算する**。鍵が無いことを理由に捨てると、実在する消費を落とす。過大計上より発見しにくい。
- 集計結果を HTML に埋め込むときは `<script type="application/json">` を使い、`<` `>` `&` `\u2028` `\u2029` を置換する。履歴由来の文字列には `</script>` が入り得る。
- 推定値（フェーズ分類・アクティブ時間）は、**分類率・除外率を併記できないなら出さない**。実測の根拠が無い閾値を置くくらいなら、指標ごと落とす方がよい。
- 設計書は起草後に必ず別コンテキストで原典突合レビューを回す。今回は Rev 1.0 に対し事実誤認 6・技術的破綻 6・未定義ケース 6・過剰設計 7 が出て、うち 1 件（HTML からの再パース）は**実装不可能な仕様**だった。
- 新規サービス名は既存と 1 語違いにしない。`UsageHistoryService` / `history.json` が既にあるため、新規は `SessionStats` 系で統一した。

## 2026-09-01
### Facts（事実）
- **B0 / Q1 確定: Codex の `cached_input_tokens` は `input_tokens` の内数**。実履歴 40 セッション 3,497 行で `cached_input > input` は 0 件、`total_tokens == input + output` は全行一致。`last_token_usage` の系列で turn1 の `cached`(16,768) が turn0 の `input`(16,958) にほぼ一致し、turn1 の `input`(17,316) が前ターン分を含む累積になっていた。外数なら turn1 の input は新規分のみになる。7.1 の課金相当 `(input - cached_input) + output` は妥当。
- **B0 / Q2 確定: Codex の `session_id` は「会話ツリーのルート ID」であり、セッション単位の識別子ではない。** 540 ファイル中 `session_id != id` が 364 件、ユニーク `session_id` は 90 件、同一 `session_id` が最大 76 ファイルに出現。同一 `session_id` グループの `user_message` 系列を全ペア比較したところ、検査した 12 グループすべてが prefix 関係になく **枝分かれしていた**。
- **`session_meta.id` はスレッド固有で、540 ファイルすべてユニーク（重複 0）。** `thread_source` は subagent 404 / user 82 / automation 1 / なし 53。
- **fork 先（サブエージェント含む）は累積トークンをリセットして 0 から始まる。** 親を解決できた 390 スレッドのうち、子の初回累積が親の累積の半分以上だったのは 3 件のみで、387 件は 1〜2 万トークン（自分の初期コンテキスト相当）から開始していた。**fork 先の `total_token_usage` はそのスレッド固有の消費である。**
- **`total_token_usage` の重複計上は存在しない。** 内容シグネチャ（`user_message` 列 + 最終累積 + 行数）が一致する複製ファイルは **0 組**、全行が同一 timestamp のファイルも **0 件**。`id` 単位の合算 7,713,173,162 と内容一意化後の値は完全一致（差 0）。
- **したがって設計書 5.2 の「同一 `session_id` のうち `total_tokens` 最大の 1 ファイルだけを採用する」は誤りで、サブエージェントの消費を丸ごと捨てる。** 実測で `id` 単位合算 7,713,173,162 に対し `session_id` 最大値採用は 1,929,849,177 となり、**全体の 75% が消える**。内訳は subagent 5,306,505,158 / user 1,894,521,090 で、捨てられるのは主にサブエージェント分である。
- 2026-08-31 に記録した「Codex は fork のたびに元履歴を丸ごと別ファイルへ複製する」「`session_id` 一意化で 68.8% が過大計上」は**誤りだった**ため削除した。実体は「1 つの会話ツリーから派生した独立スレッド群が同じ `session_id` を共有している」である。
- Claude Code 側の重複は 5.1 の規則で再現した。実機 603 ファイル・407.7 MB で `(requestId, message.id)` 一意化により output 2.41 倍 / cacheCreate 2.47 倍 / cacheRead 1.71 倍 / input 2.66 倍の過大が解消。`requestId` 欠落行は 8 件のみ（output 0）。
- **B0 / Q6 の材料**: Claude Code 603 ファイル・407.7 MB の全行パースが Python で 4.8 秒（parseErrors 0）。Windows 実機・ローカルディスク。.NET 実装での計測は B1 後。
- 実機の Claude Code ツール実行回数トップ: Bash 3,815 / Edit 2,343 / Read 1,126 / PowerShell 735 / Write 430 / Grep 106。`mcp__Claude_Browser__*` が 200 件超あり、`mcp__` 接頭辞の扱いは分類率に効く。

### Policy（方針）
- **Codex の一意化キーは `session_meta.id`（スレッド固有 ID）とする。`session_id` はツリーのルート参照としてのみ使い、重複排除には使わない。** 逆にすると全体の 75%（サブエージェント分）が消える。
- サブエージェントは「重複」ではなく「独立した消費者」として集計する。親子の紐付け（`parent_thread_id` / `forked_from_id`）は表示のためであって、除外のためではない。
- 「重複しているはず」という仮説は、**内容シグネチャの一致で裏を取ってから除外規則にする**。ID の共有だけを根拠に除外すると、実在する消費を捨てる。今回は ID 共有＝複製と解釈した前回の分析が誤っていた。

## 2026-09-02
### Facts（事実）
- **Codex の `session_meta` と `turn_context` は payload に `type` を持たない。** 行の root 側の `type` だけで種別が決まる。`payload.type` だけを見る実装では cwd・session_id・model を丸ごと取りこぼす（実装初版がこれで、実データ検証まで気付かなかった）。
- **`turn_context.model` は payload のキー列の後方にある**（turn_id / cwd / workspace_roots / … / model / effort / summary）。存在するが先頭付近を見るだけでは見落とす。
- **Codex の `tools.shell_command(...)` の引数は厳密な JSON ではない。** 実データは `{command:"...",workdir:"..."}` のようにキーが引用符無しの JavaScript オブジェクトリテラルで、`JsonDocument.Parse` は失敗する。値は二重引用符の文字列リテラルなので、キーを探して文字列を 1 文字ずつ読むフォールバックが要る。改行で整形された形（`{\n  command: "..."`）もある。
- **Codex のパッチ適用は 2 通りで記録される。** `custom_tool_call` の `name=apply_patch`（879 件）と、`name=exec` で input が `*** Begin Patch` 本文のもの（2,171 件）。さらに結果イベント `patch_apply_end` が別に出るため、要求と結果の両方を数えると 1 回の編集が 2 回の実行になる。
- **Claude Code のサブエージェント履歴は、中の `sessionId` が親と同一**（実測 200/200 で一致）。トップレベルは `sessionId` == ファイル名の UUID（100/100）。`sessionId` だけを鍵にすると親と全サブエージェントが 1 件へ潰れるため、サブエージェントはパス（`<UUID>/subagents/...`）を鍵へ含めて区別する必要がある。
- **B0 / Q6 確定: Windows 実機での初回フルスキャンは 6.8〜7.2 秒**（1,113 ファイル / Claude Code 573 + Codex 540 / .NET 9 / ローカルディスク）。30 秒を大きく下回るため、進捗表示とキャンセルは必須ではない。
- **B0 / Q3 の材料**: 実機の履歴範囲は 2026-06-03 〜 2026-09-02（約 3 か月）。読めなかったファイルは 0 件、壊れた行 0 行、末尾破損 0 件だった。
- **フェーズ分類率の実測**: Codex は設計書 7.3.2 の 41.7% から **74.2%** へ改善した（PowerShell 語彙 + パッチ適用の分類 + `cd` 等の前置き読み飛ばし）。Claude Code は 80.3%、全体 78.3%。残る未分類の最大要因は shell_command を取り出せない `(不明)` 2,568 件と `python -c` 系。
- 実機の全期間集計は 課金相当 2.47 億トークン / 総処理 106.9 億トークン（キャッシュ読込 104.5 億）。**総処理の 97.7% がキャッシュ読込**で、単純合計を「使用量」と呼ぶと実感と桁が合わない。
- xUnit のテストプロジェクトで `UseWPF=true` を立てると、暗黙 using から `System.IO` が外れる。`Path` / `File` / `Directory` が未定義になり CS0103 になる。

### Policy（方針）
- **JSONL の行構造は「payload の中だけ」を見て決めない。** root と payload の 2 段で種別が決まる形式があるため、実データで両方を確認してからパーサを書く。実装初版はこれを取りこぼし、単体テストは合成データで通っていたため気付けなかった（合成データを実データから写す前に検証したのが原因）。
- **合成テストデータは、実データの構造を確認してから作る。** 設計書の記載（`{"command":"..."}`）と実データ（`{command:"..."}`）が食い違っていた。設計書は起草時点の観測であり、実装時に再確認する。
- **要求イベントと結果イベントが両方記録される操作は、どちらか一方だけを数える。** 本機能では「要求（custom_tool_call）」に統一した。exec も要求で数えているため基準が揃う。
- 実機計測用のテストは環境変数（`AIUSAGE_REAL_SCAN=1`）でガードし、通常の `dotnet test` では走らせない。実履歴を読むテストを CI に混ぜない。

### Facts（追記 2026-09-02）
- 生成物を「直近 N 件だけ残す」整理では、**並べ替えの主キーをファイル名にする**（名前に生成日時が入っている場合）。更新時刻（mtime）を主キーにすると、テストや同期でタイムスタンプを操作したときに **今作ったファイル自身が削除対象になる**（実際にテストで踏んだ）。
- 使用量レポートの HTML は `session-stats-cache.json` からの派生物であり、キャッシュは履歴ファイルが消えた分の集計も保持し続ける。**この性質があるから古いレポートを消してよい。** 逆に `schemaVersion` を上げると全件再パースになり、その時点で履歴が消えているセッションは失われる。

### Policy（追記 2026-09-02）
- **「生成物を自動削除してよいか」は、元データから再生成できるかで決める。** 再生成できるなら消してよく、できないなら残す。本機能ではキャッシュの保持性をテスト（T18）で固定してから自動削除を入れた。根拠をテストにしておくと、後の変更で前提が崩れたときに気付ける。
- 自動削除の対象は**自分が生成した命名規則のファイルに限定する**（`usage-*.html`）。同じフォルダに利用者が置いたファイルを巻き込まない。
