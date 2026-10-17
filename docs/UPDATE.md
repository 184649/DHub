# DHub 更新仕様と正式配布の注意

このブランチは preview です。通常版への自動更新は拒否し、起動・検証は README のソースビルド手順を使用します。以下は正式配布を別途承認・検証した後の仕様であり、この変更で release を公開したものではありません。

## 更新の流れ

通常版は `https://raw.githubusercontent.com/184649/DHub/main/version.json` を確認します。起動時の確認は通知のみです。手動操作で利用者が更新を選択した場合にダウンロードします。

1. 公式 repository の HTTPS URL と必須 SHA-256 を確認し、サイズ制限付きで app / updater の ZIP を取得します。旧 Chairman-bits/DHub URL も互換先として許可します。
2. ZIP の traversal、link、重複 entry、展開サイズなどを検査します。想定した exe-only の self-contained package を要求します。
3. Updater が検証済み app を staging し、flush 済みの readiness 合図を原子的に公開します。ここまで失敗した場合、DHub は閉じません。
4. DHub が proceed 合図を渡して終了します。Updater は合図と一致する PID の終了を待ちます。timeout・cancel・不正な合図では置換せず、process を強制 kill しません。
5. backup を保持して置換し、失敗時は rollback を試みます。再起動に `--data-dir` を引き継ぎます。

SHA-256 は package の完全性を検査するもので、Authenticode の発行者署名ではありません。配布元自体の侵害を防ぐ保証はありません。新しい updater は hash のない旧 manifest を安全側に拒否します。

## 正式配布を作る場合

`Build.bat` は明示した正式 version で project を更新し、win-x64 self-contained single-file の app / updater を publish して ZIP と hash 付き manifest を生成します。既存 `release` フォルダーを作り直すため、必要な旧配布物は先に保管してください。入力は `1.1.0` のような数値 3 要素を使用します。preview のまま配布しないでください。

生成された `DHub.zip`、`DHubUpdater.zip`、`version.json`、`release-notes.json` を一組として検証します。manifest は次の項目を持ちます。

```json
{
  "version": "1.1.0",
  "downloadUrl": "https://raw.githubusercontent.com/184649/DHub/main/DHub.zip",
  "updaterUrl": "https://raw.githubusercontent.com/184649/DHub/main/DHubUpdater.zip",
  "downloadSha256": "<DHub.zip の SHA-256 64 桁>",
  "updaterSha256": "<DHubUpdater.zip の SHA-256 64 桁>",
  "releaseNotes": "https://raw.githubusercontent.com/184649/DHub/main/release-notes.json"
}
```

公開前に別の Windows 検証用 profile で旧版→新版の実際の置換・再起動、起動失敗、書込み不能、rollback、データ保持を確認してください。この実機 E2E はまだ実施していません。main の書換えや release 公開はこのブランチ検証に含みません。

## データと一時ファイル

通常版の既定保存先は `%LOCALAPPDATA%\DHub`、preview は `%LOCALAPPDATA%\DHub-Preview` です。`--data-dir` 指定時はそちらを維持します。更新器の診断もその profile に保存します。

Updater 自身が実行中のため、ダウンロード用の外側の一時フォルダーは即時削除しません。Updater が所有する展開・staging ファイルは終了時に整理します。backup や失敗診断は回復時に確認してください。
