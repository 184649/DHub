# AIMANGA Virtual Office — 同一ドメイン /manga/ 版

## 正式なURL

- **最終URL（既存のDawnサイト配下）:** `https://dawn-app.jp/manga/`
- **既存サイト:** `https://dawn-app.jp/` と `https://dawn-app.jp/home` は変更しない
- **GitHub Pagesで公開する場合のプレビュー（Pages有効化後）:** `https://184649.github.io/DHub/manga/`
- **採用しない構成:** `manga.dawn-app.jp`（サブドメイン）、`dawn-app.jp/home/office/`（旧案）

**2026-10-26:** URLとGitHub上の静的ファイルを準備。実際の `dawn-app.jp/manga/` への反映・動作確認は、既存VPSのNginx設定と静的ファイルを変更できる管理権限を持つ環境でのデプロイ後に行う。既存サイトに手を加えたと偽らない。

## GitHub上のファイル

- `manga/index.html` — 完成した静的Webアプリ。相対パスで配信
- `manga/assets/office-art.webp` — アプリのオフィス画像
- `manga/icon.svg`、`manga/manifest.webmanifest` — アイコン・ホーム画面追加用設定
- `deploy/conoha-nginx-location.conf` — 既存 `dawn-app.jp` の**既存 HTTPS server ブロック**への非破壊的な追記例
- `deploy/README.md` — 本文書

`gh-pages` 専用ブランチ。既存DHubの `main`、Dawnリポジトリ、`dawn-app.jp/home` アプリには変更しない。以前の作業で追加した `gh-pages/home/office/` は廃止する。GitHub Pagesの既存 `/office/` ミラーは旧バージョンのブックマーク用に残せるが、正式な新規配信ルートは `/manga/` とする。

## 既存のdawn-app.jpへのデプロイ例（VPS / Nginx を想定）

**サーバーの実際の運営方式・設定は管理環境で再確認すること。** VPS/Nginx以外ならホスティング方式に合わせ、同じ `/manga/` ルートを作る。DNSや証明書は変更しない。

1. 既存の `dawn-app.jp` 設定とコンテンツをバックアップし、`/home`・既存API・リバースプロキシ・SSL設定を確認する。
2. リポジトリの `gh-pages/manga/` **内の4点**をサーバーの専用フォルダ `/srv/aimanga-office/current/` へコピーする。例：`index.html`、`assets/`、`manifest.webmanifest`、`icon.svg`。他アプリのフォルダには触れない。
3. `deploy/conoha-nginx-location.conf` を確認して、**dawn-app.jpの既存 HTTPS server ブロック内**に `location = /manga` と `location ^~ /manga/` を追記する。新しい `server_name`、`CNAME`、`root` ブロックは作らない。
4. 設定例は `auth_basic` を有効化してある。サーバー側で `/etc/nginx/.aimanga-office-htpasswd` を安全に作成するか、既存認証基盤と統合してから読み込む。**認証パスワードとハッシュはGitHubへ保存しない**。
5. `nginx -t` で構文確認。既存の `/`、`/home`、APIが変わらないことを検証してからNginxをリロードする。
6. ブラウザ・iPhoneで `https://dawn-app.jp/manga/` と `https://dawn-app.jp/manga/manifest.webmanifest`、画像読み込み、8部署タップ、モバイル表示、Notionリンクが正常であることを確認する。
7. 問題があれば、追加した2個の `location` 設定を取り消してリロードする。既存のDawnホームに影響させない。

サーバー側にNginx変更権限が無ければ、**GitHub側のファイル配置のみでは `dawn-app.jp/manga/` は開通しない**。DNSはパスの振分けができないため、`/manga/` というルートは既存のWebサーバー側に設定が必要。

## GitHub Pages（必要な場合のみ）

`184649/DHub` → Settings → Pages → Source「Deploy from a branch」 → Branch「gh-pages」、Folder「/(root)」。**他のサイトがすでにPagesを使っている場合は、設定を上書きしない**。GitHub Pagesは公開サイトであるため、未公開原稿・PDF・APIキー・Notion内部ログは載せない。Pagesはオフィスの見本を直接見るための配信先であり、`dawn-app.jp`の代わりにDNSを付け替えない。

## 重要な安全・運用上の制限

- 配信するHTMLには架空の社員名、部署、公開用イメージ、デモ的な状態表示のみを置く。**本当の稼働中・終了・障害を自動同期した情報ではない**。
- Notionリンクを入力する場合は、利用者のブラウザ内のみに保存。トークンや秘密URLはGitHub/公開HTMLへ書き込まない。
- 完成1話PDFの正本と採用・出版決裁は**Notionに残し、CEO本人が判断する**。GitHubを漫画原稿の正本としない。
- Codex/ChatGPT Work、有料API、追加費用を発生させない。認証回避・アクセス制限の撤廃は行わない。
