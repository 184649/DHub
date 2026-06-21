# DHub 自動アップデート仕様

## 参照先

DHub は起動時に以下の main ブランチ上の `version.json` を確認します。

```text
https://raw.githubusercontent.com/Chairman-bits/DHub/main/version.json
```

`version.json` の `version` が現在のアプリより新しい場合、以下を自動で実行します。

1. `downloadUrl` から `DHub.zip` をダウンロード
2. `updaterUrl` から `DHubUpdater.zip` をダウンロード
3. 一時フォルダに `DHubUpdater.exe` を展開
4. DHub 本体を終了
5. Updater が `DHub.exe` を上書き
6. 更新後の `DHub.exe` を再起動

## 保存データの保持

ショートカット一覧と設定は、アプリ配置フォルダではなく以下に保存します。

```text
%LOCALAPPDATA%\DHub\shortcuts.json
```

そのため、GitHub から取得した `DHub.zip` でアプリ本体を更新しても、登録済みショートカットや表示設定は維持されます。

## main ブランチに配置するファイル

`Build.bat` 実行後に生成される以下を GitHub の main ブランチ直下へ配置してください。

```text
DHub.zip
DHubUpdater.zip
version.json
release-notes.json
```

## version.json 例

```json
{
  "version": "1.0.1",
  "downloadUrl": "https://raw.githubusercontent.com/Chairman-bits/DHub/main/DHub.zip",
  "updaterUrl": "https://raw.githubusercontent.com/Chairman-bits/DHub/main/DHubUpdater.zip",
  "releaseNotes": "https://raw.githubusercontent.com/Chairman-bits/DHub/main/release-notes.json"
}
```
