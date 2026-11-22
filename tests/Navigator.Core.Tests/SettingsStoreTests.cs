using Navigator.Core.Models;
using Navigator.Infrastructure;

namespace Navigator.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navigator-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Old_flat_bookmarks_file_is_still_read()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "bookmarks.json"),
            """[ { "name": "Progetti", "path": "D:\\Progetti" }, { "name": "Foto", "path": "D:\\Foto" } ]""");

        var bookmarks = new JsonSettingsStore(_dir, isPortable: false).LoadBookmarks();

        Assert.Equal(new[] { "Progetti", "Foto" }, bookmarks.Select(b => b.Name).ToArray());
        Assert.Equal(@"D:\Progetti", bookmarks[0].Path);
        Assert.Null(bookmarks[0].Children);
    }

    [Fact]
    public void Bookmark_folders_round_trip()
    {
        var store = new JsonSettingsStore(_dir, isPortable: false);
        store.SaveBookmarks(
        [
            BookmarkNode.Folder("Lavoro", BookmarkNode.Link("Report", @"D:\Lavoro\report.xlsx"), BookmarkNode.Folder("Vuota")),
            BookmarkNode.Link("Download", @"C:\Users\Anna\Downloads"),
        ]);

        var loaded = new JsonSettingsStore(_dir, isPortable: false).LoadBookmarks();

        Assert.Null(loaded[0].Path);
        Assert.Equal(new[] { "Report", "Vuota" }, loaded[0].Children!.Select(c => c.Name).ToArray());
        Assert.Equal(@"D:\Lavoro\report.xlsx", loaded[0].Children![0].Path);
        Assert.Empty(loaded[0].Children![1].Children!);
        Assert.Equal(@"C:\Users\Anna\Downloads", loaded[1].Path);
    }

    [Fact]
    public void Panel_layout_is_saved_with_readable_names()
    {
        var store = new JsonSettingsStore(_dir, isPortable: false);
        store.SaveSettings(new AppSettings
        {
            Panels = [new PanelState { Id = PanelId.QuickView, Dock = PanelDock.Left, Order = 2, Weight = 1.5, IsVisible = false }],
        });

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        var loaded = store.LoadSettings().Panels!.Single();

        Assert.Contains("\"QuickView\"", json);
        Assert.Equal(PanelDock.Left, loaded.Dock);
        Assert.False(loaded.IsVisible);
        Assert.Equal(1.5, loaded.Weight);
    }
}
