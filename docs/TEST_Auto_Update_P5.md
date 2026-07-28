# 自動アップデートP5 v2.0.1リリース検証

| 項目 | 内容 |
|------|------|
| 対象機能 | P5（F-18〜F-24: 更新検知・通知・手動ダウンロード導線） |
| 旧版 | v2.0.0 |
| 新版 | v2.0.1 |
| 実施日 | 2026-07-28 |
| 事前検証 | 完了 |
| 公開Release検証 | 未実施（v2.0.1のGitHub Release公開後に実施） |

## 1. 事前検証結果

| # | 確認項目 | 結果 | 証拠 |
|---|----------|------|------|
| 1 | 開発用の既定バージョンがv2.0.1か | 合格 | `AIUsageOverlay.csproj`の`<Version>`は`2.0.1` |
| 2 | Release構成でビルドできるか | 合格 | `dotnet build AIUsageOverlay/AIUsageOverlay.csproj -c Release`が警告0・エラー0 |
| 3 | 既定バージョンがAssembly情報へ入るか | 合格 | 生成された`AIUsageOverlay.AssemblyInfo.cs`の`AssemblyInformationalVersion`は`2.0.1+<commit>`、`AssemblyFileVersion`は`2.0.1.0` |
| 4 | 公式Releaseビルドがタグ版数を優先する構成か | 静的確認済み | `release.yml`は`v`を除いたタグを検証し、`dotnet publish`へ`-p:Version=${{ steps.version.outputs.version }}`を渡す。実動作は公開後に確認する |
| 5 | 同一バージョンを更新ありと判定しない実装か | 静的確認済み | `UpdateCheckService`は`LatestVersion > CurrentVersion`の場合だけ更新情報を返す。公開成果物での確認は未実施 |

## 2. GitHub Release公開後の実機検証

以下は`v2.0.1`タグをGitHubへpushし、GitHub ActionsのRelease作成が完了してから実施する。

| # | 手順 | 期待結果 | 結果 |
|---|------|----------|------|
| 1 | GitHub Release v2.0.1を開く | `AIUsageOverlay_v2.0.1.zip`と`checksums.txt`が添付されている | 未実施 |
| 2 | zipのSHA256を`checksums.txt`と照合する | SHA256が一致する | 未実施 |
| 3 | GitHub Actionsの「Publish executable」ログを確認する | `-p:Version=2.0.1`でpublishされている | 未実施 |
| 4 | Releases APIのv2.0.1レスポンスを確認する | `tag_name`、`html_url`、zipのURL・名前・サイズを取得できる | 未実施 |
| 5 | 公式Releaseのv2.0.0を起動し、「今すぐ確認」を押す | v2.0.1を検知し、更新ありと表示する | 未実施 |
| 6 | v2.0.0の通知またはトレイメニューをクリックする | v2.0.1のGitHub Releaseページが開く | 未実施 |
| 7 | v2.0.1のzipを展開し、同梱DLLを維持したまま起動する | 設定画面に現在のバージョンv2.0.1と表示する | 未実施 |
| 8 | v2.0.1で「今すぐ確認」を押す | 「現在のバージョンが最新です。」と表示し、更新通知を繰り返さない | 未実施 |
| 9 | ネットワークを切断して「今すぐ確認」を押す | アプリが終了せず、更新確認の失敗理由を表示する | 未実施 |
| 10 | 自動確認ON・前回確認時刻なしでアプリを起動する | 起動約30秒後に1回だけGitHub Releasesを確認する | 未実施 |
| 11 | 自動確認ON・24時間以内の前回確認時刻で再起動する | 24時間ゲートにより自動GETを行わない | 未実施 |
| 12 | 自動確認をOFFにしてアプリを再起動する | 起動時・定期タイマーの自動GETは行わない | 未実施 |
| 13 | 自動確認OFFのまま「今すぐ確認」を押す | 手動確認は実行でき、最新と表示する | 未実施 |
| 14 | `v2.0.0`、`v2.0.1`、`v2.1.0-rc.1`、`v1.40`をSemVer検証する | 3成分SemVerを正しく比較し、2成分タグを拒否する | 未実施 |

## 3. 判定

ビルドとローカルAssembly情報の事前検証は合格。P5のリリース検証完了判定は、
上記の公開Release検証をすべて実施してから行う。未実施項目を静的なコード確認だけで合格扱いしない。
P5は更新ファイルの自動ダウンロード・自動適用を行わず、通知とGitHub Releaseページへの導線のみを提供する。
