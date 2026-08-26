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
