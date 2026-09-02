# Git 運用ルール（AI Usage Overlay）

このファイルは `.claude/git-rules.json` から生成された派生物。ルールを変更する場合は
`.claude/git-rules.json` を直してから `git-rules-keeper` スキルの再生成手順を実行する。

## 1. リモート構成

| remote | 役割 | URL |
| --- | --- | --- |
| `backlog` | 社内（コード可） | `https://miwalock.backlog.com/git/TOOL/Claude-UsageTool.git` |
| `github` | 公開 | `https://github.com/yhashimoto000/AIUsageOverlay.git` |

初回のみ登録（既に登録済みなら不要）:

```
git remote add backlog https://miwalock.backlog.com/git/TOOL/Claude-UsageTool.git
git remote add github https://github.com/yhashimoto000/AIUsageOverlay.git
git remote -v
```

## 2. ブランチ構成

```
wip   ローカル限定・push しない・1 本を使い回す
   |  --squash（1 サブタスク = 1 コミットに圧縮／refs/wip-archive/<トピック> へ退避）
feat/*  fix/*  docs/*  refactor/*  perf/*  build/*  chore/*   種別ブランチ・push する・削除しない
   |  --no-ff（マージ単位で履歴を残す）
master   統合ブランチ・push する
```

| ブランチ | 役割 | push 先 | 親へのマージ |
| --- | --- | --- | --- |
| `wip` | 作業。細かく commit（1 本を使い回す） | push しない | `--squash` |
| 種別 `*/` | トピック単位のまとめ（削除しない） | `backlog` / `github` | `--no-ff` |
| `master` | 統合 | `backlog` / `github` | - |

### 種別ブランチ

命名は `<種別>/<トピック>`（英小文字・ハイフン区切り・日本語と空白は禁止）。

| 接頭辞 | 用途 |
| --- | --- |
| `feat/` | 機能追加 |
| `fix/` | 不具合修正 |
| `docs/` | ドキュメント・設計書 |
| `refactor/` | 挙動を変えない整理 |
| `perf/` | 性能改善 |
| `build/` | ビルド・配布まわり |
| `chore/` | 雑務・バージョン更新 |

### 作業ブランチ

`wip`。種別ブランチから派生し、細かくコミットしてよい。
push しない。ブランチは `wip` の 1 本だけを使い回す。種別ブランチへ取り込んだ直後に `git update-ref refs/wip-archive/<トピック> wip` で細かい履歴を退避し、`git switch -C wip <種別ブランチ>` で作り直す。退避先は `refs/heads/` の外なので `git branch` に出ず、通常の push でも飛ばず、`git gc` でも回収されない。読むときは `git log refs/wip-archive/<トピック>`、一覧は `git for-each-ref refs/wip-archive/`。

## 3. 運用フロー

1. **種別ブランチを作る** — `master` を最新化してから分岐する。

   ```
   git switch master
   git pull --ff-only backlog master
   git switch -c feat/<トピック>
   git push -u backlog feat/<トピック>
   git push -u github feat/<トピック>
   ```

2. **作業ブランチを用意する** — 作業ブランチは `wip` の 1 本だけを使い回す。トピックごとに新しい名前を作らない（ブランチ本数が作業量に比例して増えるのを避けるため）。`-C` は「あれば作り直す」の意味。

   ```
   git switch -C wip feat/<トピック>
   # 細かく commit してよい。push はしない
   ```

3. **種別ブランチへまとめる** — `--squash` で 1 コミットに圧縮する。`--squash` は内容を取り込むだけでコミットまではしないので `git commit` が別途必要。**このとき `-m` を付けない**。`--squash` は取り込んだコミットのログを `.git/SQUASH_MSG` に書き出し、`-m` なしの `git commit` はそれをエディタの初期内容として開く。先頭（＝最新の代表コミット）の 1 行だけ残して他を消せば、作業ブランチ側と一字一句同じメッセージになる。`-m` を付けると `SQUASH_MSG` は無視され、文言がずれる。

   ```
   git switch feat/<トピック>
   git merge --squash wip
   git commit          # -m は付けない。SQUASH_MSG の先頭 1 行だけ残す
   git push backlog feat/<トピック>
   git push github feat/<トピック>
   git update-ref refs/wip-archive/<トピック> wip   # 細かい履歴を退避
   git switch -C wip feat/<トピック>   # 作り直して次のサブタスクへ
   ```

   退避先の `refs/wip-archive/` は `refs/heads/` の外なので `git branch` に出ず、通常の push でも飛ばず、`git gc` でも回収されない。読むときは `git log refs/wip-archive/<トピック>`、一覧は `git for-each-ref refs/wip-archive/`。

4. **統合ブランチへ入れる**

   ```
   git switch master
   git pull --ff-only backlog master
   git merge --no-ff feat/<トピック>
   git push backlog master
   git push github master
   ```

コミットメッセージ: `<種別>(<範囲>): <要約>（日本語・Conventional Commits 風。例: fix(viewmodel): GitHub Copilot表示のちらつきを修正）`

`master` 上で `git log --first-parent --oneline` を打つと、トピック単位のマージが 1 行ずつ並ぶ。変更履歴の作成やレビュー範囲の把握はこの一覧で行う。種別ブランチ側は 1 コミットに圧縮されているため作業中の細かい経緯は載らないが、経緯は `refs/wip-archive/<トピック>` に退避してある。`git log refs/wip-archive/<トピック>` で読み、`git for-each-ref refs/wip-archive/` で一覧できる。

## 4. 公開範囲（ソース公開）

`github` にはコードもドキュメントも push する。
公開前に、社内 URL・認証情報・顧客名・社内限ドキュメントが含まれていないことを確認する。

## 5. 禁止事項

- `master` / 種別ブランチへの直接コミット（必ず 1 段下からマージする）
- `wip` のリモート push（ローカル限定）
- `git push --all`（push しない約束の作業ブランチまで送られる）
- 退避 ref（`refs/wip-archive/*`）の削除（squash 前の内訳が復元できなくなる）
- `git push --mirror`（`refs/wip-archive/*` まで送られ、隠したはずの試行錯誤が公開される）
- `push --force` / `reset --hard` / `clean -fd` などの破壊的コマンド
- ビルド成果物・生成物のコミット（`.gitignore` 準拠）

## 6. 申し送り

- 手順を補助する PowerShell ヘルパーを scripts/git-flow.ps1 に置いている（status / start / wip / squash / integrate / archives / show）。使い方は README の「開発フロー」。ルールを変えたらスクリプト側も見直す。スクリプトは破壊的コマンド（push --force / reset --hard / clean -fd / push --all / push --mirror）を実行しない。
- release ブランチは作らない。master を統合兼公開確定とし、master にタグを打つ。
- タグは厳密な 3 成分 SemVer（v2.1.0）でのみ打つ。release.yml が正規表現で検査しており、v1.40 のような 2 成分は弾かれる（v1.40 / v1.50 / v1.60 は旧表記の残骸）。
- タグを github へ push すると release.yml が走り、self-contained zip と checksums.txt を添付した Release が draft:false で即時公開される。push から完了まで約 2 分。
- リリース手順: csproj の <Version> 更新 → 種別ブランチを master へ --no-ff マージ → 注釈付きタグ → master とタグを両リモートへ push。マージコミットの件名は「merge: <内容>（vX.Y.Z）」に揃える。
- master とタグは backlog / github の両方へ push する。片方だけに入れない（過去の v2.0.1〜v2.1.0 は両方に存在する）。
- 自動アップデートは GitHub Releases の /releases/latest を見る。github へタグを push しないと既存利用者へ新版が届かない。
- ブランチ接頭辞は feat / fix を正とする。過去の feature/* bugfix/* feat/* は旧表記であり、遡って改名しない。
- 種別ブランチは削除しない方針を選択している。マージ済みの検出は git branch --merged master で行える。
- 配布用 exe の動作確認は publish\ ではなく bin\Release\ の exe を使う。publish 先の exe を起動したままだと、次の publish が GenerateBundle の UnauthorizedAccessException で失敗する。
- public-source の確認（2026-09-02 実施）: 社内 URL・Backlog の URL・メールアドレスは公開ファイルに含まれていない。docs/SPEC_Usage_Dashboard.md に社内プロジェクト名 Y_SmartApp が 3 箇所あり（cwd 正規化の実例）、公開済み。今後の設計書でも社内固有名を実例に使うときは公開可否を判断する。
- 生成される使用量レポート（%AppData%\AIUsageOverlay\reports）はコミットしない。利用者の履歴由来の情報を含む。

