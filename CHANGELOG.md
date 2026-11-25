# Changelog

## 0.1.3 — 2026-11-25

- Panels (Bookmarks, Folders, Sessions, Drop Stack) now always use compact rows (about 23 px), not only when
  the items no longer fit, with some padding around the lists so they don't touch the panel edges.
- The address bar and the search box moved into the central column too, above the toolbar: the side panels now
  reach up to the tab bar. In a narrow column the search box shrinks (down to 140 px).

## 0.1.2 — 2026-11-25

- Drive bar above the Folders panel, like CubicExplorer: one button per drive (icon and letter). Click opens the
  drive, Ctrl+click or middle click opens it in a new tab, right click shows the Windows menu. The tooltip shows
  the drive name and free space; the drive of the active tab is highlighted. It follows drives being plugged in
  or removed and can be hidden from More options or from the Folders panel menu.
- The toolbar (back, forward, up, refresh and the command buttons) now sits above the file list instead of
  spanning the whole window, like CubicExplorer; panels on the sides and below reach up to the address bar.
  When the file list is narrow the buttons wrap onto a second line.

## 0.1.1 — 2026-11-24

- Quick View: text files can be edited in place. Ctrl+S saves, keeping the file's encoding (UTF-8 with or
  without BOM, UTF-16, ANSI); undo changes, word wrap and "open in the editor" buttons. Navigator asks before
  discarding unsaved changes and before overwriting a file that another program changed. Files over 4 MB and
  read-only files stay read-only.
- The built-in editor no longer converts ANSI files to UTF-8 on save, and can save hidden files.
- Git status badges on file and folder icons, like TortoiseGit overlays: up to date, modified, added,
  unversioned, ignored and conflicted. Navigator computes them itself with Git for Windows (read-only,
  `--no-optional-locks`); folders that are repositories show a summary when seen from outside.
- Folder sizes in the Size column, calculated on a separate low-priority thread (running total while counting,
  F5 recalculates, sorting by size includes folders). Junctions and symbolic links are not followed.
  Not calculated in the root of a drive or network share.
- Both can be turned off in More options.

## 0.1.0 — first public release

First preview of Navigator File Manager, a tabbed file manager for Windows 10/11 inspired by CubicExplorer
and styled like the Windows 11 File Explorer.

- Tabs in the title bar, breadcrumb address bar, back/forward/up and per-tab history.
- This PC view with drives, free space and usage bars.
- Folder tree, native Windows context menu, Explorer-compatible clipboard, Recycle Bin and undo.
- Details, Medium icons and Large icons views with Windows thumbnails.
- Movable and resizable panels on the left, on the right or below the file list.
- Bookmarks to folders and files, organised in virtual folders; sessions; filters; Quick View; search; Drop Stack.
- Built-in text editor with syntax highlighting.
- English, Italian, German, French and Spanish.
- Single self-contained `Navigator.exe` for Windows x64 (no installation, no .NET runtime needed); portable mode.

The exe is not code-signed yet: Windows SmartScreen may ask for confirmation the first time (More info → Run anyway).

---

**Italiano** — Prima versione pubblica di prova: schede, Questo PC con lo spazio dei dischi, pannelli spostabili
(anche sotto la lista dei file), segnalibri in cartelle virtuali, sessioni, filtri, Quick View, ricerca, Drop Stack,
editor di testo, viste a icone con miniature e cinque lingue. Un solo `Navigator.exe`, senza installazione.
