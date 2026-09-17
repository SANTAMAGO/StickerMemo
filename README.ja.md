# StickerMemo

画面の端に常駐する、軽量な Windows 付箋アプリ。

[English](README.md) · [한국어](README.ko.md) · [日本語](README.ja.md)

## ダウンロード

**[StickerMemo v1.0.0 をダウンロード](https://github.com/SANTAMAGO/StickerMemo/releases/download/v1.0.0/StickerMemo-v1.0.0-win-x64-portable.zip)**
Windows x64 · Portable · インストーラー不要

または [Releases](https://github.com/SANTAMAGO/StickerMemo/releases) ページですべてのバージョンを確認できます。

### クイックスタート

1. `StickerMemo-v1.0.0-win-x64-portable.zip` を任意の場所に展開します。
2. `StickerMemo.exe` を実行します。

これだけです — インストーラーも管理者権限も不要で、`%APPDATA%\StickerMemo\` 以外には何も書き込みません。

## 主な機能

- **Edge Deck** — 画面右端に固定される、常に最前面に表示される細いバー。開いているメモごとにタブが並び、ホバーでプレビュー、クリックでフローティングウィンドウとして開きます。
- **Floating Notes** — 画面のどこにでもドラッグできる独立したメモウィンドウ。位置・サイズ・色・フォント設定をそれぞれ記憶します。
- **All Notes ドロワー** — Deck から開く一つのドロワーで、アクティブ/アーカイブ済みのメモを検索・閲覧できます。
- **Microsoft Sticky Notes からのインポート** — 既存の Sticky Notes データベースをコピー(読み取り専用)して読み込み、StickerMemo にメモを取り込みます。元のデータベースは変更されません。
- **多言語 UI** — 한국어 / English / 日本語 に対応。初回起動時に Windows の表示言語を自動検出し、以降は Settings から再起動なしで即座に言語を切り替えられます。
- **Settings / About ウィンドウ** — 言語の変更、「Windows 起動時に自動的に開始」の切り替え、ビルド/バージョン情報の確認。
- **システムトレイ操作** — 新規メモ、Deck の表示/非表示、インポート、設定、About、終了をトレイアイコンから直接操作できます。
- **シングルインスタンス** — StickerMemo を再度起動しても新しいウィンドウは開かず、既存のインスタンスにフォーカスします。
- **ローカル保存のみ** — メモはローカルの SQLite データベースにのみ保存され、どこにも送信されません。
- **軽量** — Rust + Tauri で構築され、OS 標準の WebView2 をそのまま利用するため、ブラウザランタイムを同梱せず実行ファイルが小さく保たれます。

## Screenshots

_このリポジトリにはまだスクリーンショットがありません — 今後のアップデートで追加予定です。_

## Project History(開発の背景)

StickerMemo は、Windows 標準の Sticky Notes を使っていて感じた不便さから生まれました。メモが画面いっぱいに開いて邪魔になるか、隠れて忘れられてしまうかのどちらかしかないことが不満でした。必要なときにワンクリックでメモを取り出せて、必要ないときは画面の端に目立たず置いておきたい——そこから始まりました。

最初に動作した版は .NET 8 / WPF で作られ、実際に使いながら発展しました。複数のメモウィンドウ、カラーテーマ、フォント書式、Microsoft Sticky Notes からのインポート、システムトレイ連携などはこの時期に追加されたものです。この WPF 実装は削除せず、このリポジトリの git history にそのまま残しています(`feat: complete rust rebuild of StickerMemo with Tauri v2` コミット以前の履歴を参照)——StickerMemo が実際にそこから始まったからです。

その後、より軽量で反応の良いアプリにするため、**Rust + Tauri v2** で全体を作り直しました。その際、既存のメモデータベースのパスとテーブルスキーマは意図的にそのまま維持し、既存のメモが移行作業なしに引き継がれるようにしています。v1.0.0 はこの Rust/Tauri 版の最初の正式リリースで、韓国語/英語/日本語の多言語対応(Windows のロケール自動検出とリアルタイム切り替え)、Settings ウィンドウ、そして今回の portable パッケージングが追加されています。

## リポジトリ構成

```text
src-tauri/   Rust バックエンド (Tauri v2): コマンド、ストレージ、i18n、プラットフォーム連携 — 現在の実装
ui/          フロントエンド: プレーンな HTML/CSS/JS、ビルドステップなし — 現在の実装
ui/lang/     翻訳元ファイル (ko/en/ja)
docs/        WPF → Rust 移行時の開発記録(分析レポート、ユーザー向けドキュメントではありません)
```

旧 .NET/WPF のソースは現在の `main` の最新状態には含まれておらず、上記の移行コミット以前の git history でそのまま閲覧できます。

## データについて

メモは次の場所にローカル保存されます。

```text
%APPDATA%\StickerMemo\AppNotes.db
```

アカウント登録もクラウド同期もネットワークアクセスもありません。**Import Sticky Notes** を使用する場合、StickerMemo は Microsoft Sticky Notes データベースの一時コピーのみを読み取り、元のデータベースは変更しません。とはいえ、重要なデータ移行の際は事前にバックアップを取っておくことをお勧めします。

## ソースからビルドする

必要環境: [Rust](https://www.rust-lang.org/tools/install)(stable)、Windows 向け [Tauri v2 事前準備](https://v2.tauri.app/start/prerequisites/)。

```powershell
cd src-tauri
cargo build --release
```

ビルドされた実行ファイルは `src-tauri/target/release/sticker-memo.exe` に出力されます。

## Known Issues

- 配布される実行ファイルはコード署名されていないため、初回実行時に Windows SmartScreen の警告(「Windows によって PC が保護されました」)が表示される場合があります — **詳細情報 → 実行** を選択してください。

## コントリビュート

バグ報告や、目的の明確な修正提案を歓迎します。StickerMemo は意図的に小さくシンプルに保たれているプロジェクトなので、プルリクエストはその範囲を大きく外れないようお願いします。

## ライセンス

MIT — [LICENSE](LICENSE) を参照してください。サードパーティ依存関係のライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にまとめています。
