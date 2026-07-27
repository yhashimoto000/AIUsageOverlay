## 2026-07-27
### Facts（事実）
- 自動アップデート P5（F-18〜F-24）は `feature/auto-update` で実装され、Release ビルドはタグ由来の SemVer を exe・zip 名へ注入する。
- 2026-07-27 時点の GitHub Releases API `/releases/latest` は旧形式タグ `v1.60` を返すため、3成分 SemVer の初回リリースまでは更新候補として解析されない。
- 自動更新用 Release には self-contained zip と、その zip の SHA256 を記載した `checksums.txt` を添付する。
### Policy（方針）
- 自動アップデートが参照する資産は `release.yml` 産の self-contained zip に限定し、`build-release.bat` 産の framework-dependent zip は GitHub Release へ手動添付しない。
- 自動確認は既定 ON とし、起動30秒後・6時間タイマー・24時間ゲートで GitHub の公開メタデータだけを GET する。利用データ・認証情報・テレメトリは送信しない。
- P6（ダウンロード・自己差し替え・updater.exe）は P5 の運用実績確認と人間の明示承認を得てから着手する。
