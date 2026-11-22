using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Navigator.Core.Services;
using static Navigator.Shell.Interop.NativeMethods;
using Navigator.Core.Localization;

namespace Navigator.Shell;

public sealed class ShellService : IShellService
{
    private readonly ConcurrentDictionary<string, string> _typeNames = new(StringComparer.OrdinalIgnoreCase);

    public void Open(string path)
    {
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
        });
    }

    public void ShowProperties(string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
        };
        ShellExecuteEx(ref info);
    }

    public void ShowContextMenu(IntPtr ownerWindow, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        // Con Maiusc premuto Windows mostra i comandi estesi (es. "Copia come percorso").
        var extended = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        ShellContextMenu.Show(ownerWindow, paths, screenX, screenY, extended);
    }

    public string GetTypeName(string path, bool isDirectory)
    {
        var key = isDirectory ? "<dir>" : Path.GetExtension(path);
        if (string.IsNullOrEmpty(key))
        {
            key = "<none>";
        }

        return _typeNames.GetOrAdd(key, _ =>
        {
            var info = new SHFILEINFO();
            var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            var queryPath = isDirectory ? "folder" : "file" + Path.GetExtension(path);
            SHGetFileInfo(queryPath, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
            return string.IsNullOrEmpty(info.szTypeName) ? Strings.FileTypeFallback : info.szTypeName;
        });
    }
}
