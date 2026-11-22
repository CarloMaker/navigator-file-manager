using System.IO;
using System.Windows;
using System.Windows.Interop;
using Navigator.Core.Services;
using Navigator.Core.Utilities;
using static Navigator.Shell.Interop.NativeMethods;
using Navigator.Core.Localization;

namespace Navigator.Shell;

/// <summary>
/// Copia, sposta ed elimina con le finestre di Windows (avanzamento, conflitti, Cestino, annulla).
/// </summary>
public sealed class ShellFileOperations : IFileOperations
{
    public bool Copy(IReadOnlyList<string> sources, string destinationFolder)
    {
        // Copia nella stessa cartella → Windows crea "nome - Copia".
        var sameFolder = sources.All(s => PathHelper.AreSame(PathHelper.GetParent(s) ?? string.Empty, destinationFolder));
        var flags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | (sameFolder ? FOF_RENAMEONCOLLISION : 0));
        return Run(FO_COPY, sources, destinationFolder, flags);
    }

    public bool Move(IReadOnlyList<string> sources, string destinationFolder)
    {
        var toMove = sources
            .Where(s => !PathHelper.AreSame(PathHelper.GetParent(s) ?? string.Empty, destinationFolder))
            .ToList();
        return toMove.Count == 0 || Run(FO_MOVE, toMove, destinationFolder, (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR));
    }

    public bool Delete(IReadOnlyList<string> paths, bool permanent)
    {
        var flags = permanent ? FOF_WANTNUKEWARNING : (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING);
        return Run(FO_DELETE, paths, null, flags);
    }

    public string Rename(string path, string newName)
    {
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new IOException(Strings.Format(Strings.InvalidName, newName));
        }

        var parent = PathHelper.GetParent(path) ?? throw new IOException(Strings.CannotRenameDrive);
        var target = PathHelper.Combine(parent, newName);

        if (string.Equals(path, target, StringComparison.Ordinal))
        {
            return path;
        }

        if (Directory.Exists(path))
        {
            // Cambio solo di maiuscole: Directory.Move rifiuta lo stesso nome, serve un passaggio intermedio.
            if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase))
            {
                var temp = PathHelper.Combine(parent, Guid.NewGuid().ToString("N"));
                Directory.Move(path, temp);
                Directory.Move(temp, target);
            }
            else
            {
                Directory.Move(path, target);
            }
        }
        else
        {
            File.Move(path, target);
        }

        return target;
    }

    public string CreateFolder(string parentFolder, string baseName)
    {
        var candidate = PathHelper.Combine(parentFolder, baseName);
        var n = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = PathHelper.Combine(parentFolder, $"{baseName} ({n++})");
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static bool Run(uint operation, IReadOnlyList<string> sources, string? destination, ushort flags)
    {
        if (sources.Count == 0)
        {
            return true;
        }

        var op = new SHFILEOPSTRUCT
        {
            hwnd = GetOwner(),
            wFunc = operation,
            // Elenco separato da \0 e terminato da un doppio \0 (il marshaller aggiunge l'ultimo).
            pFrom = string.Join('\0', sources) + '\0',
            pTo = destination is null ? null : destination + '\0',
            fFlags = flags,
        };

        var result = SHFileOperation(ref op);
        return result == 0 && !op.fAnyOperationsAborted;
    }

    private static IntPtr GetOwner()
    {
        var window = Application.Current?.MainWindow;
        return window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
    }
}
