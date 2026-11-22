using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Presentation.ViewModels;

namespace Navigator.Presentation.Tests;

public class BookmarkTests
{
    private static (MainViewModel Vm, RecordingShell Shell, MemoryStore Store) Create()
    {
        var shell = new RecordingShell();
        var store = new MemoryStore();
        var vm = new MainViewModel(
            FakeFileSystem.Sample(),
            shell,
            new NoFileOperations(),
            new NoClipboard(),
            new SilentDialogs(),
            store,
            new FakeWatcher(),
            new ImmediateDispatcher(),
            new FakeKnownFolders());
        return (vm, shell, store);
    }

    private static string[] Names(IEnumerable<BookmarkItemViewModel> items) => items.Select(b => b.Name).ToArray();

    [Fact]
    public void Files_and_folders_can_be_bookmarked_by_dropping_them()
    {
        var (vm, _, store) = Create();

        var added = vm.AddBookmarks([@"C:\Data\Foto", @"C:\Data\a.txt", @"C:\Nope\missing.txt"]);

        Assert.Equal(2, added);
        Assert.Equal(new[] { "Foto", "a.txt" }, Names(vm.Bookmarks));
        Assert.True(vm.IsFileBookmark(vm.Bookmarks[1]));
        Assert.False(vm.IsFileBookmark(vm.Bookmarks[0]));
        Assert.Equal(2, store.Bookmarks.Count);
    }

    [Fact]
    public void Duplicates_are_skipped_and_drops_insert_at_the_position()
    {
        var (vm, _, _) = Create();
        vm.AddBookmarks([@"C:\Data\Foto", @"C:\Data\a.txt"]);

        var added = vm.AddBookmarks([@"C:\Data\b2.jpg", @"c:\data\FOTO"], null, 1);

        Assert.Equal(1, added);
        Assert.Equal(new[] { "Foto", "b2.jpg", "a.txt" }, Names(vm.Bookmarks));
    }

    [Fact]
    public void Dragging_reorders_bookmarks()
    {
        var (vm, _, store) = Create();
        vm.AddBookmarks([@"C:\Data\Foto", @"C:\Data\a.txt", @"C:\Data\b2.jpg"]);
        var first = vm.Bookmarks[0];

        vm.MoveBookmark(first, null, 3); // in fondo
        Assert.Equal(new[] { "a.txt", "b2.jpg", "Foto" }, Names(vm.Bookmarks));

        vm.MoveBookmark(first, null, 0); // di nuovo in cima
        Assert.Equal(new[] { "Foto", "a.txt", "b2.jpg" }, Names(vm.Bookmarks));
        Assert.Equal("Foto", store.Bookmarks[0].Name);
    }

    [Fact]
    public void New_folder_goes_inside_the_selected_folder()
    {
        var (vm, _, _) = Create();

        vm.NewBookmarkFolderCommand.Execute(null);
        var projects = vm.Bookmarks.Single();
        vm.SelectedBookmark = projects;
        vm.NewBookmarkFolderCommand.Execute(null);

        Assert.True(projects.IsFolder);
        Assert.True(projects.Children.Single().IsFolder);
        Assert.Same(projects, projects.Children[0].Parent);
    }

    [Fact]
    public void Bookmarks_can_be_dropped_into_a_folder_and_moved_out()
    {
        var (vm, _, store) = Create();
        vm.AddBookmarks([@"C:\Data\Foto", @"C:\Data\a.txt"]);
        vm.NewBookmarkFolderCommand.Execute(null);
        var folder = vm.Bookmarks.Single(b => b.IsFolder);
        var foto = vm.Bookmarks[0];

        Assert.True(vm.MoveBookmark(foto, folder, 0));
        vm.AddBookmarks([@"C:\Data\b2.jpg"], folder);

        Assert.Equal(new[] { "a.txt", "Nuova cartella" }, Names(vm.Bookmarks));
        Assert.Equal(new[] { "Foto", "b2.jpg" }, Names(folder.Children));
        Assert.Same(folder, foto.Parent);
        Assert.Equal(new[] { "Foto", "b2.jpg" }, store.Bookmarks.Single(n => n.Path is null).Children!.Select(n => n.Name).ToArray());

        Assert.True(vm.MoveBookmark(foto, null, 0));
        Assert.Null(foto.Parent);
        Assert.Equal(new[] { "Foto", "a.txt", "Nuova cartella" }, Names(vm.Bookmarks));
    }

    [Fact]
    public void A_folder_cannot_be_moved_into_itself_or_a_subfolder()
    {
        var (vm, _, _) = Create();
        vm.NewBookmarkFolderCommand.Execute(null);
        var outer = vm.Bookmarks.Single();
        vm.SelectedBookmark = outer;
        vm.NewBookmarkFolderCommand.Execute(null);
        var inner = outer.Children.Single();

        Assert.False(vm.MoveBookmark(outer, outer, 0));
        Assert.False(vm.MoveBookmark(outer, inner, 0));
        Assert.Same(outer, vm.Bookmarks.Single());
    }

    [Fact]
    public void Removing_a_folder_removes_its_contents()
    {
        var (vm, _, store) = Create();
        vm.NewBookmarkFolderCommand.Execute(null);
        var folder = vm.Bookmarks.Single();
        vm.AddBookmarks([@"C:\Data\Foto", @"C:\Data\a.txt"], folder);

        vm.RemoveBookmarkCommand.Execute(folder);

        Assert.Empty(vm.Bookmarks);
        Assert.Empty(store.Bookmarks);
    }

    [Fact]
    public async Task Opening_a_file_bookmark_opens_the_file()
    {
        var (vm, shell, _) = Create();
        await vm.InitializeAsync([@"C:\Data"]);
        vm.AddBookmarks([@"C:\Data\a.txt"]);

        await vm.OpenBookmarkCommand.ExecuteAsync(vm.Bookmarks[0]);

        Assert.Equal(new[] { @"C:\Data\a.txt" }, shell.Opened.ToArray());
        Assert.Equal(@"C:\Data", vm.SelectedTab!.CurrentPath);
    }

    [Fact]
    public async Task Open_location_selects_the_file_in_its_folder()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync([@"C:\Data"]);
        vm.AddBookmarks([@"C:\Data\Foto\mare.jpg"]);

        await vm.OpenBookmarkLocationCommand.ExecuteAsync(vm.Bookmarks[0]);

        Assert.Equal(@"C:\Data\Foto", vm.SelectedTab!.CurrentPath);
        Assert.Equal("mare.jpg", vm.SelectedTab.SelectedItem?.Name);
    }

    [Fact]
    public async Task Open_all_bookmarks_includes_folders_inside_virtual_folders_and_skips_files()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync([@"C:\Data"]);
        vm.NewBookmarkFolderCommand.Execute(null);
        var folder = vm.Bookmarks.Single();
        vm.AddBookmarks([@"C:\Data\Foto", @"C:\Data\a.txt"], folder);

        await vm.OpenAllBookmarksCommand.ExecuteAsync(null);

        Assert.Equal(new[] { @"C:\Data", @"C:\Data\Foto" }, vm.Tabs.Select(t => t.CurrentPath).ToArray());
    }

    [Fact]
    public async Task Middle_click_on_a_virtual_folder_opens_its_bookmarks_in_tabs()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync([@"C:\Data"]);
        vm.NewBookmarkFolderCommand.Execute(null);
        var folder = vm.Bookmarks.Single();
        vm.AddBookmarks([@"C:\Data\Foto"], folder);

        await vm.OpenBookmarkInNewTabCommand.ExecuteAsync(folder);

        Assert.Equal(2, vm.Tabs.Count);
        Assert.Equal(@"C:\Data\Foto", vm.SelectedTab!.CurrentPath);
    }

    [Fact]
    public async Task The_tree_is_restored_from_the_saved_nodes()
    {
        var (vm, _, store) = Create();
        store.Bookmarks =
        [
            BookmarkNode.Link("Dati", @"C:\Data"),
            BookmarkNode.Folder("Progetti", BookmarkNode.Link("Foto", @"C:\Data\Foto")),
        ];

        await vm.InitializeAsync([@"C:\Data"]);

        var folder = vm.Bookmarks[1];
        Assert.True(folder.IsFolder);
        Assert.Equal("Foto", folder.Children.Single().Name);
        Assert.Same(folder, folder.Children[0].Parent);
    }

    [Fact]
    public async Task The_folder_tree_opens_up_to_the_current_folder()
    {
        var (vm, _, _) = Create();
        await vm.InitializeAsync([@"C:\Data\Foto"]);

        var drive = vm.TreeRoots.Last().Children.First(d => d.FullPath == @"C:\");
        var data = drive.Children.Single();
        var foto = data.Children.Single();

        Assert.True(drive.IsExpanded);
        Assert.True(data.IsExpanded);
        Assert.True(foto.IsSelected);
        Assert.False(vm.IsSyncingTree);
    }

    [Fact]
    public async Task View_mode_is_saved_with_the_settings()
    {
        var (vm, _, store) = Create();
        await vm.InitializeAsync([@"C:\Data"]);

        vm.SetViewModeCommand.Execute(FileViewMode.LargeIcons);
        vm.SaveState();

        Assert.Equal(FileViewMode.LargeIcons, store.Settings.ViewMode);
    }

    [Fact]
    public async Task Panel_layout_is_saved_with_the_settings()
    {
        var (vm, _, store) = Create();
        await vm.InitializeAsync([@"C:\Data"]);
        vm.Layout.MoveToOtherSide(vm.Layout.Bookmarks);

        vm.SaveState();

        var saved = store.Settings.Panels!.Single(p => p.Id == PanelId.Bookmarks);
        Assert.Equal(PanelDock.Right, saved.Dock);
    }
}

internal sealed class RecordingShell : IShellService
{
    public List<string> Opened { get; } = [];

    public void Open(string path) => Opened.Add(path);

    public void ShowProperties(string path)
    {
    }

    public void ShowContextMenu(IntPtr ownerWindow, IReadOnlyList<string> paths, int screenX, int screenY)
    {
    }

    public string GetTypeName(string path, bool isDirectory) => isDirectory ? "Cartella di file" : "File";
}

internal sealed class MemoryStore : ISettingsStore
{
    public AppSettings Settings { get; private set; } = new();

    public List<BookmarkNode> Bookmarks { get; set; } = [];

    public List<Session> Sessions { get; private set; } = [];

    public string DataDirectory => @"C:\Dati";

    public bool IsPortable => false;

    public AppSettings LoadSettings() => Settings;

    public void SaveSettings(AppSettings settings) => Settings = settings;

    public IReadOnlyList<BookmarkNode> LoadBookmarks() => Bookmarks;

    public void SaveBookmarks(IEnumerable<BookmarkNode> bookmarks) => Bookmarks = bookmarks.ToList();

    public IReadOnlyList<Session> LoadSessions() => Sessions;

    public void SaveSessions(IEnumerable<Session> sessions) => Sessions = sessions.ToList();
}

internal sealed class NoFileOperations : IFileOperations
{
    public bool Copy(IReadOnlyList<string> sources, string destinationFolder) => true;

    public bool Move(IReadOnlyList<string> sources, string destinationFolder) => true;

    public bool Delete(IReadOnlyList<string> paths, bool permanent) => true;

    public string Rename(string path, string newName) => path;

    public string CreateFolder(string parentFolder, string baseName) => parentFolder;
}

internal sealed class NoClipboard : IClipboardService
{
    public void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
    }

    public (IReadOnlyList<string> Paths, bool IsCut) GetFiles() => (Array.Empty<string>(), false);

    public bool HasFiles() => false;

    public void SetText(string text)
    {
    }
}

internal sealed class SilentDialogs : IDialogService
{
    public string? Prompt(string title, string message, string defaultValue) => defaultValue;

    public bool Confirm(string title, string message) => true;

    public void ShowError(string message) => throw new InvalidOperationException(message);
}

internal sealed class FakeKnownFolders : IKnownFolders
{
    public string Home => @"C:\Data";

    public IReadOnlyList<(string Name, string Path)> GetQuickAccess() => [];
}
