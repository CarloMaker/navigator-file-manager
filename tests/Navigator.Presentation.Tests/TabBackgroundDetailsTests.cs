using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Presentation.ViewModels;

namespace Navigator.Presentation.Tests;

public class TabBackgroundDetailsTests
{
    private sealed class FakeGit : IGitStatusService
    {
        public IReadOnlyDictionary<string, GitItemStatus> GetFolderStatus(string folder, IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken) =>
            entries.ToDictionary(e => e.Name, e => e.IsDirectory ? GitItemStatus.Modified : GitItemStatus.Normal);
    }

    private sealed class FakeSizes : IFolderSizeService
    {
        public int Calls;

        public long? GetSize(string folder, Action<long>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            progress?.Invoke(1);
            return folder.EndsWith("Foto", StringComparison.Ordinal) ? 5 * 1024 * 1024 : 2048;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static FakeFileSystem FileSystem() => FakeFileSystem.Sample();

    [Fact]
    public async Task Git_status_and_folder_sizes_arrive_in_background()
    {
        var sizes = new FakeSizes();
        var tab = new TabViewModel(
            new TabServices(FileSystem(), new FakeShell(), new FakeWatcher(), new ImmediateDispatcher(), new FakeGit(), sizes),
            () => false);

        await tab.NavigateToAsync(@"C:\Data");

        var foto = tab.Items.Single(i => i.Name == "Foto");
        var file = tab.Items.Single(i => i.Name == "a.txt");
        await WaitUntil(() => !foto.IsSizePending && foto.GitStatus != GitItemStatus.None && file.GitStatus != GitItemStatus.None);

        Assert.Equal(GitItemStatus.Modified, foto.GitStatus);
        Assert.Equal(GitItemStatus.Normal, file.GitStatus);
        Assert.Equal(5L * 1024 * 1024, foto.FolderSize);
        Assert.Equal(string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{5120:N0} KB"), foto.SizeText);

        // F5 ricalcola la dimensione.
        await tab.RefreshAsync();
        await WaitUntil(() => !tab.Items.Single(i => i.Name == "Foto").IsSizePending);
        Assert.Equal(2, sizes.Calls);
    }

    [Fact]
    public async Task Options_turn_the_features_off()
    {
        var sizes = new FakeSizes();
        var tab = new TabViewModel(
            new TabServices(FileSystem(), new FakeShell(), new FakeWatcher(), new ImmediateDispatcher(), new FakeGit(), sizes, () => false, () => false),
            () => false);

        await tab.NavigateToAsync(@"C:\Data");
        await Task.Delay(50);

        var foto = tab.Items.Single(i => i.Name == "Foto");
        Assert.Equal(GitItemStatus.None, foto.GitStatus);
        Assert.False(foto.IsSizePending);
        Assert.Equal(string.Empty, foto.SizeText);
        Assert.Equal(0, sizes.Calls);
    }

    [Fact]
    public async Task Folder_sizes_are_not_calculated_in_a_drive_root()
    {
        var sizes = new FakeSizes();
        var tab = new TabViewModel(
            new TabServices(FileSystem(), new FakeShell(), new FakeWatcher(), new ImmediateDispatcher(), new FakeGit(), sizes),
            () => false);

        await tab.NavigateToAsync(@"C:\");
        await Task.Delay(50);

        var data = tab.Items.Single(i => i.Name == "Data");
        Assert.False(data.IsSizePending);
        Assert.Equal(string.Empty, data.SizeText);
        Assert.Equal(0, sizes.Calls);
        Assert.Equal(GitItemStatus.Modified, data.GitStatus); // lo stato Git invece sì
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"d:\", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\", true)]
    [InlineData(@"C:\Data", false)]
    [InlineData(@"\\server\share\cartella", false)]
    public void Volume_roots_are_recognised(string path, bool expected) =>
        Assert.Equal(expected, Navigator.Core.Utilities.PathHelper.IsVolumeRoot(path));

    [Fact]
    public void Folder_size_text_shows_progress()
    {
        var item = new FileItemViewModel(new FileEntry(@"C:\x", "x", true, 0, DateTime.Now, FileAttributes.Directory), "Cartella");
        Assert.Equal(string.Empty, item.SizeText);
        item.IsSizePending = true;
        Assert.Equal("…", item.SizeText);
        item.FolderSize = 3000;
        Assert.Equal("3 KB…", item.SizeText);
        item.IsSizePending = false;
        Assert.Equal("3 KB", item.SizeText);
        Assert.Equal(3000, item.SortSize);
    }
}
