# DHub を自分の PC で動作確認する

## 最短の手順

1. 受け取ったソース ZIP をすべて展開します。ZIP 内から直接実行せず、`Build.cmd` と `packaging`・`src` などが同じフォルダーにある状態にします。Git や clone 操作は不要です。
2. `Build.cmd` をダブルクリックします。管理者としての実行は不要です。古い `Build.bat` / `build.bat` とは別の入口です。
3. 完了した画面の「出力先」を開きます。既定ではデスクトップの `DApps-Preview-Builds` 内に、アプリ名・日時・GUID の新しいフォルダーができます。以前のビルドは上書きしません。
4. 出力先の `はじめに.txt` を開き、`Launch-Preview.cmd` をダブルクリックします。MSI をインストールせず、そのビルドのアプリを試せます。
5. アプリのトレイメニューから終了します。同じ `Launch-Preview.cmd` で再起動すると、同じテストデータを読み込みます。

ビルド中は数分かかることがあります。画面に出るログファイルで経過を確認できます。成功・失敗のどちらでもウィンドウを閉じる前に結果を読めます。

## PC に必要なもの

- Windows 10/11 x64
- [Microsoft 公式 .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。Desktop Runtime だけではビルドできません
- [Microsoft 公式 PowerShell 7.2 以降](https://learn.microsoft.com/powershell/scripting/install/install-powershell-on-windows)。Windows 標準の PowerShell 5.1 とは異なります
- [WiX Toolset 3.14.1](https://github.com/wixtoolset/wix3/releases/tag/wix3141rtm)。`candle.exe` と `light.exe` の FileVersion 文字列が **3.14.1.8722** で、`WixUIExtension.dll`・`WixUtilExtension.dll` が必要です。WiX のネイティブ数値欄は `3.14.8722.0` になるため、版の確認には文字列欄を使います
- NuGet の公式 .NET ランタイム依存ファイルと、ライセンス用 WiX ソース ZIP を取得できるインターネット接続

不足している場合は、公式案内へのリンクとログを残して中止します。ツールを自動インストールしたり、実行ポリシーを変更したり、管理者へ昇格したりしません。PC のポリシーでスクリプト実行が禁止されている場合は、管理者へ確認してください。警告やポリシーを回避する操作は案内しません。

## 試せること

1. 管理画面の「サンプル3件を追加」で実ファイル・フォルダー・公式URLを追加し、固定バーのボタンから試します。起動時に概要が出たら「ブックマーク一覧へ（次回も一覧から）」を選びます。
2. お気に入りの追加・順序変更、トレイからの直接起動を試します。
3. 自分で作ったテスト用 `.lnk` / `.url` のフォルダー連携と解除を試し、元ファイルが残ることを確認します。
4. トレイから終了して再起動し、登録内容・順序が残ることを確認します。同じ起動ファイルを二重起動した場合も確認します。詳しい項目は [VALIDATION_JA.md](VALIDATION_JA.md) を参照してください。

- 保存先は出力先内の `preview-profile` です。通常版や別のプレビューの保存先とは別です。別ビルドを作ると新しい確認用データになります。
- 出力先内の `sample-files` はテスト用です。最初は大切な実データを登録・移動せず、このフォルダーで確認してください。
- `--data-dir` と保存先を 2 引数で渡します。同じ起動ファイルなら再起動後も同じ保存先を使用します
- 直接 `payload\DHub.exe` を開かず、確認用データを指定する `Launch-Preview.cmd` から起動してください。
- 初期画面が表示されない場合は、隠れているタスクトレイアイコンも確認します。ウィンドウの × では常駐が続く場合があります。
- MSI に梱包するものと同じ payload なので、直接起動でも「MSI管理」と表示され、EXE 単体の更新を止めます。`installed-by-msi.json` は残してください。この表示は、この PC に MSI をインストール済みであることの証明ではありません。
- 実行用の .NET は同梱されています。ビルド済み payload の実行に、別途 Desktop Runtime は必要ありません。

## インストーラ画面も確認したい場合

`はじめに.txt` の「MSI」欄に実ファイルのフルパスが書かれます。出力先の `build-<GUID>` の中にも `.msi` があります。自分で開くまで実行されません。

未署名の Preview 用 MSI です。画面だけを確認する場合は、インストールを確定せずキャンセルしてください。導入する場合の保存先・修復・更新・アンインストールの確認項目は [INSTALLER.md](INSTALLER.md) を参照してください。MSI 起動後のアプリは既定のプレビュー用データを使い、`Launch-Preview.cmd` の `preview-profile` とは別になります。

`Launch-Preview.cmd` の動作確認だけでは、MSI の導入・修復・更新・削除・ショートカットを検証したことにはなりません。ビルドの成功も GUI 動作確認の合格を意味しません。

## 出力先の指定・CI

通常のコマンドプロンプト:

```bat
Build.cmd -OutputDirectory "C:\Users\yourname\Documents\DApps Preview"
```

同じ入口を CI で実行する場合は、最初に `--ci` を指定します。ウィンドウのキー待ちを省き、成功は 0、失敗は 0 以外の終了コードをそのまま返します。

```bat
Build.cmd --ci -OutputDirectory "C:\ci\local-builds"
```

既設 WiX の場所を明示する場合:

```bat
Build.cmd --ci -OutputDirectory "C:\ci\local-builds" -WixBin "C:\Program Files (x86)\WiX Toolset v3.14\bin"
```

ライセンス用 WiX ソース ZIP が手元にある場合は `-WixSourceArchive "C:\path\wix3-source.zip"` も指定できます。これはツールのインストーラではありません。正しいソースの検証は `Build-Installer.ps1` が行います。

`-OutputDirectory` は親フォルダーです。指定先が既に存在しても、その下に毎回新しい子フォルダーを作ります。相対パスは呼び出し元の現在のフォルダーを基準にします。ソース・出力先の空白と日本語を含むパスに対応しています。

PowerShell 7 から直接呼ぶ場合も同じ処理です:

```powershell
pwsh -NoProfile -File .\packaging\Build-Local.ps1 -NonInteractive -OutputDirectory 'C:\ci\local-builds'
```

`Build.cmd --ci` は `Build-Local.ps1 -NonInteractive` に渡します。ビルド側の対象は `net10.0-windows`、MSI バージョンは `1.1.2`、アプリは `1.1.2-preview.1` です。CI の受入試験では直接 `Build-Installer.ps1` を呼ぶだけでなく、この `Build.cmd --ci` を使用してください。

## 失敗したとき

- 最初に表示される起動ログ: `%LOCALAPPDATA%\DApps-Preview-Builds\logs\DHub-bootstrap-*.log`。PowerShell 不足やスクリプト読み込み前のエラーも残します
- 出力先内の `build.log`: 事前確認とビルドの詳細。失敗時は `BUILD-FAILED.txt` も作成します。WiX 検出では、確認したフルパス・不足ファイル・FileVersion / ProductVersion 文字列・数値版を記録します
- 出力先内の `local-build.json`: 成功時の MSI・payload・確認用データ・ログのパスと、ビルド情報
- `build-<GUID>\build-manifest.json`: SDK / WiX / ランタイム版と MSI の SHA-256

ログに出る不足ツールや通信エラーを確認し、解消後に `Build.cmd` をもう一度実行してください。失敗したフォルダーを再利用・削除せず、新しい出力先を作ります。セキュリティ警告が出たときは、実行を止めて出所を確認してください。

## 1.1.1-preview.1: 固定バーの確認

新しいZIPを別フォルダーへ展開して `Build.cmd` を実行し、今回表示された新しい生成先の `Launch-Preview.cmd` を開いてください。以前の生成先は自動更新されません。版表示が 1.1.1 であることを確認します。

管理画面の「固定バーを表示」から実際のブックマークボタン列を開きます。一覧の項目を選んで「固定バーに追加」、またはバーへファイル・フォルダー・URLをドロップしてください。既存の登録を勝手に固定化しません。ボタン1回で起動、右クリックの左右移動、ドラッグ用つまみで位置変更、終了・再起動で順序と位置の維持を確認します。長い同名項目は先頭識別子とツールチップで区別してください。詳しくは `PINNED_BAR.md`。

Previewのデータ分離は継続しています。旧版のブックマークは自動移行しません。フォルダー連携は管理画面からも開けますが、連携項目と通常登録の統合検索は今回の修正には含まれません。

## 1.1.2-preview.1: サンプルと一覧起動

今回のZIPを新しいフォルダーに展開して `Build.cmd` から作り直し、新しい生成先の `Launch-Preview.cmd` を開いてください。表示版は 1.1.2 です。過去の生成先は更新されません。

- 新規profileはブックマーク一覧を表示します。旧profileの概要表示設定は維持するので、概要先頭の青い「ブックマーク一覧へ（次回も一覧から）」で切り替えてください。
- 「サンプル3件を追加」で、使い方メモ・作業フォルダー・Microsoft公式Windowsヘルプを追加します。固定バーが表示されたら、自分でボタンをクリックして開けます。
- 追加を繰り返しても重複せず、既存登録や編集した内容を置き換えません。削除は一覧で対象を選び「削除」です。元ファイル・フォルダーは残ります。
- サンプルの実体は利用中profileの `Samples\GettingStarted-v1` です。ビルド出力にある旧 `sample-files` は別の手動確認用fixtureです。
- サンプル追加では、保存済みの固定バー表示・位置設定を変えません。次回もバーを表示したい場合は「固定バーを表示」を選んでください。

詳しい安全仕様、保存失敗時の扱いと受入チェックは [SAMPLES.md](SAMPLES.md) を参照してください。
