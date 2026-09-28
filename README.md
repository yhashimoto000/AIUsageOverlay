# AI Usage Overlay

Claude.ai、GitHub Copilot、Codex の使用量を Windows 画面上に常時表示するオーバーレイアプリです。
タスクトレイに常駐し、使用率・取得状態に応じてアイコン表示が変化します。

---

## 機能

- Claude.ai の**セッション・週間使用率**、GitHub Copilot の**AI credits**、
  Codex の**週間使用率**、Cursor の**プラン枠・Auto/指名モデル枠**を表示
- **残り時間・リセット時刻・予定比・枯渇予測**を表示
- 縦積み、コンパクト、詳細のオーバーレイ表示を切り替え可能
- 使用率推移のスパークライン、stale（取得失敗中）の減光表示
- 画面の好きな場所にドラッグして配置可能（位置は次回起動時も維持）
- Windows 起動時に自動起動（設定から ON/OFF）
- 使用量の定期自動更新（デフォルト 30 秒、適応更新間隔あり）＋手動更新ボタン（↺）
- 使用量更新の30分・1時間・3時間スヌーズ
- 使用率の閾値超過、上限到達、リセット完了をトレイ通知
- **タスクトレイ常駐**（タスクバーには表示されない）
- **トレイアイコンを4形式から選択**（リング / 2段バー / ドーナツ / 数字）
- **トレイアイコンにカーソルを合わせると使用率を表示**（例: "セッション: 75%  週間: 10%"）
- GitHub Releases を利用した**新バージョン確認**（通知とReleaseページへの案内）
- **Cursor のトークン累積**（ダッシュボードの API レスポンスを傍受して月別に蓄積）
- **使用量レポート**（Claude Code / Cowork / Codex / Cursor のローカル履歴から、
  プロジェクト別・日別・作業内容別の使用量を 1 枚の HTML にまとめる）
- レポートの**API利用相当（参考）**（Claude Code / Cowork を公開 API 係数で重み付けした AP。金額・通貨は出さない）

---

## 動作要件

| 項目 | 内容 |
|------|------|
| OS | Windows 10 / 11 (64-bit) |
| WebView2 Runtime | Windows 11 および Edge インストール済みの Windows 10 には標準搭載 |
| アカウント | 表示するサービスの Claude.ai / GitHub / ChatGPT アカウント |

> **WebView2 が未インストールの場合**  
> [Microsoft の公式ページ](https://developer.microsoft.com/ja-jp/microsoft-edge/webview2/) からインストールしてください。

---

## インストール（ビルド不要）

1. [Releases](https://github.com/yhashimoto000/AIUsageOverlay/releases) ページを開く
2. 最新バージョンの `AIUsageOverlay_vX.Y.Z.zip` をダウンロード
3. zip を任意のフォルダへ展開
4. 展開先の `AIUsageOverlay.exe` をダブルクリック

インストーラーは不要です。`AIUsageOverlay.exe` と同じフォルダにある DLL も実行に必要なため、
exe だけを移動せず、展開したフォルダごと使用してください。

Release の `checksums.txt` には zip の SHA256 が記載されています。必要に応じて
`Get-FileHash AIUsageOverlay_vX.Y.Z.zip -Algorithm SHA256` の結果と照合できます。

---

## 初回セットアップ

アプリを起動するとタスクバーには表示されず、**タスクトレイ**（画面右下の通知領域）にアイコンが表示されます。  
オーバーレイを右クリック、またはトレイアイコンを右クリックしてメニューを開きます。

### 1. ログイン

```
右クリック → ログイン - <サービス>
```

表示するサービスに応じて、右クリックメニューからログイン画面を開きます。

| メニュー | ログイン先 |
|----------|------------|
| ログイン - Claude | claude.ai |
| ログイン - GitHub | GitHub Copilot |
| ログイン - Codex | ChatGPT Codex |
| ログイン - Cursor | cursor.com |

ログイン完了後、ウィンドウを閉じて ↺ ボタンを押すと使用量が反映されます。

> Cursor は既定で非表示です。**設定 → 「Cursor の使用状況を表示する」を ON にしてから**
> ログインしてください。

> ログイン情報は `%TEMP%\AIUsageOverlay_WebView2` に保存され、  
> **次回以降は自動ログイン**されます（Cookie の手動入力は不要です）。

### 2. 設定（任意）

```
右クリック → 設定
```

| 設定項目 | 説明 |
|----------|------|
| 更新間隔（秒） | 使用量データ取得の基本間隔（最小 5 秒） |
| 適応更新間隔 | 操作状況・表示状態・電源状態に応じて取得間隔を延長 |
| Windows 起動時に自動起動 | チェックで HKCU\...\Run に登録 |
| 自動で更新を確認する | GitHub Releases を24時間ごとに確認 |
| 表示項目 | Copilot / Codex / Cursor、リセット時刻、ペース、スパークラインの表示設定 |
| 外観 | レイアウト、トレイアイコン、色の閾値、マーカー、不透明度 |
| 通知 | 使用率閾値、リセット完了、100%到達の通知設定 |

### 3. アプリの更新確認

設定画面の「今すぐ確認」、またはトレイメニューの「更新を確認」から最新版を確認できます。
新しいバージョンがある場合はトレイ通知とメニューで案内され、クリックするとGitHub Releaseページが開きます。

現在のP5では、アプリが更新ファイルを自動ダウンロード・自動適用することはありません。
Releaseページからzipを取得し、終了した旧版のフォルダを新版で置き換えてください。
公式Release版はタグからバージョンが埋め込まれるため、更新後の同一バージョンを再通知しません。

---

## 使い方

### オーバーレイの操作

| 操作 | 動作 |
|------|------|
| ドラッグ | オーバーレイを好きな場所へ移動（位置は自動保存） |
| ↺ ボタン | 今すぐ更新 |
| 右クリック → 設定 | 設定画面を開く |
| 右クリック → ログイン - Claude / GitHub / Codex / Cursor | WebView2ブラウザで各サービスへログイン |
| 右クリック → セッションリセット | セッションタイマーをリセット |
| 右クリック → 使用量レポートを生成 | ローカル履歴を集計して HTML を作り、既定ブラウザで開く |
| 右クリック → レポートのフォルダを開く | 生成済みレポートの保存先をエクスプローラで開く |
| 右クリック → 非表示にする | オーバーレイを隠してトレイに引っ込む |
| 右クリック → 終了 | アプリを終了 |
| × ボタン / Alt+F4 | トレイに引っ込む（アプリは終了しない） |

### トレイアイコンの操作

| 操作 | 動作 |
|------|------|
| ダブルクリック | オーバーレイの表示 / 非表示をトグル |
| 右クリック → 表示 / 非表示 | オーバーレイの表示 / 非表示をトグル |
| 右クリック → 更新を一時停止 | 使用量更新を30分 / 1時間 / 3時間停止、または再開 |
| 右クリック → 更新を確認 | アプリの最新バージョンを確認。検知済みならReleaseページを開く |
| 右クリック → 使用量レポートを生成 | ローカル履歴を集計して HTML を作り、既定ブラウザで開く |
| 右クリック → レポートのフォルダを開く | 生成済みレポートの保存先をエクスプローラで開く |
| 右クリック → 終了 | アプリを終了 |
| カーソルを合わせる | セッションと週間の使用率を表示 |

### 使用量レポート

オーバーレイが「いま残りいくつか」を出すのに対し、こちらは
**過去に何へどれだけ使ったか**を振り返るための機能です。

#### 生成のしかた

**トレイアイコンを右クリック → 「使用量レポートを生成」**。それだけです。
オーバーレイ本体を右クリックしたメニュー（設定・ログインが並ぶ方）からも同じことができます。

集計が終わると HTML ファイルが作られ、**既定ブラウザで自動的に開きます。**
実測では約 7 秒（履歴 1,100 ファイル）でした。

生成中はメニュー項目が「使用量レポートを生成中… 42%」に変わり、
**オーバーレイの右下にも小さく進行状況が出ます。** 二重に押しても無視されます。

レポートは**生成した時点のスナップショット**です。自動では更新されません。
新しい数字が見たいときは、もう一度メニューから生成してください。

#### どこに保存されるか

```
%AppData%\AIUsageOverlay\reports\usage-YYYYMMDD-HHmm.html
```

右クリックメニューの**「レポートのフォルダを開く」**で、この場所をエクスプローラで開けます。

- 生成のたびに**日時入りの新しいファイル**が増えます。上書きはしません
- **直近 5 件だけを残し、古いものは生成時に自動削除します**（1 ファイルあたり 1 MB 前後）。
  消えて困ることはありません。レポートは下のキャッシュから作り直せるためです
  （同じフォルダに置いた `usage-*.html` 以外のファイルは削除対象外です）
- 外部を一切参照しない単一 HTML なので、**そのままコピーして他人に渡せます**
  （渡す前に下の「指示文の出力」を確認してください）。
  残しておきたいレポートは、このフォルダの外へコピーしてください

#### レポートの見かた

画面上部で期間を切り替えます。**既定は「今日」**です。

| 期間 | 内容 |
|------|------|
| 今日 / 7 日 / 30 日 / 全期間 | ボタンで切り替え |
| 月を選ぶ | 履歴に残っている月だけがドロップダウンに並びます |

4 つのタブがあります。

| タブ | 内容 |
|------|------|
| 概要 | 消費トークン・処理トークン・セッション数・経過時間・判別率、**API利用相当（参考）**、日ごとの推移、作業内容の内訳、プロジェクト別 |
| セッション | セッション一覧。サブエージェントは親の下に畳んで表示。指示文の先頭 64 文字で見分けられます。**行をクリックすると明細が開きます**（下記） |
| ツール内訳 | ツール別の実行回数、判別できなかったコマンド、モデル別 |
| データ品質 | **この数字をどこまで信じてよいか。** 重複排除の実績、読めなかったファイル、履歴の範囲 |

#### API利用相当（参考）

概要タブの API 利用相当ポイント（AP）は、Claude Code / Cowork のトークンを
公開 API 係数で重み付けした**比較用の参考指標**です。トークン数の割合とは別物です。

| 種別 | 単価（新規入力を 1 としたときの倍率） |
|------|------|
| 新規入力 | 1.0 |
| キャッシュ書き込み（5 分保持） | 1.25 |
| キャッシュ書き込み（1 時間保持） | 2.0 |
| キャッシュ読み込み | 0.1 |
| 出力 | 5.0 |

キャッシュ読み込みはトークン数では 9 割を超えることもありますが、
係数が 10 分の 1 なので、API 利用相当では半分前後になります。
**この値が半分を大きく超えていなければ、キャッシュは意図どおり働いています。**

金額・通貨は表示しません。定額プランの支払い、利用枠、レート制限とは一致しません。
係数が分からない Claude 系モデルは AP から除き、除外量と対象率を表示します。
Codex / Cursor は AP の対象外で、対象率の分母にも含めません。

ブロックの下には**換算に使った係数の一覧**と基準日を出します。
係数は改定されることがあるため、そのレポートがどの前提で計算されたかを残すためのものです。

同じ内訳は**セッション明細（セッション一覧の行をクリック）にも出ます。**
セッションごとにキャッシュの効き方が違うため、重いセッションを見つけたらそこで内訳を確認できます。

#### セッションの明細

セッション一覧の**行をクリックする**と、その下に内訳が開きます。

| カード | 内容 |
|------|------|
| このセッション | 指示文、開始・終了、経過時間、ターン数、主なモデル |
| トークンの内訳 | 新規入力 / キャッシュ作成 / キャッシュ読込 / 出力 の内訳と実数 |
| API利用相当 | そのセッションを公開 API 係数で重み付けした参考 AP と構成比 |
| 何をしていたか | 調査・実装・テスト・対話・未分類 の実行回数 |
| モデル別 | モデルごとのターン数と消費・処理トークン |
| 使ったツール | ツール別の実行回数 |
| 判別できなかったコマンド | 何の作業か決められなかったコマンド |
| 日別 | 日をまたいだセッションのみ |
| 配下のサブエージェント | 子の消費（多い順） |
| 数字の確からしさ | 読んだ / 採用したレコード数、壊れた行など |

ターン 1 件ずつの時系列は出せません。レポートは集計済みの値だけを持っており、
開いた HTML から履歴ファイルを読み直す仕組みは持たないためです。

数字の意味で迷いやすいのは次の 2 つです。

| 表示 | 意味 |
|------|------|
| **消費トークン** | 使用量の枠（レート制限）を減らす分。キャッシュから読んだ分を含みません |
| **処理トークン** | AI が実際に読み書きした量。キャッシュから読んだ分を含みます |

実測では**処理トークンの 97% 以上がキャッシュ読込**でした。
「使った量」として見るなら消費トークンの方です。

なお **経過時間は作業時間ではありません。** セッションの最初と最後の記録の差なので、
読んでいた時間や離席も含みます。

#### 指示文の出力（他人に渡すとき）

セッションを見分けられるよう、既定では**各セッションの最初の指示文の先頭 64 文字**を出します。
会話の本文は出しません。

社外へ渡すレポートを作るときは、**設定 → 「使用量レポートに指示文を出力する」を OFF** にしてから
生成してください。プロジェクトのフルパスは OFF にしても出ます。

#### 履歴が消える前に

Claude Code / Cowork / Codex / Cursor の履歴ファイルは、各ツールの保持期間を過ぎると削除されます。
本機能は一度取り込んだ集計を `%AppData%\AIUsageOverlay\session-stats-cache.json` に
残し続けるので、**レポートを作った時点までの記録は保持されます。**
履歴ファイルが消えても、このキャッシュにあるセッションは以降のレポートにも出続けます。
逆に、作らないまま履歴が消えた分は後から集計できません。

> このキャッシュがあるため、古いレポート HTML は消しても構いません（上の自動整理）。
> ただし**キャッシュ自体を削除すると、履歴が残っている分しか復元できません。**
> `session-stats-cache.json` は消さないでください。

そのため、**7 日以上レポートを作っていないとトレイ通知でお知らせします**
（設定 → 「レポート未作成を通知する」で OFF にできます）。

#### Cursor のトークンは別扱い

**Cursor のローカル履歴にはトークン数が入っていません。** そのため Cursor のセッションは
件数や作業内容としては集計されますが、トークンは 0 として扱われます
（API 利用相当の対象にもなりません）。

代わりに、Cursor のダッシュボードを読みに行くたびに API のレスポンスを傍受して
`%AppData%\AIUsageOverlay\cursor-tokens.json` へ月別に蓄積し、
レポートの「Cursor トークン累積」ブロックに出しています。
**アプリが起動していなかった期間の分は取れません。**

過去の月をまとめて取り込みたいときは、次のようにアプリを起動します。
取得だけ行って自動的に終了します。

```bash
AIUsageOverlay.exe --fetch-cursor-month=2026-08
```

### トレイアイコンの色の意味

| 色 | セッション使用率 |
|----|----------------|
| 緑 | 0 〜 49%（通常） |
| オレンジ | 50 〜 79%（注意） |
| 赤 | 80 〜 100%（警告） |

注意・警告の閾値は設定画面で変更できます。

### ステータス表示の見方

| 表示 | 意味 |
|------|------|
| `API: HH:mm` | Claude.aiから使用量データを取得済み |
| `エラー: 未ログイン` | ログインが必要（右クリック → ログイン） |
| `取得中...` | 使用量データを更新中 |
| `一時停止中（〜HH:mm）` | 指定時刻まで使用量更新をスヌーズ中 |

---

## 自分でビルドする場合

### 必要なもの

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022 以降（WPF ワークロード）

### ビルド手順

```bash
git clone https://github.com/yhashimoto000/AIUsageOverlay.git
cd AIUsageOverlay/AIUsageOverlay
dotnet restore
dotnet build -c Release
```

### ローカル配布用に発行する

```bat
build-release.bat
```

`publish/AIUsageOverlay.exe` と必要なDLL、`AIUsageOverlay_release.zip` が生成されます。
引数を省略した場合はcsprojの既定バージョンが使われます。任意の版数を使う場合は
`build-release.bat X.Y.Z`の形式で3成分SemVerを指定してください。

GitHub公式Releaseは`v*`タグのpushで`.github/workflows/release.yml`が生成します。
タグを単一の真実源としてバージョンを注入し、self-contained zipと`checksums.txt`を添付します。
`build-release.bat`のframework-dependent zipはローカル確認用であり、GitHub Releaseへ手動添付しないでください。

---

## 開発フロー

ブランチ運用のルールは `.claude/git-rules.json` に記録しています。
人が読む形は [GIT_WORKFLOW.md](GIT_WORKFLOW.md)、規約の要約は `CLAUDE.md` / `AGENTS.md` にあります。
**ルールを変えるときは `.claude/git-rules.json` を直してから再生成**してください（手で書き換えない）。

```
wip                                    ローカル限定・push しない・1 本を使い回す
 |  --squash（1 サブタスク = 1 コミット / refs/wip-archive/<トピック> へ退避）
feat/  fix/  docs/  refactor/  perf/  build/  chore/     種別ブランチ・push する
 |  --no-ff（マージ単位で履歴を残す）
master                                 統合ブランチ・タグを打つ
```

### git-flow.ps1

手順の多い操作（とくに squash 取り込み）をまとめたヘルパーです。
**PowerShell 7 (pwsh) 推奨**。リポジトリのどこから実行しても動きます。

```powershell
.\scripts\git-flow.ps1 help
```

| コマンド | 動作 |
|---------|------|
| `status` | 現在のブランチ・wip の有無・退避の一覧・未 push の件数を表示 |
| `start <種別> <トピック>` | master を最新化して `<種別>/<トピック>` を作り、push して `wip` を用意 |
| `wip` | 現在の種別ブランチから `wip` を作り直す |
| `squash [<トピック>]` | `wip` を種別ブランチへ squash 取り込み → 退避 → `wip` 作り直し |
| `integrate` | 種別ブランチを master へ `--no-ff` マージして push |
| `archives` | 退避した作業履歴の一覧 |
| `show <トピック>` | 退避した作業履歴のコミットログ |

`-Yes` を付けると確認プロンプトを飛ばします。

**このスクリプトは破壊的なコマンドを実行しません。**
`push --force` / `reset --hard` / `clean -fd` / `push --all` / `push --mirror` は使わず、
push は対象を明示した `git push <remote> <branch>` だけです。

#### 典型的な流れ

```powershell
# 1. トピックを始める（master 最新化 → feat/xxx 作成 → push → wip 用意）
.\scripts\git-flow.ps1 start feat usage-dashboard

# 2. wip で細かくコミットする（push はされない）
git add -A
git commit -m "feat(usage-dashboard): 走査の骨組みを追加"

# 3. サブタスクが片付いたら種別ブランチへまとめる
.\scripts\git-flow.ps1 squash
#    エディタが開くので SQUASH_MSG の先頭 1 行だけ残して保存する。
#    そのあと自動で refs/wip-archive/<トピック> へ退避し、wip を作り直す。

# 4. 2〜3 を繰り返す

# 5. トピックが完成したら master へ入れる
git switch feat/usage-dashboard
.\scripts\git-flow.ps1 integrate
```

squash 後にエディタで `-m` を付けずにコミットするのは意図的です。
`git merge --squash` が `.git/SQUASH_MSG` へ書き出した wip 側のログが初期内容になるため、
先頭 1 行を残せば wip と種別ブランチのメッセージが一致します（`-m` を付けると無視されます）。

退避した履歴は通常の push では飛ばず、`git gc` でも回収されません。読むときは次のとおり。

```powershell
.\scripts\git-flow.ps1 archives
.\scripts\git-flow.ps1 show usage-dashboard
```

### リリース手順

**自動化していません。** 版数の決定とタグ push（＝ GitHub Release の即時公開）は
人が確認しながら行います。手順は次の 5 つです。

```powershell
# 1. 版数を上げる（AIUsageOverlay/AIUsageOverlay.csproj の <Version>）
#    タグと csproj を一致させないと、引数なしのローカルビルドが誤った更新判定を出す
git add AIUsageOverlay/AIUsageOverlay.csproj
git commit -m "chore(release): 開発用既定バージョンを2.1.0へ更新"

# 2. master へ統合する
.\scripts\git-flow.ps1 integrate

# 3. ビルドとテストを通す
dotnet build AIUsageOverlay/AIUsageOverlay.csproj -c Release
dotnet test AIUsageOverlay.Tests/AIUsageOverlay.Tests.csproj

# 4. 注釈付きタグを打つ（厳密な 3 成分 SemVer。v1.40 のような 2 成分は CI が弾く）
git tag -a v2.1.0 -m "AI Usage Overlay v2.1.0"

# 5. master とタグを両リモートへ push する
git push github master;  git push github v2.1.0
git push backlog master; git push backlog v2.1.0
```

**タグを `github` へ push した時点で GitHub Actions が走り、Release が即時公開されます**
（`draft: false`）。self-contained zip と `checksums.txt` が自動で添付され、
所要時間は約 2 分です。公開後は `/releases/latest` が新しいタグを返すようになり、
既存利用者の自動更新確認に乗ります。

> **注意**: 動作確認には `publish\` ではなく `AIUsageOverlay\bin\Release\` の exe を使ってください。
> `publish\AIUsageOverlay.exe` を起動したままにすると、次の `build-release.bat` が
> `GenerateBundle` の `UnauthorizedAccessException` で失敗します（出力先を上書きできないため）。

---

## アーキテクチャ

```
AIUsageOverlay/
├── Resources/
│   ├── app.ico              # トレイアイコン（カスタム）
│   └── Report/              # 使用量レポートの HTML テンプレート（埋め込みリソース）
├── Models/
│   ├── AppSettings.cs       # 表示・通知・更新確認・レポートなどの設定
│   ├── *UsageData.cs        # Claude / Copilot / Codex / Cursor の使用量モデル
│   ├── UsagePace.cs         # 予定比・枯渇予測
│   ├── UpdateInfo.cs        # GitHub Release の更新情報
│   ├── CursorTokenRecord.cs # Cursor のトークン累積（cursor-tokens.json）
│   └── SessionStats*.cs     # 使用量レポートの集計レコードとキャッシュ
├── Services/
│   ├── ClaudeApiClient.cs   # WebView2 で claude.ai API を呼び出す
│   ├── GitHubWebScraper.cs  # GitHub Copilot 使用量取得
│   ├── CodexWebScraper.cs   # Codex 使用量取得
│   ├── CursorWebScraper.cs  # Cursor 使用量取得とトークンイベントの傍受
│   ├── Parsing/             # 各サービスJSON・Release・SemVer・履歴JSONLの純粋パーサ
│   ├── SessionStats/        # 履歴の走査・重複排除・分類・集計・HTML生成
│   ├── UpdateCheckService.cs # GitHub Release取得とバージョン比較
│   └── UsageService.cs      # 設定・取得・フォールバックの統合
├── ViewModels/
│   └── MainViewModel.cs     # INotifyPropertyChanged / DispatcherTimer
├── App.xaml(.cs)            # 起動・トレイアイコン管理・動的アイコン生成
├── MainWindow.xaml(.cs)     # 常時最前面オーバーレイ
├── SettingsWindow.xaml(.cs) # 設定ダイアログ
└── LoginWindow.xaml(.cs)    # WebView2 ログインウィンドウ
```

**使用量データ取得フロー:**
1. 各サービスの認証済みWebView2セッションで使用量ページへアクセス
2. ページ内の通信またはレスポンスを取得
3. `Services/Parsing/`のサービス別ParserでJSONを解析
4. `UsageService`と`MainViewModel`を経由して表示を更新

**アプリ更新確認フロー:**
1. 起動30秒後、以降は6時間タイマーと24時間ゲートで確認
2. `UpdateCheckService`がGitHub Releasesの公開メタデータをGET
3. 実行中アプリと最新版を厳密な3成分SemVerで比較
4. 新版がある場合だけトレイ通知し、GitHub Releaseページを案内

**使用量レポート生成フロー:**
1. トレイメニューから起動。名前付き Mutex で多重起動時の同時集計を防ぐ
2. `SessionStatsScanner` が `.claude\projects` / `.codex\sessions` /
   `.cursor\projects`（`agent-transcripts` 配下）に加え、Cowork の
   `%AppData%\Claude\local-agent-mode-sessions\<内部ID>\<アカウントID>\<セッションID>\.claude\projects\**\*.jsonl`
   だけを限定走査する。
   前回から変わっていないファイルはキャッシュのレコードを流用する
3. `Services/Parsing/` のパーサが JSONL を 1 行ずつ構造体へ変換する
4. 重複を除いて正規化する。Claude Code / Cowork は同じ応答が複数行に書かれるため
   `(requestId, message.id)` で一意化し、Codex は `session_meta.id`（スレッド固有 ID）で数える
5. `PhaseClassifier` がツール実行を 調査 / 実装 / テスト / 対話 / 未分類 へ分類する
6. `SessionStatsAggregator` が期間・プロジェクト・日・モデル別に畳み、
   `ModelPricing` の係数表で Claude Code / Cowork の AP・構成比・対象率を求める
7. `UsageReportRenderer` が埋め込みテンプレートへ JSON を差し込み、HTML を書き出す
8. 既定ブラウザで開く

**トレイアイコン更新フロー:**
1. `DispatcherTimer` が定期的に `MainViewModel.RefreshUsageAsync()` を呼び出す
2. `SessionPercent` プロパティが変化すると `PropertyChanged` イベントが発火
3. `App`がイベントを受け取り、`TrayIconRenderer`で選択形式の32×32ビットマップを生成
4. `NotifyIcon.Icon` を差し替えてトレイアイコンを更新、`Text` にツールチップを設定

---

## プライバシー・セキュリティ

- **利用データの外部送信なし** — 使用量取得は各サービスとの直接通信に限定
- **更新確認は公開情報のGETのみ** — GitHub Releasesへ利用データ・認証情報・テレメトリを送信しない
- **認証情報の保存場所** — `%TEMP%\AIUsageOverlay_WebView2`（WebView2 の標準プロファイル）
- **設定・計測ファイル** — `%AppData%\AIUsageOverlay\`
  （`settings.json` / `usage.json` / `history.json` / `session-stats-cache.json` /
  `cursor-tokens.json`）
- **使用量レポートはローカル完結** — 履歴ファイルは読み取り専用で開き、書き込まない。
  生成した HTML は外部参照を一切持たず、スクリプトからの通信も行わない
- **レポートに載る情報** — 各セッションの最初の指示文の先頭 64 文字（設定で OFF 可）と、
  プロジェクトのフルパス。会話の本文は載せない。
  **生成した HTML ファイル自体を利用者が転送することは防げない**ため、渡す相手は選ぶこと

---

## 謝辞

トレイアイコンの2段バーデザイン・stale 減光表現・使用率レベルの閾値色など、一部の UI ロジックは
以下のオープンソース実装（いずれも MIT License）を参考に移植しています。

- [CodexBar](https://github.com/steipete/CodexBar)（steipete 氏、macOS / Swift）
- [Win-CodexBar](https://github.com/Finesssee/Win-CodexBar)（Finesssee 氏、Windows / Rust + Tauri）

Swift / Rust コードの複製ではなくロジック（描画パラメータ・閾値）の移植であり、
該当ファイルのヘッダに移植元を明記しています。

## ライセンス

MIT License
