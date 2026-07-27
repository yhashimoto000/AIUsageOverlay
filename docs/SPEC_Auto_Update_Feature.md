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
| 実装状況 | **P5（F-18〜F-24）実装済み**（2026-07-27）。P6（F-25〜F-27）は未着手。 |
| ビルド検証 | P5 実装後の `dotnet build AIUsageOverlay/AIUsageOverlay.csproj -c Release` は成功（警告0・エラー0）。 |

### 前提条件（実装前に確定・整備が必要）
1. **リポジトリの public 化**: ✅ 完了（`github.com/yhashimoto000/AIUsageOverlay` は public 化済み、確認済み）。未認証 `GET /repos/{owner}/{repo}/releases/latest` が利用可能。
2. **owner/repo の確定**: 本書は `yhashimoto000/AIUsageOverlay` を API URL に用いる前提。相違があれば実装時に修正。
3. **csproj `<Version>` の設定**: 現状 csproj に `<Version>` が無く、ビルドされた exe は自分を `1.0.0` と誤認する（比較が原理的に成立しない）。F-18 で最優先に是正する。
4. **CLAUDE.md / AGENTS.md 改定提案の承認**: ✅ 完了（2026-07-27）。公開メタデータの HTTPS GET を許可し、利用データ・認証情報・テレメトリ等の外部送信禁止を維持する文言へ改定済み。

---

## 1. 目的・背景

新バージョンを GitHub Releases に公開した際、ユーザーが手動でリポジトリを確認しなくても、アプリが更新の有無を検知して知らせ、最終的にはボタン操作で適用できるようにする。

多観点の技術精査（5観点）の結論は次のとおり。

- **自前実装が最適**。自己更新ライブラリ（Velopack 等）は技術的に優れる（delta・rollback 内蔵）が、独自パッケージ形式・独自インストールレイアウトを強制し、**現在の素 zip 配布・`release.yml` を全面的に作り替える**ことになる。最小依存方針・既存リリース資産の温存に反するため不採用。将来 delta やロールバックが必須要件になった時点で「配布方式ごと移行」を判断する。
- **最大の落とし穴**: `csproj` に `<Version>` が無いため exe が自分を `1.0.0` と認識する。また SemVer 比較に `System.Version` は使えない（`v1.40` を `1.40` と解釈し、将来の `v1.7.0` を旧版と誤順序にする）。この土台を最初に固める。
- **実行中プロセスの自己差し替えは罠が多い**ため、段階分けする。P5（検知・通知・手動 DL 導線）は外部への GET とローカル UI だけで完結し危険がない。P6（半自動適用）でファイル差し替えの本丸に踏み込む。

### CLAUDE.md「データ取得は各サービスとの直接通信のみ。外部サーバー送信（テレメトリ等）を追加しない。」との整合
CLAUDE.md の当該行（`触ってはいけない領域`）は二つの要素からなる: (1)「データ取得は各サービス（Claude/GitHub Copilot/Codex）との直接通信のみ」、(2)「外部サーバー送信（テレメトリ等）を追加しない」。

既存3サービス（`ClaudeApiClient`/`GitHubWebScraper`/`CodexWebScraper`）はいずれも WebView2 の認証済みブラウザセッション経由・fetch 傍受のみで通信しており（実コード確認済み）、コードベース全体に生の `HttpClient` による直接 HTTP 通信の前例はない。F-21 の `UpdateCheckService` は `static readonly HttpClient` で api.github.com へ無認証の直接通信を行う、本コードベース初の通信様式・信頼モデルである。

これは (2)「送信禁止」には抵触しない（GET 専用・ユーザーの使用量等のデータをボディ/クエリに一切載せないため）。しかし (1)「各サービスとの直接通信のみ」を文字通り適用すると、Claude/GitHub Copilot/Codex 以外の新しい外部通信先（api.github.com）の追加として扱われる余地がある。

この解釈の相違は CLAUDE.md 自体が定める「止まる条件（スコープの変更）」および「CLAUDE.md 自己改善（プロジェクト独自のルール・規約の新たな指摘時は提案→承認）」の検知トリガーに該当する。よって本書はこの箇所を実装確定事項として扱わず、次の2点をユーザーへの意思決定事項として明示する:

1. ✅ 2026-07-27 に承認を得て、CLAUDE.md / AGENTS.md の当該行を「各サービスとの直接通信、および更新チェック等に必要な公開メタデータの HTTPS GET のみ」に改定した。利用データ・認証情報・テレメトリ等の外部送信禁止は維持している。
2. ✅ `AutoUpdateCheckEnabled` の既定値は `true`（黙示のオプトアウト方式）に確定した。設定画面のトグルを OFF にした場合、起動時・定期タイマーの更新確認通信は行わない。

---

## 2. 参照（現状コードの該当箇所）

| ファイル | 参照内容 |
|----------|----------|
| `AIUsageOverlay/AIUsageOverlay.csproj` | `<Version>` 未設定（F-18 で追加）。`PublishSingleFile=true`、ネイティブ DLL は exe 外部出力 |
| `.github/workflows/release.yml` | v* タグ push で `--self-contained true` publish → `AIUsageOverlay_v{version}.zip` を Release 添付。`-p:Version` 注入なし（F-18）、checksums なし（F-24） |
| `build-release.bat` | ローカルは `--self-contained false`（CI と方針差、§10 R-8） |
| `App.xaml.cs` | `App_Startup`（Mutex なし＝F-25 対象。日本語 XML コメントに「処理順序: 1.〜5.」の番号付き記載があり、追記時はこのコメントとの整合を取る）、`NotifyIcon`、`BuildTrayContextMenu`（トレイメニュー）、`AttachNotifier`。**F-22（専用タイマー）・F-23（トレイメニュー項目）・F-25（Mutex ガード）・F-27（適用起動）の4機能が同ファイルを変更する。** CLAUDE.md の「ファイル全体の再生成は禁止」「大きなファイルは分割して編集」に従い、`App_Startup` 全体を書き直さず対象メソッド・追記位置を絞った差分編集で行う。実装順序は F-25（冒頭に Mutex ガード追加）→ F-22（起動遅延ワンショット＋専用タイマー追加）→ F-23（トレイメニュー項目追加）→ F-27（適用起動処理追加）とし、1コミット1機能の差分編集を徹底する |
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

> **実装状況メモ（2026-07-27）**: P5（検知・通知・手動 DL 導線、F-18〜F-24）は実装済み。P6（半自動適用、F-25〜F-27）は未着手であり、P5 の運用実績確認と人間の明示承認後に着手する。

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
| ベータチャネル購読 | `/releases/latest` は `prerelease: true` のリリースを自動除外する。ただし該当判定は release.yml のタグ名判定に依存するため、release.yml の prerelease 判定を「タグに `-` を含むか」（F-19 の SemVer 正規表現の prerelease 識別子検出と同一基準）に統一した上で安定版のみ対象とする（→ F-24 に修正を追加、§10 R-13 参照） |
| オーバーレイ常時バナー | トレイ＋設定画面で導線は足りる。1a/1b の窮屈な領域を圧迫しない（任意扱い） |
| 過去タグの再付番 | v1.40 等は比較対象外として無視。破壊的 git 操作（タグ削除）はしない |
| P6の自前実装を「最小実装」原則の例外として明記 | §1で述べた通り Velopack 等は delta・rollback を内蔵し技術的に優れるが、自己書き換え（実行中プロセスが自分のインストール先を差し替える）という性質上、ライブラリを採用しても PID 待ち・アトミック差し替え・ロールバック挙動・権限昇格の理解とインテグレーションコストは残り、独自パッケージ形式への移行コストが上乗せされる。既存の素zip配布・release.ymlを温存できる利点がこの上乗せコストを正当化するため、P6（Updaterプロジェクト一式）は「最小実装」原則の意図的な例外として扱う |
| build-release.bat 産 zip の GitHub Release 手動添付 | 自動アップデートの資産源は release.yml が publish する self-contained zip のみに一本化するため対象外。build-release.bat は初回配布・ローカルテスト専用と位置づける（§10 R-8） |
| 公開済みの不良リリースの取り消し・段階配信 | GitHub の `/releases/latest` は作成日時ベースで決まり SemVer 順ではないため、誤配布時のリカバリ手段（kill-switch・段階公開）は本設計に含めない。誤配布時は速やかに修正版を新タグで再公開する運用でカバーする |

### 4.3 CLAUDE.md 整合
| ルール | 本改修での順守 |
|--------|----------------|
| データ取得は許可された直接通信／公開メタデータ HTTPS GET のみ。利用データ等を外部送信しない | ✅ CLAUDE.md / AGENTS.md 改定承認済み。GitHub API は公開メタデータの GET のみに限定し、利用データ・認証情報・テレメトリ等を送信しない。`AutoUpdateCheckEnabled` は既定 `true`、設定で OFF にできる |
| パースは `Services/Parsing/` に置く | Release JSON パースは `GitHubReleaseParser`、SemVer パースも Parsing 配下 |
| プロパティ更新は `SetProperty<T>` | 本機能は VM バインドプロパティを新設しない。F-22 の `MainViewModel` 変更は `SaveSettings(AppSettings)` という素のパススルーメソッド1本のみで、既存の `GetSettings()` パススルーと対称な形。設定値（`AutoUpdateCheckEnabled` 等）はこれまで通り `SettingsWindow.xaml.cs`/`MainWindow.xaml.cs` が `UsageService.GetSettings()/SaveSettings()` で取得した `AppSettings` スナップショットを直接読み書きする既存パターンを踏襲するため、`SetProperty` の適用対象外 |
| WebView2/Scraper ライフサイクルを壊さない | HttpClient は完全独立系統。WebView2 に一切触れない |
| 最小実装 | ライブラリ非採用、依存追加なし（BCL の HttpClient のみ）、release.yml 温存。ただし P6（Updater 一式）はこの原則の意図的な例外（§4.2 参照） |
| 信頼できないソースからのDL・実行を避ける | DL 元を github.com / api.github.com に固定、HTTPS 強制、SHA256 照合、適用はユーザー操作。ホスト/スキーム検証は `UpdateInfo` 生成時点（F-20）に一本化し、F-23（ブラウザ起動）・F-26（DL）は検証済みの値のみを消費する。F-26 のリダイレクト先ホスト制限は実行時の多層防御として別途維持し、F-20 の検証で置き換えない |

---

## 5. 機能仕様

### F-18: バージョン管理基盤【P5】

**概要**: exe が自分の正しい版数を持てるようにする。これが全機能の土台。

**現状**: csproj に `<Version>` が無く、生成 AssemblyInfo は `1.0.0`。`release.yml` も `-p:Version` を注入しないため、どのタグでビルドしても exe は自分を 1.0.0 と認識する。

**本アプリでの仕様**:
- `AIUsageOverlay.csproj` に `<Version>2.0.0</Version>` を追加（仕切り直しの初期値。開発時フォールバック）。
- `release.yml` 内の「Extract version from tag」ステップ（既存・`shell: bash`、タグから `v` を除去し `steps.version.outputs.version` を出力）を「Publish executable」ステップより**前**（Restore の直後）に移動する。
- 「Publish executable」ステップ（`shell` 未指定＝Windows既定の pwsh。既存の `run:` はバックティックによる pwsh 行継続で書かれている）はシェルを変えずそのまま維持し、`dotnet publish` の引数に `-p:Version=${{ steps.version.outputs.version }}` を追加する。`${{ }}` は GitHub Actions がシェル実行前にテンプレート置換するため、pwsh/bash どちらのステップからでもシェル非依存に参照できる。これにより**タグを単一の真実源**にする（例 `v2.0.0` → `2.0.0`）。
- bash 専用のパラメータ展開構文（`${VAR#pattern}` 等）は「Extract version from tag」ステップ内だけに閉じ込め、他ステップ（Publish executable・Create release zip・Create GitHub Release）へ直書きしない。バージョン文字列は `steps.version.outputs.version` の1箇所からのみ導出し、独立な再パースを禁止する（`Create release zip` の既存 `$ver = "${{ github.ref_name }}"` も `$ver = "v${{ steps.version.outputs.version }}"` へ揃える）。
- `build-release.bat` にも同様に任意で `-p:Version` を渡せるようにする（ローカルビルドはタグ情報を持たないため必須にはしない）。
- 実行時の自バージョンは `AssemblyInformationalVersionAttribute` を読み、`'+'` 以降（`+<githash>`）を除去して使う（`Assembly.Location` は SingleFile で空になるため使わない）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `AIUsageOverlay.csproj` | `<Version>2.0.0</Version>` 追加 |
| `.github/workflows/release.yml` | 「Extract version from tag」ステップを Restore 直後（Publish executable より前）へ移動。「Publish executable」（pwsh既定のまま）の `dotnet publish` に `-p:Version=${{ steps.version.outputs.version }}` を注入。「Create release zip」の zip 名生成も同じ出力を再利用するよう統一 |
| （実行時取得は F-21 の UpdateCheckService に実装） | — |

**リスク**: タグと csproj のドリフト。タグ注入を真実源とし、csproj 値は開発時のみと位置づける。加えて、「Publish executable」ステップは shell 未指定＝pwsh のため、bash 専用のパラメータ展開構文（`${GITHUB_REF_NAME#v}` 等）をこのステップに直接書かない（pwsh では未定義変数参照として空文字に評価され、バージョン注入が機能しない）。同ステップに `shell: bash` を明示する代替案も採らない（既存の `run:` がバックティック行継続で書かれており、bash では行継続にならず構文が壊れるため）。バージョン文字列を release.yml 内の複数箇所（zip名生成・Release本文・publish注入）で独立に再パースすると、この F-18 が解消しようとしている「バージョンのドリフト」を release.yml 内で再生産するため、`steps.version.outputs.version` の1箇所に統一する。

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
- **URL 検証**: パース時に `html_url` と `browser_download_url` の URI を検証する。`Uri.TryCreate` で絶対 URI として解釈できない、スキームが `https` でない、または `Host` が許可リスト（`github.com` / `api.github.com` / `objects.githubusercontent.com`）に**完全一致**しない場合（`uri.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase)`。`CodexWebScraper.IsSafeCodexCloudUrl` と同方式。`Contains`/`StartsWith` 等の部分一致は使わない）は、当該フィールド（`HtmlUrl` または `DownloadUrl`）のみを null にする。`LatestVersion` など URL と無関係な項目は維持し、更新検知・通知自体は継続させる（`UpdateInfo` 全体は null にしない）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Services/Parsing/GitHubReleaseParser.cs`（新規） | JSON → UpdateInfo（static 純粋クラス）。ホスト/スキーム検証を含む |
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
- トレイメニュー（`BuildTrayContextMenu`）に「更新を確認 / 更新があります(vX.Y.Z)」項目を追加。クリックで **P5 では**リリースページ（`HtmlUrl`）を既定ブラウザで開く（`Process.Start` with `UseShellExecute`）。`HtmlUrl` が null（F-20 の検証で不正と判定された場合）は、固定のリリース一覧 URL（`https://github.com/yhashimoto000/AIUsageOverlay/releases`）にフォールバックする。`Process.Start`（ShellExecute）には F-20 で検証済みの `HtmlUrl` 以外を渡さない。
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
- `release.yml` の「Create release zip」ステップの**後**・「Create GitHub Release」ステップの**前**に、zip の SHA256 を計算して `checksums.txt` を生成するステップを追加する。生成コマンド（既存 `shell: pwsh` を踏襲）:
  ```powershell
  $hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLower()
  $name = Split-Path -Path $zip -Leaf
  "$hash  $name" | Out-File -FilePath checksums.txt -Encoding ascii
  ```
  - `Get-FileHash` の `.Hash` は既定で大文字16進文字列を返すため `.ToLower()` で小文字化する（照合側は `StringComparison.OrdinalIgnoreCase` で比較するため必須ではないが、生成側・検証側の契約を明確にするため統一する）。
  - `.Path` はフルパス（CI 実行時の絶対パス）を返すため、`Split-Path -Leaf` でファイル名のみを取り出す（フルパスのまま書くと F-26 側のファイル名照合が破綻する）。
  - `-Encoding ascii` を明示し BOM の有無に依存しない（ハッシュ値・ファイル名は ASCII 範囲）。
- 既存の「Create GitHub Release」ステップの `files:` は現状 `files: AIUsageOverlay_v${{ steps.version.outputs.version }}.zip` という**単一ファイル指定**であり、このままでは checksums.txt を生成しても Release アセットとして添付されない（`softprops/action-gh-release@v2` の `files:` は複数行リストで複数アセットを扱う仕様）。次のように複数行化する:
  ```yaml
  files: |
    AIUsageOverlay_v${{ steps.version.outputs.version }}.zip
    checksums.txt
  ```
- P5 時点では照合ロジックは動かさない（リリース側の準備のみ）。P6 の F-26 が参照する。
- 既存の `prerelease: ${{ contains(github.ref_name, '-beta') || contains(github.ref_name, '-rc') }}` を `prerelease: ${{ contains(github.ref_name, '-') }}` に修正する。F-19 の SemVer 正規表現は `-` 以降を任意の prerelease 識別子として受理する設計であり、release.yml 側の判定基準をこれと統一することで、`-alpha`/`-preview` 等の未知の prerelease 命名でも正しく prerelease として扱われ、`/releases/latest` から除外されるようにする（放置すると将来 `v2.1.0-alpha.1` 等が安定版として公開され、全ユーザーに誤って更新通知が出る）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `.github/workflows/release.yml` | ①checksums.txt 生成ステップ追加（Create release zip 後 / Create GitHub Release 前）②既存 `Create GitHub Release` の `files:` を単一ファイルから複数行リスト（zip + checksums.txt）へ変更③`prerelease:` 判定を `contains(github.ref_name, '-')` に統一 |

**リスク**: 同一チャネル配布のため改ざん検知にはならない（破損検知どまり。真の改ざん耐性は署名でのみ、§10 R-4）。`files:` の更新漏れにより checksums.txt が Release アセットとして公開されないと F-26 の検証が完全に機能しなくなるため、実装後は release.yml 上で checksums.txt が実際に Release アセットとして公開されることを手動確認する（§9 のテスト項目に含める）。

---

### F-25: 単一起動 Mutex【P6】

**概要**: 更新適用中の多重起動による差し替え破綻を防ぐため、単一起動制御を追加する。

**現状**: `App.xaml.cs` の `App_Startup` に単一起動制御が無く、複数インスタンスが同時起動しうる。

**本アプリでの仕様**:
- `App_Startup` 冒頭で名前付き Mutex を取得。既に取得済みなら2つ目のインスタンスは即終了（既存インスタンスを前面化する処理は任意）。
- 名前空間は `Local\` ではなく `Global\` を用いる。`Local\` はセッション（RDP/Terminal Services・ファストユーザースイッチング）ごとに独立した名前空間であり、共有インストール先（Program Files 等、R-7）へ複数ユーザーが別セッションから同時ログインした場合、各セッションが独立に Mutex を取得できてしまい単一起動保証が成立しない。`Global\` オブジェクトの作成は対話ログオンユーザーの既定権限（SeCreateGlobalPrivilege）で可能であり追加の昇格は不要。
- Mutex 名にはインストールディレクトリの正規化パスから生成した短いハッシュを含める（例 `Global\AIUsageOverlay_{sha256(installDir)[:16]}`）。固定名のまま `Global\` 化すると、同一マシン上で複数ユーザーがそれぞれ自分の `%LocalAppData%` 配下に個別展開して同時使用する主流の利用形態（設定・認証は既に `%AppData%`/`%TEMP%` でユーザー別に分離済み）まで機械全体で単一化してしまい、無関係な別ユーザーの別インストールの同時起動を阻害する。インストールディレクトリ単位でハッシュ化することで、同一インストールを複数セッションから同時操作する場合のみ正しく排他される。
- 取得した `Mutex` インスタンスは `_notifyIcon` と同様に `App` クラスの `private Mutex? _singleInstanceMutex;` フィールドとして保持する。`App_Startup` 内のローカル変数のままにしない（ローカル変数だと参照が切れて GC 対象になり、ファイナライズ時に OS ハンドルが閉じられて Mutex が意図せず消滅し二重起動を許してしまう）。解放（`ReleaseMutex()`/`Dispose()`）は `ExitApplication()` 内でのみ行う。
- 既存のトレイ常駐・起動フローを壊さない（可逆・小変更）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `App.xaml.cs` | `App_Startup` 冒頭に Mutex 単一起動ガード（`private Mutex? _singleInstanceMutex` フィールド、`Global\` 名前空間＋installDir ハッシュ）。`ExitApplication()` で解放 |

**リスク**: (a) 解放漏れ（Release/Dispose忘れ）による再起動不能、(b) ローカル変数保持のみだと参照が切れて GC 対象になり意図せず Mutex が消滅し二重起動を許す、の双方を避けるため、`App` クラスの private フィールドとしてアプリ生存中保持し `ExitApplication()` でのみ解放する。`Local\` のままだと共有インストール（Program Files 等、R-7）を複数セッション（RDP/共有端末）から同時操作するケースを防げない点に注意（§10 R-6）。

---

### F-26: 半自動ダウンロード（staging）＋完全性検証【P6】

**概要**: 検知した更新の zip を staging に自動 DL し、SHA256 で検証する。install ディレクトリには一切触れない。

**本アプリでの仕様**:
- DL 先: `%LocalAppData%\AIUsageOverlay\update\staging\`。install ディレクトリと分離。
- `UpdateCheckService` に DL メソッドを追加。`browser_download_url` から zip を取得。リダイレクト先ホストを github.com / objects.githubusercontent.com 等の既知ドメインに制限し、HTTPS ダウングレードを拒否。F-20 で `DownloadUrl` の初期ホストは検証済みだが、GitHub の実ダウンロードは `github.com` から `objects.githubusercontent.com` 等へ実行時にリダイレクトされるため、ここでのリダイレクト先ホスト制限は F-20 の検証を代替するものではなく、実行時の多層防御として別途維持する（F-20＝静的文字列の検証、F-26＝HTTP層での実遷移先の検証、と役割を分離する）。
- 検証: `checksums.txt`（F-24）を取得し、DL した zip の SHA256 と照合する。比較は `StringComparison.OrdinalIgnoreCase` を使う（`Get-FileHash` は大文字16進、.NET の `Convert.ToHexString` も大文字16進を返すため通常は一致するが、実装差異による大文字小文字不一致で正規の更新が誤って改ざん検知扱いされないよう明示的に大文字小文字を無視する）。加えて API の `size` と実 DL サイズを事前照合（安価な打ち切り検知）。
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
- 新規プロジェクト `Updater`（`OutputType=WinExe` に確定。コンソールウィンドウを表示しない）。zip に同梱して配布（`release.yml`/`build-release.bat` で publish・同梱）。
- **ログ出力**: WinExe で無表示のため、進捗・エラーはプレーンテキストのログファイルに記録する。出力先は `%LocalAppData%\AIUsageOverlay\update\updater.log`（staging と同系統、install ディレクトリ外）。追記型で、起動時刻・引数（PID/installDir/staging/バックアップ先）、PID 待ち完了、rename 成否、`.bak` ロールバック発生有無、例外メッセージを1行1イベントで記録する。self-elevate 経由で起動された場合も同一ログファイルに追記する。
- 起動: 本体の「適用」操作で、`updater.exe` を **`%TEMP%\AIUsageOverlay_Updater\`（固定パス。WebView2 セッションフォルダ `AIUsageOverlay_WebView2` とは別名・別ディレクトリ）にコピーしてから**起動（自分がロック元にならないため）。updater はこのフォルダ配下にのみ書き込み、`AIUsageOverlay_WebView2` には一切アクセスしない。適用完了後（正常終了・ロールバックいずれの場合も）このフォルダを削除し、コピーの残留を防ぐ。引数に本体 PID・installDir・staging パス・旧版バックアップ先を渡す。
- **起動確認**: `Process.Start` の例外（AppLocker/WDAC 等のポリシーブロックは `Win32Exception` として同期的に返るケースが多い）を捕捉する。加えて起動後数秒のグレースピリオド内に updater.exe からの生存シグナル（起動直後に書く一時フラグファイル、または `Process.HasExited` が false のままであることの確認）が得られない場合は「起動失敗」と判定する。**起動確認できた場合のみ**本体は `App.ExitApplication()` でクリーン終了する。**起動失敗時**は本体を終了させず、「更新を適用できませんでした。手動でダウンロードしてください」とリリースページ（`HtmlUrl`）へのリンクを提示する通知を表示し、旧版のまま動作を継続する（installDir・staging には一切手を付けていないためロールバックは不要）。管理端末で AppLocker/WDAC によるブロックが疑われる場合は IT 部門への許可リスト追加依頼を案内文に含める。
- 差し替え手順（アトミック・ロールバック付き）:
  1. `OpenProcess`+`WaitForSingleObject` で本体 PID の終了を待つ（タイムアウト **30秒**。タイムアウト後は起動失敗として §5 F-27 の「起動確認」経路と同様にエラー通知し中断する）。rename 失敗時は **200ms 間隔・最大10回（計2秒）**のリトライを行い、それでも失敗すれば手順4のロールバック処理に委ねる。
  2. staging（`%LocalAppData%\AIUsageOverlay\update\staging\`）と installDir が同一ボリューム上にあるかを事前検証する。ドライブレター文字列の一致だけでは、フォルダーリダイレクト/ジャンクションにより見かけ上同じドライブレターでも実体が別ボリュームというケースを判定できないため、実ボリューム識別（`GetVolumeInformation` 等によるシリアル番号比較、または対象パスの実ルートを解決した上での比較）で判定する。
     - 同一ボリュームの場合: staging を展開し、以降アトミック rename（手順3・4）で差し替える。
     - 異なるボリューム（FSLogix 等のプロファイル仮想化環境で起こり得る）の場合: installDir に一切触れずに適用を中断し、リリースページへの手動 DL 導線（F-23）を案内する。
  3. `installDir` → `installDir.bak`（旧版 last-known-good 退避、リネーム）。
  4. staging（展開済み新版）→ `installDir`（リネーム昇格）。手順3〜4は try/catch で包む。手順4が例外を送出した場合は直ちに `installDir.bak` → `installDir` へ戻す明示的ロールバックを実行し、適用失敗として通知する（手順3自体が例外を送出した場合はリネームが不可分操作のため installDir は未変更のままであり、追加の復旧操作なしにそのまま中断・通知する）。
  5. `installDir\AIUsageOverlay.exe` を再起動。
  6. 新版が起動成功マーカー（**30秒以内**に heartbeat/初回起動フラグ）を書けば `.bak` 削除。書けなければ `.bak` を `installDir` へ戻して**自動ロールバック**。ロールバックにも失敗した場合はユーザーに手動復旧手順（`.bak` フォルダの手動リネーム）を通知する。
- **install 位置は変えない**（同一パス in-place）ことで自動起動（HKCU Run）・ショートカット整合を維持。
- 昇格: 適用前に installDir へ一時ファイルを作成して書込可否を判定。可なら非昇格。不可（Program Files 等）のときだけ `install-runtime.bat` と同じ self-elevate（`net session` 判定 → `Start-Process -Verb RunAs`）で updater を昇格。
- 適用導線: トレイメニュー「更新を適用」＋ SettingsWindow の「適用」ボタン（DL 済み・検証済みのときのみ活性）。

**変更・新規ファイル**:
| ファイル | 変更 |
|----------|------|
| `Updater/`（新規プロジェクト、`AIUsageOverlay` と兄弟） | 小型 updater.exe（`OutputType=WinExe`、`updater.log` 出力）。PID 待ち・ボリューム検証・アトミック差し替え・ロールバック・昇格 |
| `AIUsageOverlay/AIUsageOverlay.sln` | `Updater` プロジェクトを追加登録（`dotnet sln AIUsageOverlay/AIUsageOverlay.sln add Updater/Updater.csproj` 相当。sln は `AIUsageOverlay/` サブフォルダ内のため `Updater/`（リポジトリ直下）への相対パス解決に注意）。CI（release.yml/build-release.bat）は `.sln` を経由せず `.csproj` を直接 restore/publish するためビルドには影響しない。Visual Studio での通常の開発体験（ソリューションエクスプローラー表示・ビルド構成）から Updater が漏れないようにするための登録 |
| `.github/workflows/release.yml` | 既存の `AIUsageOverlay` publish ステップの直後に `Updater/Updater.csproj` の `dotnet restore`→`dotnet publish -c Release -r win-x64 --self-contained true -o publish/updater/` を追加。出力は `publish/updater/updater.exe`。「Create release zip」ステップの `Copy-Item "publish\*" $tmp -Recurse` は再帰コピーのため、`publish/updater/` サブフォルダごと自動的に zip 側 `updater/updater.exe` として同梱される（Copy-Item 自体の改修は不要） |
| `build-release.bat` | 同様に `Updater/Updater.csproj` の restore/publish を追加し、出力先を `%OUTPUT%\updater\` とする。既存の `Copy-Item '%OUTPUT%\*' $tmp -Recurse` が再帰コピーのため zip 側の追加変更は不要 |
| `App.xaml.cs` | 適用起動（updater を `%TEMP%\AIUsageOverlay_Updater\` へコピー→起動確認→引数付き起動→確認後に本体終了） |
| `MainWindow`（起動成功マーカー書き込み） | 新版初回起動で heartbeat フラグを書く |

zip 展開後の install ディレクトリ内で `updater.exe` は `updater\updater.exe`（サブフォルダ）に格納される。`App.xaml.cs` の適用起動処理はこの相対パス（`installDir\updater\updater.exe`）を参照して `%TEMP%\AIUsageOverlay_Updater\` にコピーする。release.yml / build-release.bat の publish 先とこの相対パスは常に一致させる。

**リスク**: AV 誤検知（自己書き換え・DL 実行体）。半自動（適用=明示操作）・専用実行体・.bat 多用回避で軽減。加えて、AppLocker/WDAC 等の許可リスト型アプリケーション制御が `%TEMP%` からの updater.exe 実行を既定でブロックしうる（AV のヒューリスティック誤検知とは別種。AppLocker既定ルールは「Windowsフォルダ」「Program Files」のみ許可し `%TEMP%` は対象外のため、ポリシー強制環境では起動前に確定的にブロックされ得る）。本体終了前の起動確認（上述）で「本体が無応答のまま消える」事態を防ぐ。差し替え中断は `.bak` ロールバックで last-known-good を保持するが、手順3→4のリネーム自体が例外で失敗するケース（別ボリューム・権限不足等）は、事前のボリューム検証と手順4失敗時の即時ロールバックが無いと last-known-good を保持できない点に注意（上述の差し替え手順で対応済み）。

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
  ▶ 着手ゲート: P5 運用実績の確認 + 規模・攻撃面（self-elevate/自己書き換え）を踏まえた人間の明示承認（スコープ確定の合意）
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
| F-27 | 「適用」で本体終了→差し替え→新版再起動。フォルダ位置不変で自動起動が維持。差し替え失敗時に旧版へロールバック。WebView2 認証セッション（%TEMP%）が更新後も維持される。updater.exe 起動失敗（`Process.Start` を人為的に失敗させる、または AppLocker/WDAC 有効環境）で本体が終了せず「更新を適用できませんでした」の案内が表示され、旧版が動作継続すること。staging と installDir を別ドライブに配置した状態で「適用」し、installDir が消失しないこと |
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
| R-6 | 多重起動で差し替え破綻 | F-25 の単一起動 Mutex（`Global\` 名前空間＋installDir ハッシュ）。`Local\` のままだと共有インストール（Program Files 等、R-7）を複数セッション（RDP/共有端末）から同時操作するケースを防げない点に注意 |
| R-7 | Program Files 配置時の書込み昇格 | 書込可否を事前判定し、不可時のみ self-elevate。ドキュメントで「無人更新は Program Files 以外へ」明記 |
| R-8 | release.yml（self-contained）と build-release.bat（framework-dependent）の方針差 | **決定: 両経路は統一しない。** build-release.bat の配布物（`AIUsageOverlay_release.zip`）は自動アップデートの配布経路の対象外とする。`UpdateCheckService`（F-21）・DL/検証（F-26）が読む唯一の資産源は GitHub Release の `assets[]` であり、それは release.yml が publish する self-contained zip に限定する（release.yml の `--self-contained true` は変更不可の前提として固定）。**build-release.bat が生成する zip を GitHub Release へ手動添付することは禁止**とし、release.yml 冒頭コメントに明記する。build-release.bat 自体の `--self-contained false` は変更しない（配布サイズ縮小方針・`install-runtime.bat` 運用を維持）。F-27 で build-release.bat 側にも updater.exe を同梱するのは、framework-dependent インストールも初回導入経路としては有効なままにするためであり、以後の自動更新は必ず release.yml 産の self-contained ビルドに収束するため「更新後にランタイム不足で起動不能」は発生しない |
| R-9 | AV が自己更新の挙動を誤検知 | 半自動・専用実行体・.bat 回避で軽減。起動成功マーカー未達で自動ロールバック |
| R-10 | 無断の外部通信がプライバシー方針と衝突 | GET のみ・利用データ非送信・宛先固定。`AutoUpdateCheckEnabled` でオプトアウト |
| R-11 | private リポジトリのままだと 404 | ✅ 対応済み（前提条件、public 化完了） |
| R-12 | staging（`%LocalAppData%`）と installDir が別ボリューム配置（別ドライブ、UNC 等）だとリネームがアトミックに成立しない | F-27: 適用開始時にドライブ文字/ルートの一致を事前チェック。不一致時は installDir に触れず中断し手動 DL 導線（F-23）へ誘導。手順3〜4は try/catch で包み、手順4失敗時は `.bak` から即時ロールバック |
| R-13 | release.yml の prerelease 判定（`-beta`/`-rc` のみ）と F-19 の SemVer prerelease 検出（任意の `-` 識別子）の基準が乖離 | F-24: release.yml の判定を `contains(github.ref_name, '-')` に統一し F-19 と同一基準にする。放置すると将来 `-alpha`/`-preview` 等のタグが安定版として公開され誤って更新通知が出る |
| R-14 | AppLocker/WDAC 等の許可リスト型アプリケーション制御が `%TEMP%` からの updater.exe 実行を既定でブロックしうる（R-9 の AV ヒューリスティック誤検知とは別種） | F-27: updater.exe 起動確認（例外捕捉＋グレースピリオド内の生存確認）を本体終了前に行い、未確認時は本体を終了させずエラー案内を表示する。管理端末では IT 部門による許可リスト追加が別途必要 |

---

## 11. ライセンス表記

本改修は本アプリ独自実装であり、自己更新ライブラリ（Velopack/Squirrel/NetSparkle/AutoUpdater.NET）は採用しない。外部コードの移植を含まないため新たな帰属表記は不要。GitHub Releases API は GitHub の公開 API を規約に従い GET で利用する。
