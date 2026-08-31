# StickerMemo

StickerMemo is a lightweight, open-source sticky notes application for Windows. It keeps notes close at hand in a right-edge deck and lets you work with multiple floating notes at the same time.

## Features

- Right Edge Deck with compact active-note tabs
- Hover previews for quick reading
- Multiple floating note windows with saved position and size
- Active and archived note views
- Import from Microsoft Sticky Notes
- Note color themes and basic font formatting
- System tray controls and optional Start with Windows
- Local SQLite storage

## Requirements

- Windows 10 or Windows 11
- x64 processor

## Install and run

1. Open the latest GitHub Release.
2. Download `StickerMemo.exe`.
3. Place it in a folder where you want to keep the portable app and run it.

The release is a self-contained single EXE, so a separate .NET Runtime installation is not required. StickerMemo currently does not include an installer.

## Your data

StickerMemo stores notes locally in:

```text
%APPDATA%\StickerMemo\AppNotes.db
```

The app does not require a cloud account. If you use **Import Sticky Notes**, StickerMemo copies the Microsoft Sticky Notes database to a temporary location and reads that copy in read-only mode. The original Sticky Notes database is not modified. As with any important data migration, keeping a backup is still recommended.

## Build from source

Install the .NET 8 SDK, then run:

```powershell
dotnet restore
dotnet build -c Release
```

The official release configuration is [`Properties/PublishProfiles/FolderProfile.pubxml`](Properties/PublishProfiles/FolderProfile.pubxml). Create the release binary with that profile:

```powershell
dotnet publish StickerMemo.csproj -p:PublishProfile=Properties/PublishProfiles/FolderProfile.pubxml
```

Please use the checked-in profile for official builds instead of replacing its settings with ad-hoc publish options.

## Contributing

Bug reports and focused fixes are welcome, and you may fork the project under the MIT License. StickerMemo intentionally stays small and straightforward, so proposed changes should preserve its existing scope and simple user experience.

## License

StickerMemo is available under the [MIT License](LICENSE). Third-party packages and their license notices are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

