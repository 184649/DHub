# AIMANGA Virtual Office — GitHub Pages / dawn-app.jp 対応準備

## 制作物の扱い

- このフォルダには**公開してもよい固定の仮想社員名・部屋・業務概要だけ**を収録します。原稿・非公開企画・Notion内部ログ・PDF・トークン・個人情報は含みません。
- 公開GitHub Pagesはアクセス制限されたプライベートサイトにはなりません。実際の稼働・完成1話PDFは**認証済みNotion**で確認します。
- ブラウザの「設定」に入力する2つのNotion URLは、利用者の端末の `localStorage` に保存します。GitHubには送信しません。Notionの認証トークンを入力してはいけません。
- 画面に使うAI社員名・表示灯は実在社員のリアルタイム状態を示すものではありません。

## GitHub Pages 側

- 公開専用ブランチ：`184649/DHub` の `gh-pages`。`main` には一切変更しません。
- GitHub > DHub > Settings > Pages > Build and deployment > Deploy from a branch > `gh-pages` > `/(root)` で Pages を有効化します（利用可能な場合）。既存Pagesが別設定なら**切り替える前に確認**します。
- Pages が有効になった場合の**想定**サイト：`https://184649.github.io/DHub/office/` （現時点で稼働を保証しません）。
- `https://184649.github.io/DHub/home/office/` の同内容コピーも置きます。
- 公開対象に `.nojekyll`、`office/`、`home/office/` を用意。既存 `main` のバイナリZIPなどは `gh-pages` ブランチのサイトに引き継がない。
- `CNAME` ファイルは作成していません。**dawn-app.jp のDNSは変更しません**。

## 既存 dawn-app.jp/home 配下への追加（ConoHa VPS / Nginx を想定）

**目的URL： `https://dawn-app.jp/home/office/`**。既存の `https://dawn-app.jp/home` を置換しません。

1. 現在の `dawn-app.jp` が本当に ConoHa VPS / Nginx を使用しているか、公開サーバーの管理画面と設定で確認してください。ホスティングが変更済みなら現在の構成に合わせます。
2. 既存 Nginx server 設定をバックアップしてください。**既存 `/home` の location や証明書・PM2 プロセス・DNS・main ブランチを上書き禁止**。
3. 配信専用ディレクトリ例：`/srv/aimanga-office/current/` に、この配布物の **`home/office/` の中身（index.html、assets/、manifest.webmanifest、icon.svg）** を配置します。
4. `deploy/conoha-nginx-location.conf` の `/home/office/` 専用 location を**既存サイトの正しい HTTPS server ブロック内**に追加します。既存の認証方式があればそちらで保護します。必要なら HTTPS・Basic 認証等を適用し、秘密情報はリポジトリへコミットしません。
5. Nginx構文を検証し、既存トップ／home／APIへのルーティングが変わらないことを確認したうえでのみリロード。証明書・DNS はそのまま使います。
6. 公開後に `https://dawn-app.jp/home/office/` をiPhoneで表示、部屋タップ・下部ナビ・Notionリンク設定・既存Dawnホームを確認します。**この手順は現在未実施です。**
7. `deploy/dawn-home-link.html` は既存Dawnの `/home` に「オフィスを開く」リンクを追加する参考です。既存ホームのソースに合った変更が必要です。

## GitHub Pages と既存ドメインの制約

- DNSは**ドメイン名**の参照先を変えます。`/home/office/` というURLのパスだけをGitHub Pagesに振り分けることはDNS設定ではできません。
- 同じ既存 `dawn-app.jp` 上の1パスとして共存するには、Nginxルーティング／既存アプリの静的配信／リバースプロキシ等の**既存配信基盤側**の設定が必要です。
- `dawn-app.jp` の `CNAME` をGitHub Pages側で勝手に設定すると既存サイトの障害やドメイン競合を起こし得るため、設定しません。
- サーバーのアクセス権限が確認できない状態で自動変更・自動デプロイをしたと主張しません。

## 動作と受け入れ条件

- スマホ・PC両対応。8名の部屋と社員カードをタップできる。
- 社内Notionへのリンクは**ユーザー本人が各端末に設定**。APIトークンを公開HTMLに含めない。
- 公開版は固定情報で、実際のAIの内部処理・リアルタイムの作業を見せるものではない。
- 1話全編PDF以外を社長に提出しない。採否・Kindle出版許可はCEO本人のみ。
- 将来の本物のリアルタイム同期は、既存認証と読み取り専用サーバー側Notion接続を必要とし、別途設計・承認・実装する。