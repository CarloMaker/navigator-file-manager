using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Core.Utilities;
using Navigator.Presentation.ViewModels;

namespace Navigator.Presentation.Tests;

public class TabViewModelTests
{
    private static TabViewModel CreateTab(FakeFileSystem fs) =>
        new(new TabServices(fs, new FakeShell(), new FakeWatcher(), new ImmediateDispatcher()), () => false);

    [Fact]
    public async Task Navigation_loads_items_with_folders_first()
    {
        var fs = FakeFileSystem.Sample();
        var tab = CreateTab(fs);

        await tab.NavigateToAsync(@"C:\Data");

        Assert.Equal(@"C:\Data", tab.CurrentPath);
        Assert.Equal("Data", tab.Title);
        Assert.Equal(new[] { "Foto", "a.txt", "b2.jpg", "b10.jpg" }, tab.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task This_pc_lists_drives_with_free_space_and_up_from_a_drive_goes_there()
    {
        var tab = CreateTab(FakeFileSystem.Sample());
        await tab.NavigateToAsync(@"C:\");

        Assert.True(tab.CanGoUp);
        Assert.Equal(new[] { "Questo PC", "C:" }, tab.Breadcrumbs.Select(b => b.Name).ToArray());

        await tab.GoUpCommand.ExecuteAsync(null);

        Assert.True(tab.IsThisPc);
        Assert.Equal(PathHelper.ThisPc, tab.CurrentPath);
        Assert.Equal("Questo PC", tab.Title);
        Assert.False(tab.CanGoUp);
        Assert.Empty(tab.Items);
        Assert.Equal(new[] { "Sistema (C:)", "Disco locale (D:)", "Unità DVD (E:)" }, tab.Drives.Select(d => d.Name).ToArray());
        Assert.Equal("Archivio (Z:)", Assert.Single(tab.NetworkDrives).Name);

        var c = tab.Drives[0];
        Assert.Equal(0.5, c.UsedFraction, 3);
        Assert.False(c.IsAlmostFull);
        Assert.Equal("50 byte disponibili su 100 byte", c.SpaceText);
        Assert.True(tab.Drives[1].IsAlmostFull);
        Assert.False(tab.Drives[2].HasSpaceInfo);
        Assert.Equal("Unità non pronta", tab.Drives[2].SpaceText);

        // Dal testo della barra indirizzi, nella lingua dell'interfaccia, e ritorno a una cartella.
        await tab.NavigateToAsync(@"C:\Data");
        Assert.False(tab.IsThisPc);
        Assert.Empty(tab.Drives);
        await tab.NavigateToAsync("Questo PC");
        Assert.True(tab.IsThisPc);
    }

    [Fact]
    public async Task Back_forward_and_up()
    {
        var tab = CreateTab(FakeFileSystem.Sample());
        await tab.NavigateToAsync(@"C:\Data");
        await tab.NavigateToAsync(@"C:\Data\Foto");

        Assert.True(tab.CanGoBack);
        await tab.GoBackCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\Data", tab.CurrentPath);

        await tab.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\Data\Foto", tab.CurrentPath);

        await tab.GoUpCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\Data", tab.CurrentPath);
    }

    [Fact]
    public async Task Missing_folder_shows_an_error_and_stays_put()
    {
        var tab = CreateTab(FakeFileSystem.Sample());
        await tab.NavigateToAsync(@"C:\Data");

        await tab.NavigateToAsync(@"C:\Nope");

        Assert.Equal(@"C:\Data", tab.CurrentPath);
        Assert.NotNull(tab.ErrorMessage);
    }

    [Fact]
    public async Task Text_filter_and_extension_filter()
    {
        var tab = CreateTab(FakeFileSystem.Sample());
        await tab.NavigateToAsync(@"C:\Data");

        tab.FilterText = "b";
        Assert.Equal(new[] { "b2.jpg", "b10.jpg" }, tab.Items.Select(i => i.Name).ToArray());

        tab.FilterText = string.Empty;
        tab.ExtensionFilters.Single(f => f.Extension == ".jpg").IsChecked = false;
        Assert.Equal(new[] { "Foto", "a.txt" }, tab.Items.Select(i => i.Name).ToArray());
        Assert.True(tab.IsFiltered);
    }

    [Fact]
    public async Task Sorting_by_size_descending_keeps_folders_on_top()
    {
        var tab = CreateTab(FakeFileSystem.Sample());
        await tab.NavigateToAsync(@"C:\Data");

        tab.SortCommand.Execute(SortColumn.Size);
        tab.SortCommand.Execute(SortColumn.Size);

        Assert.Equal(new[] { "Foto", "b10.jpg", "b2.jpg", "a.txt" }, tab.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task Search_shows_results_and_can_be_cleared()
    {
        var tab = CreateTab(FakeFileSystem.Sample());
        await tab.NavigateToAsync(@"C:\Data");

        tab.SearchText = "jpg";
        await tab.SearchAsync();
        Assert.True(tab.IsSearchResults);
        Assert.Equal(3, tab.Items.Count);

        await tab.ClearSearchAsync();
        Assert.False(tab.IsSearchResults);
        Assert.Equal(4, tab.Items.Count);
    }
}

internal sealed class FakeFileSystem : IFileSystemService
{
    private readonly Dictionary<string, List<FileEntry>> _folders = new(StringComparer.OrdinalIgnoreCase);

    public static FakeFileSystem Sample()
    {
        var fs = new FakeFileSystem();
        fs.Add(@"C:\", Dir(@"C:\Data"));
        fs.Add(@"C:\Data",
            File(@"C:\Data\b10.jpg", 3000),
            File(@"C:\Data\a.txt", 10),
            Dir(@"C:\Data\Foto"),
            File(@"C:\Data\b2.jpg", 2000));
        fs.Add(@"C:\Data\Foto", File(@"C:\Data\Foto\mare.jpg", 5000));
        return fs;
    }

    private void Add(string folder, params FileEntry[] entries) => _folders[folder] = [.. entries];

    private static FileEntry File(string path, long size) =>
        new(path, path[(path.LastIndexOf('\\') + 1)..], false, size, new DateTime(2026, 1, 1), FileAttributes.Normal);

    private static FileEntry Dir(string path) =>
        new(path, path[(path.LastIndexOf('\\') + 1)..], true, 0, new DateTime(2026, 1, 1), FileAttributes.Directory);

    public Task<IReadOnlyList<FileEntry>> GetEntriesAsync(string path, bool includeHidden, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FileEntry>>(_folders.TryGetValue(path, out var list) ? list : []);

    public IReadOnlyList<FileEntry> GetSubdirectories(string path, bool includeHidden) =>
        _folders.TryGetValue(path, out var list) ? list.Where(e => e.IsDirectory).ToList() : [];

    public IReadOnlyList<DriveEntry> GetDrives() =>
    [
        new DriveEntry(@"C:\", "Sistema", 100, 50, true, DriveType.Fixed),
        new DriveEntry(@"D:\", "", 1000, 50, true, DriveType.Fixed),
        new DriveEntry(@"E:\", "", 0, 0, false, DriveType.CDRom),
        new DriveEntry(@"Z:\", "Archivio", 200, 100, true, DriveType.Network),
    ];

    public DriveEntry? GetDriveFor(string path) => GetDrives()[0];

    public bool DirectoryExists(string path) => _folders.ContainsKey(path.TrimEnd('\\').Length == 2 ? path : path.TrimEnd('\\'));

    public bool FileExists(string path) => _folders.Values.SelectMany(v => v).Any(e => !e.IsDirectory && e.FullPath == path);

    public IEnumerable<FileEntry> Search(string root, string pattern, bool includeHidden, CancellationToken cancellationToken) =>
        _folders
            .Where(f => f.Key.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => f.Value)
            .Where(e => e.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));
}

internal sealed class FakeShell : IShellService
{
    public void Open(string path)
    {
    }

    public void ShowProperties(string path)
    {
    }

    public void ShowContextMenu(IntPtr ownerWindow, IReadOnlyList<string> paths, int screenX, int screenY)
    {
    }

    public string GetTypeName(string path, bool isDirectory) => isDirectory ? "Cartella di file" : "File";
}

internal sealed class FakeWatcher : IFolderWatcher
{
    public IDisposable Watch(string path, Action onChanged) => new Noop();

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
