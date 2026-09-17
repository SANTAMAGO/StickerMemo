# StickerMemo

A lightweight sticky-notes app for Windows that lives on the edge of your screen.

[English](README.md) · [한국어](README.ko.md) · [日本語](README.ja.md)

## Download

**[Download StickerMemo v1.0.0](https://github.com/SANTAMAGO/StickerMemo/releases/download/v1.0.0/StickerMemo-v1.0.0-win-x64-portable.zip)**
Windows x64 · Portable · No installer required

Or see the [Releases](https://github.com/SANTAMAGO/StickerMemo/releases) page for all versions.

### Quick start

1. Unzip `StickerMemo-v1.0.0-win-x64-portable.zip` anywhere.
2. Run `StickerMemo.exe`.

That's it — no installer, no admin rights, nothing written outside `%APPDATA%\StickerMemo\`.

## Features

- **Edge Deck** — a slim, always-on-top strip docked to the right edge of the screen that holds a tab for every open note. Hover a tab for a quick preview, click it to pop the note open as a floating window.
- **Floating Notes** — independent, resizable note windows you can drag anywhere; each remembers its own position, size, color, and font settings.
- **All Notes drawer** — search and browse every note, split into Active and Archived, from a single drawer opened off the Deck.
- **Import from Microsoft Sticky Notes** — reads a copy of your existing Sticky Notes database (read-only, original left untouched) and brings your notes into StickerMemo.
- **Multilingual UI** — 한국어 / English / 日本語, with automatic detection of your Windows display language on first run and instant switching afterward (no restart needed) from Settings.
- **Settings & About windows** — change language, toggle "Start with Windows", and see build/version info.
- **System tray control** — new note, show/hide Deck, import, settings, about, and quit, all from the tray icon.
- **Single instance** — launching StickerMemo again just focuses the existing instance instead of opening a duplicate.
- **Local-only storage** — notes are kept in a local SQLite database; nothing is sent anywhere.
- **Lightweight** — built with Rust + Tauri on top of the OS's own WebView2, so the shipped executable stays small with no bundled browser runtime.

## Screenshots

_Not yet added to this repository — coming in a future update._

## Project History

StickerMemo started from a simple frustration with Windows' built-in Sticky Notes: notes were either fully open and in the way, or tucked away and easy to forget about. The idea was to keep every note one click away, tucked out of sight at the edge of the screen when you don't need it, and instantly available when you do.

The first working version was built with .NET 8 / WPF and used day to day, growing to include multiple note windows, color themes, font formatting, an import path from Microsoft Sticky Notes, and system tray integration. That WPF implementation is preserved in this repository's git history (see the commits before `feat: complete rust rebuild of StickerMemo with Tauri v2`) rather than deleted, since it's genuinely how the project started.

It was later rebuilt from scratch on **Rust + Tauri v2** for a smaller footprint and snappier UI, while deliberately keeping the same on-disk database path and table schema so existing notes carry over without migration. v1.0.0 is the first release of that Rust/Tauri version, adding Korean/English/Japanese localization (with automatic Windows-locale detection and live switching), a Settings window, and this portable packaging.

## Repository structure

```text
src-tauri/   Rust backend (Tauri v2): commands, storage, i18n, platform integration — the active implementation
ui/          Frontend: plain HTML/CSS/JS, no build step — the active implementation
ui/lang/     Translation source files (ko/en/ja)
docs/        Development-process notes from the WPF→Rust rebuild (analysis reports, not user documentation)
```

The original .NET/WPF source is not present at this tip of `main` — it remains fully browsable in git history at and before the rebuild commit mentioned above.

## Your data

Notes are stored locally at:

```text
%APPDATA%\StickerMemo\AppNotes.db
```

No account, no cloud sync, no network access. If you use **Import Sticky Notes**, StickerMemo reads a temporary copy of the Microsoft Sticky Notes database and never modifies the original — keeping a backup is still good practice before any data migration.

## Building from source

Requirements: [Rust](https://www.rust-lang.org/tools/install) (stable) and the [Tauri v2 prerequisites](https://v2.tauri.app/start/prerequisites/) for Windows.

```powershell
cd src-tauri
cargo build --release
```

The compiled executable is written to `src-tauri/target/release/sticker-memo.exe`.

## Known issues

- The released executable is not code-signed, so Windows SmartScreen may warn on first run ("Windows protected your PC") — click **More info → Run anyway**.

## Contributing

Bug reports and focused fixes are welcome. StickerMemo intentionally stays small and simple, so please keep pull requests aligned with that scope.

## License

MIT — see [LICENSE](LICENSE). Third-party dependency licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
