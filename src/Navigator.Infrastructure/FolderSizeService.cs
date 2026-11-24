using System.Diagnostics;
using System.IO.Enumeration;
using Navigator.Core.Services;

namespace Navigator.Infrastructure;

/// <summary>Somma le dimensioni dei file di una cartella e di tutte le sottocartelle (senza aprire i file).</summary>
public sealed class FolderSizeService : IFolderSizeService
{
    private const long ProgressIntervalMs = 300;

    public long? GetSize(string folder, Action<long>? progress, CancellationToken cancellationToken)
    {
        if (IsLink(folder))
        {
            return null;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0, // file nascosti e di sistema compresi, come in Proprietà di Esplora file
        };

        var files = new FileSystemEnumerable<long>(folder, (ref FileSystemEntry e) => e.Length, options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory,

            // Junction e link simbolici non si seguono (si conterebbero due volte o all'infinito);
            // le cartelle di OneDrive sono anch'esse reparse point ma non sono collegamenti.
            ShouldRecursePredicate = (ref FileSystemEntry e) =>
                (e.Attributes & FileAttributes.ReparsePoint) == 0 || !IsLink(e.ToFullPath()),
        };

        long total = 0;
        var count = 0;
        var clock = Stopwatch.StartNew();
        var lastReport = 0L;
        foreach (var length in files)
        {
            total += length;
            if ((++count & 511) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (progress is not null && clock.ElapsedMilliseconds - lastReport >= ProgressIntervalMs)
                {
                    lastReport = clock.ElapsedMilliseconds;
                    progress(total);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return total;
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
