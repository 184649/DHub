# DHub

Windows 向けのファイル・フォルダー・URL ランチャーです。このブランチは **1.1.0-preview.1**。既存登録の直接起動、フォルダー参照、保存回復と安全な更新処理を改善しています。

## プレビューを起動する

Windows 10/11 と [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) が必要です。GUI アプリは Linux では起動できません。

PowerShell で実行します。

```powershell
git clone --branch codex/dhub-reliability-launcher-20261017 https://github.com/184649/DHub.git DHub-preview
cd DHub-preview
# リポジトリに残る過去のビルド生成物だけを除き、新しいソースからビルドします。
Get-ChildItem src -Directory -Recurse | Where-Object { $_.Name -in @('bin', 'obj') } | Sort-Object FullName -Descending | Remove-Item -Recurse -Force
dotnet build DHub.sln -c Release --no-incremental
dotnet run --project src/ShortcutList/ShortcutList.csproj -c Release -- --data-dir "$env:TEMP\DHub-Preview-QA"
```

- 最初は空の検証用フォルダーで試してください。既存データを試す場合も、元の保存フォルダー一式を別の検証用フォルダーへコピーして指定します。
- `--data-dir` 省略時のプレビュー保存先は `%LOCALAPPDATA%\DHub-Preview`。通常版の `%LOCALAPPDATA%\DHub` と分けています。
- 保存先の指定は `--data-dir <フォルダー>` の 2 引数です。再起動時も同じ保存先を指定してください。
- 既存の `DHub.zip` / `release/` はこのソース変更から作った配布物ではありません。プレビューは上記のソースからビルドしてください。
- プレビューから通常版への自動更新は停止しています。通常版の exe だけでプレビューの DLL を上書きしないためです。`Build.bat` は正式配布用で、この検証手順では実行しません。

## 日常の入口

トレイアイコンの左クリックで、お気に入りを直接選べます。固定順は起動しても変わりません。多い場合は「全件」へ進みます。右クリックや管理メニューから従来の管理画面を開けます。

「フォルダー連携」で既存のショートカットフォルダーを登録できます。元の `.lnk` / `.url` はそのまま起動します。非表示・連携解除は元ファイルを削除しません。独立ランチャーバーは必要な場合だけ有効にできます。

保存の不整合・破損を検出すると、空データで上書きせず編集を止めます。回復画面の差分を確認し、使用する候補を選んでください。再読込前に必要な未保存変更をエクスポートしてください。起動後の履歴保存失敗は起動失敗とは別に通知し、履歴の再保存ではアプリを再起動しません。

## 検証

```powershell
dotnet run --project tests/StorageRegression/StorageRegression.csproj -c Release
dotnet run --project tests/LauncherRegression/LauncherRegression.csproj -c Release
dotnet run --project tests/UpdaterRegression/UpdaterRegression.csproj -c Release
```

実ソースを使う回帰テストは保存 85 件、ランチャー 24 件、更新器 50 件です。Windows ビルドと同じ 3 スイートを実行する [ブランチ限定 CI](https://github.com/184649/DHub/actions?query=branch%3Acodex%2Fdhub-reliability-launcher-20261017) もあります。GUI 操作・実 Shell 起動・DPI/複数画面は自動テストの合格に含めません。

- [起動後の確認・制限・回復手順](docs/VALIDATION_JA.md)
- [全改善項目の対応台帳](docs/IMPROVEMENT_LEDGER_JA.md)
- [正式配布時の更新手順](docs/UPDATE.md)

## ローカル確認用インストーラ

.NET 10 を同梱するユーザー単位のMSIを生成します。インストール先は通常profileと別の `%LOCALAPPDATA%\Programs\DHub-Preview` です。新規導入、同版の修復、上位版への更新、データを残すアンインストールに対応する構成です。旧portable版のデータは自動移動しません。

- [MSIの作成・試用・更新と制約](docs/INSTALLER.md)
- [既存profileからの任意の非破壊コピー](docs/PROFILE_IMPORT.md)

インストーラは署名していません。Windowsのセキュリティ警告を回避する設定変更は案内しません。一般公開・自動更新配布はこの検証には含めません。ソースからの生成には既設.NET10 SDK、PowerShell7、WiX3.14.1が必要です。ビルドscriptは不足toolを自動インストールしません。実行するアプリには.NETの別途導入は不要です。
