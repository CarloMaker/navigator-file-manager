using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Core.Utilities;

namespace Navigator.Infrastructure;

public sealed class FileSystemService : IFileSystemService
{
    public Task<IReadOnlyList<FileEntry>> GetEntriesAsync(string path, bool includeHidden, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<FileEntry>>(
            () =>
            {
                var result = new List<FileEntry>();
                var dir = new DirectoryInfo(path);
                foreach (var info in dir.EnumerateFileSystemInfos("*", Options(includeHidden, recurse: false)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.Add(ToEntry(info));
                }

                return result;
            },
            cancellationToken);

    public IReadOnlyList<FileEntry> GetSubdirectories(string path, bool includeHidden)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateDirectories("*", Options(includeHidden, recurse: false))
                .Select(ToEntry)
                .OrderBy(e => e.Name, NaturalStringComparer.Instance)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    public IReadOnlyList<DriveEntry> GetDrives()
    {
        var result = new List<DriveEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            result.Add(ToDrive(drive));
        }

        return result;
    }

    public DriveEntry? GetDriveFor(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return ToDrive(new DriveInfo(root));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<FileEntry> Search(string root, string pattern, bool includeHidden, CancellationToken cancellationToken)
    {
        var hasWildcard = pattern.Contains('*') || pattern.Contains('?');
        var regex = EntryFilter.ToRegex(hasWildcard ? pattern : $"*{pattern}*", strict: true);
        var options = Options(includeHidden, recurse: true);

        foreach (var info in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (regex.IsMatch(info.Name))
            {
                yield return ToEntry(info);
            }
        }
    }

    private static EnumerationOptions Options(bool includeHidden, bool recurse) => new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = recurse,
        AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    private static FileEntry ToEntry(FileSystemInfo info) => info switch
    {
        FileInfo file => new FileEntry(file.FullName, file.Name, false, file.Length, file.LastWriteTime, file.Attributes),
        _ => new FileEntry(info.FullName, info.Name, true, 0, info.LastWriteTime, info.Attributes),
    };

    private static DriveEntry ToDrive(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return new DriveEntry(drive.RootDirectory.FullName, string.Empty, 0, 0, false, drive.DriveType);
            }

            return new DriveEntry(
                drive.RootDirectory.FullName,
                drive.VolumeLabel,
                drive.TotalSize,
                drive.AvailableFreeSpace,
                true,
                drive.DriveType);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DriveEntry(drive.Name, string.Empty, 0, 0, false, drive.DriveType);
        }
    }
}
