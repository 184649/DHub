DHub / ShortcutList
===================

変更内容
--------
- 一覧画面の「お気に入り」列から直接登録/解除できるようにしました。
  - セル内の「☆ 登録」「★ 解除」をクリックするだけで保存されます。
  - 右クリックメニュー、上部のお気に入りボタンからも操作できます。
- 登録/編集画面で、開くアプリを「よく使うアプリから選択」できるようにしました。
  - フォルダは explorer.exe を初期値にします。
  - VS Code / Cursor / Visual Studio / ブラウザ / Office / ターミナル系などを候補表示します。
  - Windows の App Paths レジストリから取得できるインストール済みアプリも候補に加えます。
- 開くアプリ候補画面を改善しました。
  - 検索欄に加えてカテゴリ絞り込みを追加しました。
  - 対象種別や拡張子に応じて、使いそうなアプリを上位に表示します。
  - 候補にない場合のみ「直接参照」で exe を選択できます。
- 既存の shortcuts.json と settings.json との互換性を保つため、追加項目は未設定でも動作します。

使い方
------
1. 一覧画面で「☆ 登録」をクリックするとお気に入りになります。
2. お気に入り済みの行では「★ 解除」をクリックすると解除されます。
3. 追加/編集画面の「よく使うアプリから選択」でアプリを選ぶと、開くアプリ欄に反映されます。
4. より細かく探したい場合は「候補から選択」を押し、検索欄やカテゴリで絞り込んでください。

ビルド
------
- Visual Studio または同梱のバッチから通常通りビルドしてください。
- このZIPはソース一式、ソリューション、プロジェクト、アイコン、ビルド用バッチを含みます。

v1.0.3 additional stability/management features:
- Backup restore wizard from the sidebar/settings menu.
- Bulk edit for selected shortcuts (group, tags, favorite, open application).
- File/application icon display in the shortcut list.
- Independent settings screen for general, backup/recovery, and import/export settings.
- settings schemaVersion management.
- Safe save: write to temporary file, validate JSON, create .bak, then replace.
- Startup recovery: if shortcuts.json is broken, DHub attempts to recover from .bak or Backups/*.json.

追加機能メモ（全作業の起点化）
- ホーム画面・今日の作業ダッシュボード
- 統合検索 / コマンドパレット
- コマンドショートカット管理
- ショートカット・コマンドのメモ/作業ノート
- 起動ログ・操作ログ
- 設定・データの分離保存（shortcuts.data.json / workspaces.data.json / commands.data.json / logs.data.json / settings.data.json）
- 統合検索対象: ショートカット、ワークスペース、コマンド、操作、メモ、ログ
