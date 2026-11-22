using System.IO;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Navigator.Shell.Interop.NativeMethods;

namespace Navigator.Shell;

/// <summary>
/// Icone di sistema, le stesse di Esplora file. In cache per estensione;
/// eseguibili, collegamenti, icone e unità sono letti file per file.
/// </summary>
public static class ShellIconProvider
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".url", ".cur", ".ani", ".msc", ".appref-ms",
    };

    /// <param name="exact">true = icona reale del singolo elemento (cartelle speciali, desktop.ini).</param>
    public static ImageSource? GetIcon(string path, bool isDirectory, bool large = false, bool exact = false)
    {
        var isDriveRoot = path.Length <= 3 && path.EndsWith(@":\", StringComparison.Ordinal);
        var extension = isDirectory ? string.Empty : Path.GetExtension(path);
        var perFile = exact || isDriveRoot || PerFileExtensions.Contains(extension);

        string key;
        if (perFile)
        {
            key = path;
        }
        else if (isDirectory)
        {
            key = "<dir>";
        }
        else
        {
            key = string.IsNullOrEmpty(extension) ? "<none>" : extension;
        }

        key = (large ? "L|" : "S|") + key;

        return Cache.GetOrAdd(key, _ => Load(path, isDirectory, perFile, large));
    }

    /// <summary>
    /// Icona di una cartella virtuale della shell indicata per nome ("shell:MyComputerFolder" = Questo PC),
    /// che non ha un percorso su disco.
    /// </summary>
    public static ImageSource? GetShellFolderIcon(string shellName, bool large = false) =>
        Cache.GetOrAdd((large ? "L|" : "S|") + shellName, _ => LoadFromShellName(shellName, large));

    private static ImageSource? LoadFromShellName(string shellName, bool large)
    {
        if (SHParseDisplayName(shellName, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = new SHFILEINFO();
            var flags = SHGFI_PIDL | SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
            var result = SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            return result == IntPtr.Zero ? null : ToImage(info.hIcon);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static ImageSource? Load(string path, bool isDirectory, bool perFile, bool large)
    {
        var info = new SHFILEINFO();
        var flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        var queryPath = path;
        uint attributes = 0;

        if (!perFile)
        {
            // Non tocchiamo il disco: Windows sceglie l'icona dal tipo (veloce, nessun download da OneDrive).
            flags |= SHGFI_USEFILEATTRIBUTES;
            attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            queryPath = isDirectory ? "folder" : "file" + Path.GetExtension(path);
        }

        var result = SHGetFileInfo(queryPath, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        return result == IntPtr.Zero ? null : ToImage(info.hIcon);
    }

    private static ImageSource? ToImage(IntPtr icon)
    {
        if (icon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(icon);
        }
    }
}
