<p align="center">
  <img src="assets/preview-512.png" width="128" alt="Navigator File Manager icon">
</p>

<h1 align="center">Navigator File Manager</h1>

<p align="center">
  A tabbed file manager for Windows 10/11, inspired by <b>CubicExplorer</b> and styled like <b>Windows 11 File Explorer</b>.<br>
  Written from scratch in C# on .NET 10 and WPF.
</p>

<p align="center">
  <a href="../../releases/latest"><b>Download</b></a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="CHANGELOG.md">Changelog</a>
</p>

---

CubicExplorer (Cubic Reality) was a much-loved tabbed file manager whose development stopped in 2014.
Navigator brings back its ideas — tabs, bookmarks organised in folders, sessions, filters, Quick View, Drop Stack,
freely arranged panels — in a modern app that looks and feels like the Windows 11 File Explorer.
It does not reuse any CubicExplorer code.

## Download

Get `Navigator.exe` from the [latest release](../../releases/latest).
It is a single self-contained file (about 75 MB): no installation and no .NET runtime needed.

> The exe is not code-signed yet, so Windows SmartScreen may show "Windows protected your PC" the first time.
> Click **More info → Run anyway**.

**Portable mode:** create an empty `portable.txt` file (or a `Data` folder) next to `Navigator.exe`.
Settings, bookmarks and sessions are then stored in `Data\` instead of `%AppData%\Navigator`.

## Features

- **Tabs** in the title bar: middle-click to close, drag to reorder, drop files on a tab to copy or move them there.
- **Windows 11 look**: light/dark theme and accent colour follow Windows and update live; Fluent controls.
- **Breadcrumb address bar** (editable with Ctrl+L), back/forward/up, per-tab history.
- **This PC** view with every drive, its free space and usage bar (red when almost full), updated when a drive is plugged in or removed.
- **Folder tree** with Quick Access and This PC, kept in sync with the open folder.
- **Native Windows context menu**, Explorer-compatible copy/cut/paste, Recycle Bin and undo.
- **Views**: Details, Medium icons and Large icons with Windows thumbnails (photos, videos, PDFs…).
- **Movable panels**: drag a panel header to the left side, the right side or **below the file list**; resize with the dividers.
- **Bookmarks** to folders and files, organised in **virtual folders**, with drag and drop; open a whole folder of bookmarks in tabs.
- **Sessions**: save and restore sets of tabs; the last tabs reopen at startup.
- **Filters**: text, wildcards and per-extension check boxes.
- **Quick View**: preview images, text, audio and video; edit text files in place (Ctrl+S to save).
- **Git status badges** on icons (needs Git for Windows) and **folder sizes** calculated in the background.
- **Search** in the current folder and its subfolders.
- **Drop Stack**: collect files from several folders, then copy or move them all at once.
- **Text editor** with syntax highlighting (F4).
- **Languages**: English, Italian, German, French, Spanish (defaults to the Windows language).

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| Ctrl+T / Ctrl+W | New tab / close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Alt+← / Alt+→ / Alt+↑ / Backspace | Back / Forward / Up |
| Ctrl+L, Alt+D | Edit the address |
| Ctrl+F, F3 | Search |
| F5 | Refresh |
| F2 | Rename |
| Del / Shift+Del | Delete to Recycle Bin / permanently |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste |
| Ctrl+Shift+N | New folder |
| Ctrl+Shift+C | Copy path |
| Ctrl+D | Bookmark the current folder |
| Ctrl+Shift+S | Save session |
| F4 | Open in the editor |
| Ctrl+Shift+6 / 4 / 3 | Details / Medium icons / Large icons |
| Alt+Enter | Properties |

## Building from source

Requirements: Windows 10/11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(Visual Studio 2026 or JetBrains Rider are optional).

```powershell
dotnet build Navigator.slnx
dotnet test Navigator.slnx
dotnet run --project src/Navigator.App
```

Single-file exe:

```powershell
dotnet publish src/Navigator.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Every push is built and tested by GitHub Actions; pushing a tag such as `v0.1.0` publishes the exe in a release.

### Project structure

```text
src/
  Navigator.Core/            models, interfaces, filters, history, localization (no Windows dependency)
  Navigator.Infrastructure/  file system, JSON settings, folder watching
  Navigator.Shell/           Windows shell integration (icons, thumbnails, context menu, file operations)
  Navigator.Presentation/    view models (CommunityToolkit.Mvvm)
  Navigator.App/             WPF user interface, theme, windows
tests/                       xUnit tests for Core and Presentation
tools/                       translation table and .resx generator
```

### Translations

All texts live in `tools/strings.py`, one line per text in every language.
After editing it, run `python tools/gen_strings.py` to regenerate the `.resx` files.
A test checks that every text exists in every language with the same placeholders.
New languages are welcome.

## Known limitations

- The Maximize button does not show the Windows 11 Snap layouts.
- Icon views are not virtualised yet, so folders with many thousands of files are slower than in Details.
- Search scans the disk; Windows Search index support is planned.
- Rename uses a dialog rather than in-place editing.

## License

[MIT](LICENSE). Navigator is not affiliated with Cubic Reality or Microsoft.
It uses [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) and Microsoft.Extensions.DependencyInjection (all MIT).
