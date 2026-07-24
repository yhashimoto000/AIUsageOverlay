# AIUsageOverlay 変更仕様書 — 自動アップデート機能

| 項目 | 内容 |
|------|------|
| 作成日 | 2026-07-24 |
| 対象 | AIUsageOverlay（C# / .NET 9 WPF、`net9.0-windows`） |
| 起票理由 | 新バージョン公開時に、アプリ側で更新を検知・通知し、半自動で適用できるようにする |
| 配布経路 | GitHub Releases（`release.yml` が v* タグ push で zip を自動添付） |
| 適用方式 | 半自動（ダウンロードはアプリが自動、適用はユーザーがボタン操作） |
| バージョン体系 | SemVer に統一。**v2.0.0 で仕切り直し**（過去タグ v1.40 等の表記揺れは比較対象外として無視） |
| ヘルパー形態 | 小型 `updater.exe`（P6。フォルダ差し替え専用の第二成果物） |
| 完全性検証 | SHA256 照合を追加（`checksums.txt` を Release 同梱）。**当面は無署名**（検証後 MOTW 除去で SmartScreen 回避） |
| 粒度 | 機能 F-18〜F-27 / フェーズ P5（検知・通知）〜P6（半自動適用） |
| ビルド検証 | 本書は設計のみ。実装後の `dotnet build -c Release` は Windows 実機で別途実施。 |

### 前提条件（実装前に確定・整備が必要）
1. **リポジトリの public 化**: 未認証 `GET /repos/{owner}/{repo}/releases/latest` を使うため、`github.com/yhashimoto000/AIUsageOverlay` を public にする（private のままだと 404）。GitHub 側の手動操作（Settings → Change repository visibility）。
2. **owner/repo の確定**: 本書は `yhashimoto000/AIUsageOverlay` を API URL に用いる前提。相違があれば実装時に修正。
3. **csproj `<Version>` の設定**: 現状 csproj に `<Version>` が無く、ビルドされた exe は自分を `1.0.0` と誤認する（比較が原理的に成立しない）。F-18 で最優先に是正する。

---

## 1. 目的・背景

新バージョンを GitHub Releases に公開した際、ユーザーが手動でリポジトリを確認しなくても、アプリが更新の有無を検知して知らせ、最終的にはボタン操作で適用できるようにする。

多観点の技術精査（5観点）の結論は次のとおり。

- **自前実装が最適**。自己更新ライブラリ（Velopack 等）は技術的に優れる（delta・rollback 内蔵）が、独自パッケージ形式・独自インストールレイアウトを強制し、**現在の素 zip 配布・`release.yml` を全面的に作り替える**ことになる。最小依存方針・既存リリース資産の温存に反するため不採用。将来 delta やロールバックが必須要件になった時点で「配布方式ごと移行」を判断する。
- **最大の落とし穴**: `csproj` に `<Version>` が無いため exe が自分を `1.0.0` と認識する。また SemVer 比較に `System.Version` は使えない（`v1.40` を `1.40` と解釈し、将来の `v1.7.0` を旧版と誤順序にする）。この土台を最初に固める。
- **実行中プロセスの自己差し替えは罠が多い**ため、段階分けする。P5（検知・通知・手動 DL 導線）は外部への GET とローカル UI だけで完結し危険がない。P6（半自動適用）でファイル差し替えの本丸に踏み込む。

### CLAUDE.md「外部サーバー送信を追加しない」との整合
GitHub Releases API へのアクセスは公開メタデータの **GET（取得）**であり、ユーザーの使用量等のデータをボディ・クエリに一切載せない。したがって規約が禁じる「送信（テレメトリ）」には当たらず、「更新情報の取得」に該当する。ただし発信元 IP とアクセス時刻は相手に残るため、`AutoUpdateCheckEnabled`（既定 true）でオプトアウト可能にして姿勢を担保する。

---

## 2. 参照（現状コードの該当箇所）

| ファイル | 参照内容 |
|----------|----------|
| `AIUsageOverlay/AIUsageOverlay.csproj` | `<Version>` 未設定（F-18 で追加）。`PublishSingleFile=true`、ネイティブ DLL は exe 外部出力 |
| `.github/workflows/release.yml` | v* タグ push で `--self-contained true` publish → `AIUsageOverlay_v{version}.zip` を Release 添付。`-p:Version` 注入なし（F-18）、checksums なし（F-24） |
| `build-release.bat` | ローカルは `--self-contained false`（CI と方針差、§10 R-8） |
| `App.xaml.cs` | `App_Startup`（Mutex なし＝F-25 対象）、`NotifyIcon`、`BuildTrayContextMenu`（トレイメニュー）、`AttachNotifier` |
| `Services/NotificationService.cs` | `NotifyIcon.ShowBalloonTip` によるトースト（使用率通知専用。F-23 で汎用 `NotifyInfo` 追加） |
| `Services/UsageService.cs` | 設定の SSoT（`LoadSettings`/`SaveSettings`、`settings.json`）。更新チェックは相乗りさせない |
| `ViewModels/MainViewModel.cs` | `DispatcherTimer`（使用量取得専用）。`SaveSettings` パススルー未公開（F-22 で追加） |
| `SettingsWindow.xaml(.cs)` | TabControl 構成。`Environment.ProcessPath` 使用箇所あり（バージョン取得の参考） |
| `Models/AppSettings.cs` | 全項目に既定値（未知キー補完で後方互換）。更新設定を追加（§6） |

---

## 3. 機能対比サマリ

| 機能 | 現状 | 本仕様 | F番号 | フェーズ |
|------|------|--------|-------|----------|
| 自バージョン | exe が自分を 1.0.0 と誤認 | csproj `<Version>`＋タグ注入で正しい版を保持 | F-18 | P5 |
| バージョン比較 | なし | 軽量 SemVer 型（3成分厳密、非 SemVer タグ無視） | F-19 | P5 |
| 最新版取得 | なし | GitHub Releases API を GET しパース | F-20/F-21 | P5 |
| チェック起動 | なし | 起動遅延＋6h タイマー＋24h ゲート、ON/OFF 可 | F-22 | P5 |
| 更新通知・導線 | なし | トレイ通知＋メニュー＋設定画面（現在版・今すぐ確認） | F-23 | P5 |
| 完全性検証基盤 | なし | Release に checksums.txt 同梱 | F-24 | P5(リリース) |
| 単一起動 | Mutex なし（多重起動可） | 名前付き Mutex で単一起動化 | F-25 | P6 |
| 半自動DL | なし | staging へ自動 DL＋SHA256 照合＋MOTW 除去 | F-26 | P6 |
| 自己適用 | なし | updater.exe でアトミック差し替え＋ロールバック | F-27 | P6 |

---

## 4. 変更方針とスコープ

> **実装状況メモ**: 本書時点で実装未着手。P5 を先行リリースし（検知・通知・手動 DL 導線まで）、P6（半自動適用）は P5 の運用実績を見てから着手する。

### 4.1 フェーズ構成
| フェーズ | 主題 | 含む機能 | 成立条件 |
|----------|------|----------|----------|
| P5 | 更新検知・通知・手動DL導線 | F-18, F-19, F-20, F-21, F-22, F-23, F-24 | 外部 GET とローカル UI のみ。ファイル差し替えなし＝低リスク |
| P6 | 半自動適用 | F-25, F-26, F-27 | 実行中プロセスの自己差し替え。updater.exe・ロールバック必須 |

### 4.2 スコープ外と理由
| 項目 | 理由 |
|------|------|
| delta（差分）更新 | full（~70MB zip 毎回取得）で開始。delta 必須になれば Velopack 移行を別途判断 |
| コード署名 | 当面は無署名＋検証後 MOTW 除去。SmartScreen 完全回避が必要になれば別途コスト判断 |
| ベータチャネル購読 | `/releases/latest` は prerelease を自動除外。安定版のみ対象 |
| オーバーレイ常時バナー | トレイ＋設定画面で導線は足りる。1a/1b の窮屈な領域を圧迫しない（任意扱い） |
| 過去タグの再付番 | v1.40 等は比較対象外として無視。破壊的 git 操作（タグ削除）はしない |

### 4.3 CLAUDE.md 整合
| ルール | 本改修での順守 |
|--------|----------------|
| 外部サーバー送信を追加しない | GitHub API は GET（取得）のみ。利用データ非送信。`AutoUpdateCheckEnabled` でオプトアウト可 |
| パースは `Services/Parsing/` に置く | Release JSON パースは `GitHubReleaseParser`、SemVer パースも Parsing 配下 |
| プロパティ更新は `SetProperty<T>` | 設定関連の VM プロパティは `SetProperty` 経由 |
| WebView2/Scraper ライフサイクルを壊さない | HttpClient は完全独立系統。WebView2 に一切触れない |
| 最小実装 | ライブラリ非採用、依存追加なし（BCL の HttpClient のみ）、release.yml 温存 |
| 信頼できないソースからのDL・実行を避ける | DL 元を github.com / api.github.com に固定、HTTPS 強制、SHA256 照合、適用はユーザー操作 |

---

## 5. 機能仕様

### F-18: バージョン管理基盤【P5】

**概要**: exe が自分の正しい版数を持てるようにする。これが全機能の土台。

**現状**: csproj に `<Version>` が無く、生成 AssemblyInfo は `1.0.0`。`release.yml` も `-p:Version` を注入しないため、どのタグでビルドしても exe は自分を 1.0.0 と認識する。

**本アプリでの仕様**:
- `AIUsageOverlay.csproj` に `<Version>2.0.0</Version>` を追加（仕切り直しの初期値。開発時フォールバック）。
- `release.yml` の `dotnet publish` に `-p:Version=${GITHUB_REF_NAME#v}` を注入し、**タグを単一の真実源**にする（例 `v2.0.0` → `2.0.0`）。`build-release.bat` にも同様に任意で `-p:Version` を渡せるようにする。
- 実行時の自バージョンは `AssemblyInformationalVersionAttribute` を読み、`'+'` 以降（`+<githash>`）を除去して使う（`Assembly.Location` は SingleFile で空になるため使わない）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `AIUsageOverlay.csproj` | `<Version>2.0.0</Version>` 追加 |
| `.github/workflows/release.yml` | publish に `-p:Version=${GITHUB_REF_NAME#v}` 注入 |
| （実行時取得は F-21 の UpdateCheckService に実装） | — |

**リスク**: タグと csproj のドリフト。タグ注入を真実源とし、csproj 値は開発時のみと位置づける。

---

### F-19: 軽量 SemVer 型【P5】

**概要**: `System.Version` は使わず、3成分厳密の SemVer 比較を `Services/Parsing/` に自前実装する。

**本アプリでの仕様**:
- 正規表現 `^v?(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$` で major.minor.patch の3成分が揃うものだけ受理。
- `v1.40` 等の2成分・不正タグはパース失敗として**比較対象外（無視）**。
- prerelease（`-rc.1` 等）は build metadata（`+hash`）を除去して比較。安定版優先の順序付け。
- NuGetVersion 等の重い依存は追加しない（最小実装）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Services/Parsing/SemVer.cs`（新規） | 軽量 SemVer 型。パース（失敗時 null）と比較 |

**リスク**: prerelease 優先順位の境界ケース。自動テストが無いため手動検証（§9）。

---

### F-20: GitHubReleaseParser【P5】

**概要**: GitHub Releases API の JSON をパースして `UpdateInfo` POCO を返す。既存 Parser 規約（static・純粋・入力=生JSON・失敗時 null）に沿う。

**本アプリでの仕様**:
- 入力: `GET /releases/latest` のレスポンス JSON 文字列。
- 抽出: `tag_name`、`html_url`（リリースページ）、`assets[]` から zip の `browser_download_url` と `size`、`name`。
- 出力: `Models/UpdateInfo`（`LatestVersion` / `DownloadUrl` / `AssetName` / `Size` / `HtmlUrl`）。
- アセット名はハードコードせず `assets[]` から `.zip` を選ぶ（命名変更に依存しない）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Services/Parsing/GitHubReleaseParser.cs`（新規） | JSON → UpdateInfo（static 純粋クラス） |
| `Models/UpdateInfo.cs`（新規） | 更新情報 POCO |

**リスク**: JSON スキーマ変更。`System.Text.Json` で寛容にパースし、欠落時は null。

---

### F-21: UpdateCheckService【P5】

**概要**: GitHub Releases API を取得し、自バージョンと比較して更新有無を判定するサービス。

**本アプリでの仕様**:
- `Services/UpdateCheckService.cs`（新規）。`static readonly HttpClient` を1つ保持。
  - `DefaultRequestHeaders`: `User-Agent`（必須。例 `AIUsageOverlay/2.0.0`）、`Accept: application/vnd.github+json`。数秒のタイムアウト。
- `GET https://api.github.com/repos/yhashimoto000/AIUsageOverlay/releases/latest` → `GitHubReleaseParser` でパース。
- 自バージョン（F-18）と `UpdateInfo.LatestVersion`（F-19 SemVer）を比較し、新しければ `UpdateInfo` を返す（なければ null）。
- 例外は握りつぶさず意味あるメッセージでログ。失敗時は既存の「前回表示維持」同様サイレントに次回リトライ。
- **DL はしない**（P5 は検知のみ。`DownloadUrl`/`HtmlUrl` を保持して導線に渡す）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Services/UpdateCheckService.cs`（新規） | HttpClient 取得・比較。DL 制御は P6 で追加 |

**リスク**: レート制限（未認証 60/h/IP、24h スロットルで余裕）。プロキシ環境での失敗はサイレント処理。

---

### F-22: 更新チェックのオーケストレーション【P5】

**概要**: いつ・どの頻度でチェックするかを `App.xaml.cs` に置く（使用量ループに相乗りさせない）。

**本アプリでの仕様**:
- `App.xaml.cs` に専用 `DispatcherTimer`（粗い間隔、例 6h）＋起動遅延ワンショット（`InitialRefreshDelaySeconds` パターンに倣う）。
- 内部で `AppSettings.LastUpdateCheckAt` を見て **24h 経過時のみ**実 GET。重複実行防止に専用フラグ/`SemaphoreSlim`。
- `AppSettings.AutoUpdateCheckEnabled` が false なら timer を張らない／即 return。
- 設定の永続化は `MainViewModel` に `SaveSettings` パススルーを1本追加し `UsageService` に一本化（App から直接 settings.json を書かない）。
- `SkippedUpdateVersion` に一致する版は通知を抑制（「このバージョンをスキップ」対応。任意）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `App.xaml.cs` | 専用タイマー・24h ゲート・チェック起動 |
| `ViewModels/MainViewModel.cs` | `SaveSettings(AppSettings)` パススルー追加 |

**リスク**: 使用量ループ（F-10 適応間隔・F-11 スヌーズ・`_refreshGate`）と分離しないと 24h 意図がぶれる。専用タイマーで独立させる。

---

### F-23: 更新通知と手動DL導線【P5】

**概要**: 更新検知時にトレイ通知し、ユーザーがリリースページ／DL を開ける導線を用意する。

**本アプリでの仕様**:
- `NotificationService` に汎用 `public void NotifyInfo(string title, string message)` を1本追加（`NotifyIcon.ShowBalloonTip` 再利用。使用率通知の `Evaluate` 状態機械とは独立）。
- トレイメニュー（`BuildTrayContextMenu`）に「更新を確認 / 更新があります(vX.Y.Z)」項目を追加。クリックで **P5 では**リリースページ（`HtmlUrl`）を既定ブラウザで開く（`Process.Start` with `UseShellExecute`）。
- `SettingsWindow` に「バージョン情報」欄を追加: 現在バージョン表示・「今すぐ確認」ボタン（24h ゲートと `SkippedUpdateVersion` を無視して実行）・自動チェック ON/OFF トグル。
- ダウンロード自体は P5 ではユーザーがブラウザで行う（アプリからの自動 DL は P6）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Services/NotificationService.cs` | `NotifyInfo` 追加 |
| `App.xaml.cs` | トレイメニュー項目追加、通知→ブラウザ導線 |
| `SettingsWindow.xaml(.cs)` | バージョン情報欄・今すぐ確認・自動チェックトグル |

**リスク**: 安全規則「ファイルのダウンロードは明示許可」。P5 はアプリが DL せず、ユーザーがブラウザで取得するため抵触しない。

---

### F-24: Release への checksums.txt 同梱【P5（リリース基盤）】

**概要**: P6 の SHA256 照合に備え、Release に各アセットの SHA256 を同梱する。

**本アプリでの仕様**:
- `release.yml` に、zip の SHA256 を計算して `checksums.txt`（`<sha256>  <filename>` 形式）を生成し Release アセットに追加するステップを足す。
- P5 時点では照合ロジックは動かさない（リリース側の準備のみ）。P6 の F-26 が参照する。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `.github/workflows/release.yml` | checksums.txt 生成・添付ステップ追加 |

**リスク**: 同一チャネル配布のため改ざん検知にはならない（破損検知どまり。真の改ざん耐性は署名でのみ、§10 R-4）。

---

### F-25: 単一起動 Mutex【P6】

**概要**: 更新適用中の多重起動による差し替え破綻を防ぐため、単一起動制御を追加する。

**現状**: `App.xaml.cs` の `App_Startup` に単一起動制御が無く、複数インスタンスが同時起動しうる。

**本アプリでの仕様**:
- `App_Startup` 冒頭で名前付き Mutex（例 `"Local\\AIUsageOverlay"`）を取得。既に取得済みなら2つ目のインスタンスは即終了（既存インスタンスを前面化する処理は任意）。
- 既存のトレイ常駐・起動フローを壊さない（可逆・小変更）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `App.xaml.cs` | `App_Startup` 冒頭に Mutex 単一起動ガード |

**リスク**: Mutex 解放漏れで再起動できなくなる事態を避け、`GC.KeepAlive` 相当でアプリ生存中保持し、終了時に解放。

---

### F-26: 半自動ダウンロード（staging）＋完全性検証【P6】

**概要**: 検知した更新の zip を staging に自動 DL し、SHA256 で検証する。install ディレクトリには一切触れない。

**本アプリでの仕様**:
- DL 先: `%LocalAppData%\AIUsageOverlay\update\staging\`。install ディレクトリと分離。
- `UpdateCheckService` に DL メソッドを追加。`browser_download_url` から zip を取得。リダイレクト先ホストを github.com / objects.githubusercontent.com 等の既知ドメインに制限し、HTTPS ダウングレードを拒否。
- 検証: `checksums.txt`（F-24）を取得し、DL した zip の SHA256 と照合。加えて API の `size` と実 DL サイズを事前照合（安価な打ち切り検知）。
- 検証成功後にのみ zip 実体の `Zone.Identifier`（MOTW）を除去（自前検証で担保。安全シグナル削除のトレードオフは §10 R-3 に明記）。
- 半自動: DL は自動だが、展開・差し替えは F-27 の「適用」ボタンまで実行しない。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Services/UpdateCheckService.cs` | staging への DL・SHA256 照合・MOTW 除去 |
| `Services/Parsing/`（checksums パース） | checksums.txt のパース（`<sha256>  <name>`） |

**リスク**: 破損・部分 DL → SHA256/size で検知しアボート。改ざんは検知不可（§10 R-4）。

---

### F-27: 自己適用機構（updater.exe）＋適用導線【P6】

**概要**: 実行中プロセスは自分自身のフォルダを差し替えられないため、外部の小型 `updater.exe` が「本体終了待ち → アトミック差し替え → 再起動」を担う。

**本アプリでの仕様**:
- 新規プロジェクト `Updater`（小型コンソール/WinExe）。zip に同梱して配布（`release.yml`/`build-release.bat` で publish・同梱）。
- 起動: 本体の「適用」操作で、`updater.exe` を **install ディレクトリ外（%TEMP%）にコピーしてから**起動（自分がロック元にならないため）。引数に本体 PID・installDir・staging パス・旧版バックアップ先を渡す。本体は `App.ExitApplication()` でクリーン終了。
- 差し替え手順（アトミック・ロールバック付き）:
  1. `OpenProcess`+`WaitForSingleObject` で本体 PID の終了を待つ（タイムアウト例 30 秒＋rename リトライ）。
  2. staging を install ディレクトリと同一ボリューム前提で展開。
  3. `installDir` → `installDir.bak`（旧版 last-known-good 退避、リネーム）。
  4. staging（展開済み新版）→ `installDir`（リネーム昇格）。
  5. `installDir\AIUsageOverlay.exe` を再起動。
  6. 新版が起動成功マーカー（N 秒以内に heartbeat/初回起動フラグ）を書けば `.bak` 削除。書けなければ `.bak` を `installDir` へ戻して**自動ロールバック**。
- **install 位置は変えない**（同一パス in-place）ことで自動起動（HKCU Run）・ショートカット整合を維持。
- 昇格: 適用前に installDir へ一時ファイルを作成して書込可否を判定。可なら非昇格。不可（Program Files 等）のときだけ `install-runtime.bat` と同じ self-elevate（`net session` 判定 → `Start-Process -Verb RunAs`）で updater を昇格。
- 適用導線: トレイメニュー「更新を適用」＋ SettingsWindow の「適用」ボタン（DL 済み・検証済みのときのみ活性）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Updater/`（新規プロジェクト） | 小型 updater.exe。PID 待ち・アトミック差し替え・ロールバック・昇格 |
| `.github/workflows/release.yml` / `build-release.bat` | updater.exe を publish・zip 同梱 |
| `App.xaml.cs` | 適用起動（updater を %TEMP% へコピー→引数付き起動→本体終了） |
| `MainWindow`（起動成功マーカー書き込み） | 新版初回起動で heartbeat フラグを書く |

**リスク**: AV 誤検知（自己書き換え・DL 実行体）。半自動（適用=明示操作）・専用実行体・.bat 多用回避で軽減。差し替え中断は `.bak` ロールバックで last-known-good を保持。

---

## 6. AppSettings 追加プロパティ一覧

| プロパティ | 型 | 既定値 | 機能 |
|-----------|----|--------|------|
| `AutoUpdateCheckEnabled` | `bool` | `true` | F-22。自動更新チェックの ON/OFF（オプトアウト） |
| `LastUpdateCheckAt` | `DateTime?` | `null` | F-22。前回チェック時刻（24h ゲート判定） |
| `SkippedUpdateVersion` | `string` | `""` | F-22/F-23。通知を抑制する版（「このバージョンをスキップ」） |

`settings.json` 例（追加分）:
```json
{
  "autoUpdateCheckEnabled": true,
  "lastUpdateCheckAt": null,
  "skippedUpdateVersion": ""
}
```
既存 `settings.json` は未知キー補完（既定値）で後方互換を保つ。

---

## 7. 画面変更

### 7.1 トレイメニュー（`BuildTrayContextMenu`）
```text
Before                          After（P5）                         After（P6）
● 表示 / 非表示                  ● 表示 / 非表示                       ● 表示 / 非表示
● 更新を一時停止 ▸               ● 更新を一時停止 ▸                    ● 更新を一時停止 ▸
─────────                       ● 更新を確認 / 更新があります(v2.1.0)  ● 更新を適用(v2.1.0 · DL済み)
● 終了                          ─────────                            ─────────
                                ● 終了                               ● 終了
```

### 7.2 SettingsWindow（バージョン情報欄・新規タブ or 既存タブ末尾）
| 要素 | 内容 |
|------|------|
| 現在のバージョン | `v2.0.0`（F-18 の実行時取得） |
| 自動で更新を確認する | チェックボックス（`AutoUpdateCheckEnabled`） |
| 今すぐ確認 | ボタン（24h ゲート無視で即チェック→結果表示） |
| （P6）ダウンロード状況 / 適用 | 「更新をダウンロード」「適用して再起動」ボタン |

### 7.3 通知
- トレイバルーン（`NotifyInfo`）で「新しいバージョン v2.1.0 があります」。クリックで P5=リリースページ、P6=設定の適用導線へ。

---

## 8. 実装フェーズ計画と依存関係

```text
P5: 検知・通知・手動DL導線
  F-18（版数基盤）──┬─→ F-19（SemVer）──→ F-20（Parser）──→ F-21（UpdateCheckService）
                    │                                              │
                    │                                              ▼
                    └────────────────────────────→ F-22（App オーケストレーション）
                                                                   │
                                                                   ▼
                                                    F-23（通知・手動DL導線）
  F-24（release.yml checksums 同梱）… P5 のリリースから先行整備（P6 が参照）

P6: 半自動適用（P5 完了後）
  F-25（単一起動 Mutex）── 独立・前提整備
  F-26（staging DL＋SHA256 照合）── 依存: F-21, F-24
  F-27（updater.exe 適用）── 依存: F-25, F-26
```

- P5（F-18〜F-24）で「更新に気づける」状態を確立。ファイル差し替えを含まないため低リスクで先行リリース可能。
- P6（F-25〜F-27）で半自動適用。updater.exe とロールバックが本丸。P5 の運用実績を見て着手。

---

## 9. テスト観点（手動確認項目）

自動テストプロジェクトは無いため、Windows 実機で確認する。

| 対象(F) | 確認項目 |
|---------|----------|
| F-18 | タグ `v2.0.0` でビルドした exe が自分を `2.0.0` と認識する（1.0.0 固定バグの解消） |
| F-19 | `v2.1.0 > v2.0.0` を検知。`v1.40` 等の非 SemVer タグは無視される。`-rc.1` が安定版より古いと判定される |
| F-20 | 実際の Release JSON から tag/DL URL/size が正しく抽出される |
| F-21 | 最新版が自分より新しいときのみ更新ありと判定。ネットワーク失敗時にクラッシュせずサイレント |
| F-22 | 起動時＋24h ゲートで実チェックが走る。`AutoUpdateCheckEnabled=false` で一切通信しない。手動「今すぐ確認」は即実行 |
| F-23 | 更新検知でトレイ通知。メニュー/設定からリリースページが開く。現在バージョンが正しく表示 |
| F-24 | Release に checksums.txt が添付され、zip の SHA256 と一致する |
| F-25 | 2つ目のインスタンス起動が抑止される。通常起動・トレイ常駐は従来どおり |
| F-26 | staging に DL され SHA256 照合が通る。破損 zip（改変）で照合失敗しアボート。install ディレクトリが無傷 |
| F-27 | 「適用」で本体終了→差し替え→新版再起動。フォルダ位置不変で自動起動が維持。差し替え失敗時に旧版へロールバック。WebView2 認証セッション（%TEMP%）が更新後も維持される |
| 全般 | 更新後に settings.json/usage.json/history.json（%AppData%）が保持される |

---

## 10. リスク・注意事項

| # | リスク | 対応 |
|---|--------|------|
| R-1 | csproj Version 未設定で比較が成立しない | F-18 を最優先。タグ注入を真実源に |
| R-2 | `System.Version` で `v1.40` を誤順序 | F-19 の厳密 SemVer で3成分のみ受理、非 SemVer は無視 |
| R-3 | 未署名 exe の SmartScreen 警告 | 検証後 MOTW 除去で回避（安全シグナル削除のトレードオフを承知の上）。将来署名を検討 |
| R-4 | 同一チャネル配布で改ざん検知不可 | SHA256 は破損検知どまり。改ざん耐性は発行アカウント 2FA＋タグ保護で割り切り、真の耐性は署名でのみ |
| R-5 | 実行中プロセスの自己差し替え失敗 | 外部 updater.exe が本体終了後に実施。install 外から起動。アトミック rename＋`.bak` ロールバック |
| R-6 | 多重起動で差し替え破綻 | F-25 の単一起動 Mutex |
| R-7 | Program Files 配置時の書込み昇格 | 書込可否を事前判定し、不可時のみ self-elevate。ドキュメントで「無人更新は Program Files 以外へ」明記 |
| R-8 | release.yml（self-contained）と build-release.bat（framework-dependent）の方針差 | 更新は self-contained 前提に固定。両経路の統一を推奨（更新後にランタイム不足で起動不能を防ぐ） |
| R-9 | AV が自己更新の挙動を誤検知 | 半自動・専用実行体・.bat 回避で軽減。起動成功マーカー未達で自動ロールバック |
| R-10 | 無断の外部通信がプライバシー方針と衝突 | GET のみ・利用データ非送信・宛先固定。`AutoUpdateCheckEnabled` でオプトアウト |
| R-11 | private リポジトリのままだと 404 | 前提条件（public 化）を実装前に完了 |

---

## 11. ライセンス表記

本改修は本アプリ独自実装であり、自己更新ライブラリ（Velopack/Squirrel/NetSparkle/AutoUpdater.NET）は採用しない。外部コードの移植を含まないため新たな帰属表記は不要。GitHub Releases API は GitHub の公開 API を規約に従い GET で利用する。
