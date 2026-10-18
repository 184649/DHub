# DHub

Windows 向けのファイル・フォルダー・URL ランチャーです。このブランチは **1.1.3-preview.1**。既存登録の直接起動、フォルダー参照、保存回復と安全な更新処理を改善しています。

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
- 保存先の指定は `--data-dir <フォルダー>` です。`--show` または `--settings` と併用できます。再起動時も同じ保存先を指定してください。
- 既存の `DHub.zip` / `release/` はこのソース変更から作った配布物ではありません。プレビューは上記のソースからビルドしてください。
- プレビューから通常版への自動更新は停止しています。通常版の exe だけでプレビューの DLL を上書きしないためです。`Build.bat` は正式配布用で、この検証手順では実行しません。

## 日常の入口（1.1.3-preview.1）

通常起動では画面・固定バー・タスクバーのボタンを表示せず、通知領域のトレイアイコンだけで待機します。

- **Ctrl+Shift+Space**: ブックマーク一覧を呼び出す。入力で検索し、Enterまたはダブルクリックで開く
- 選択後・Esc・別の画面への切替で一覧を隠す
- トレイの左クリック: お気に入り・一覧検索・管理・設定・終了
- **Ctrl+Shift+D**: 明示的に管理画面を開く
- 一覧下部の「キー設定」: キー変更・再登録・無効化・終了。キーは保存後すぐ反映
- DHubをもう一度実行すると、同じ保存先・Windowsユーザー・セッションで起動中の一覧を表示
- `DHub.exe --settings` はキー設定の復帰入口。`--data-dir` を使っている場合は同じ保存先も指定

キーが他のアプリと重なるなどで登録できないときは、復帰画面を表示してトレイからも操作できます。候補は Ctrl+Shift+Space / Ctrl+Alt+Space / Ctrl+Alt+D。保存データが読み取り専用でも「今回だけ適用 / 再試行」は使えます。

以前の固定バー表示・位置・概要の起動設定は記録を残しますが、この版では表示に使いません。お気に入り・登録順・起動方法・保存先は引き継ぎます。Windowsのタスクバーのピンや自動起動設定は変更しません。

## まずサンプルで試す

Ctrl+Shift+D またはトレイの「管理画面を開く」で管理画面を開き、**「サンプル3件を追加」** を選びます。使い方メモ（実テキスト）、作業フォルダー（実フォルダー）、Microsoft公式Windowsヘルプ（URL）を追加できます。確認後に新規サンプルだけをお気に入りにします。ファイルやURLは自分で開くまで起動しません。

既存登録を置き換えず、再追加で重複しません。不要な登録は一覧から削除できます。実ファイルや自分で書き加えた内容は残ります。

「フォルダー連携」で既存のショートカットフォルダーを登録できます。元の `.lnk` / `.url` はそのまま起動します。非表示・連携解除は元ファイルを削除しません。

詳しくは [非表示ランチャーの確認・復帰・終了](docs/HIDDEN_LAUNCHER.md)、ZIPからのビルドは [自分のPCで動作確認](docs/LOCAL_CHECK.md) を参照してください。

保存の不整合・破損を検出すると、空データで上書きせず編集を止めます。回復画面の差分を確認し、使用する候補を選んでください。再読込前に必要な未保存変更をエクスポートしてください。起動後の履歴保存失敗は起動失敗とは別に通知し、履歴の再保存ではアプリを再起動しません。

## 検証

```powershell
dotnet run --project tests/StorageRegression/StorageRegression.csproj -c Release
dotnet run --project tests/LauncherRegression/LauncherRegression.csproj -c Release
dotnet run --project tests/UpdaterRegression/UpdaterRegression.csproj -c Release
dotnet run --project tests/HiddenLauncherRegression/HiddenLauncherRegression.csproj -c Release
```

実ソースを使う回帰テストは保存・サンプル・設定維持 116 件、ランチャー 55 件です。更新器はLinux確認で59件合格・Windows専用5件スキップです。Windows ビルドと同じ 4 スイートを実行する [ブランチ限定 CI](https://github.com/184649/DHub/actions?query=branch%3Acodex%2Fdhub-reliability-launcher-20261017) もあります。GUI 操作・実 Shell 起動・DPI/複数画面は自動テストの合格に含めません。

- [起動後の確認・制限・回復手順](docs/VALIDATION_JA.md)
- [全改善項目の対応台帳](docs/IMPROVEMENT_LEDGER_JA.md)
- [正式配布時の更新手順](docs/UPDATE.md)

## ローカル確認用インストーラ

.NET 10 を同梱するユーザー単位のMSIを生成します。インストール先は通常profileと別の `%LOCALAPPDATA%\Programs\DHub-Preview` です。新規導入、同版の修復、上位版への更新、データを残すアンインストールに対応する構成です。旧portable版のデータは自動移動しません。

- [MSIの作成・試用・更新と制約](docs/INSTALLER.md)
- [既存profileからの任意の非破壊コピー](docs/PROFILE_IMPORT.md)

インストーラは署名していません。Windowsのセキュリティ警告を回避する設定変更は案内しません。一般公開・自動更新配布はこの検証には含めません。ソースからの生成には既設.NET10 SDK、PowerShell7、WiX3.14.1が必要です。ビルドscriptは不足toolを自動インストールしません。実行するアプリには.NETの別途導入は不要です。
