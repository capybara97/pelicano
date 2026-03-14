<p align="center">
  <img src="Winui/Resources/app.png" width="160" alt="Pelicano mascot" />
</p>

# Pelicano

[Overview](./README.md) · [한국어](./README.ko.md)

Pelicano is a WinUI 3 clipboard history app for Windows 10/11. It stores text, images, and file-copy history locally, then helps you bring everything back quickly with search, preview, and a global shortcut.

## Why Pelicano?

- It is built to reduce repetitive copy-and-paste friction.
- It manages text, images, and file drops in one place.
- It stays easy to reach through the system tray and `Ctrl+Shift+V`.
- It focuses on a local-first workflow instead of remote sync.

## Key Features

- Text, image, and file-drop history
- Search, preview, and multi-select recopy
- System tray presence and background behavior
- Theme settings, auto-start, and audit logging
- Manifest-based update checks and installer download flow

## Release Notes

### v0.2

- Migrated the desktop project to a WinUI 3 app shell
- Added update checks based on a remote manifest
- Refined the core desktop experience around quick recall

### v0.1

- Initial release focused on reducing repetitive copy-and-paste work

## Tech Stack

- .NET 8
- WinUI 3
- LiteDB
- Inno Setup

## Quick Start

```powershell
dotnet restore .\Pelicano.sln --configfile .\NuGet.Config
dotnet build .\Pelicano.sln
```

To build the installer as well, run `build.bat`. It handles icon conversion, restore, publish, and Inno Setup packaging in one flow.

## Project Structure

- `Winui/`: main WinUI 3 app and runtime logic
- `Installer/`: Inno Setup installer script
- `docs/`: developer and security documents
- `scripts/`: build helper scripts

## Local Data and Security

Pelicano stores its local data under `%APPDATA%\Pelicano` by default.

- Settings: `%APPDATA%\Pelicano\settings.json`
- History DB: `%APPDATA%\Pelicano\history.db`
- Image cache: `%APPDATA%\Pelicano\images`
- Logs: `%APPDATA%\Pelicano\logs`
- Update downloads: `%APPDATA%\Pelicano\updates`

For more operational details, see:

- [Developer Guide](./docs/개발자_가이드.md)
- [Security Notes](./docs/보안팀_설명서.md)
