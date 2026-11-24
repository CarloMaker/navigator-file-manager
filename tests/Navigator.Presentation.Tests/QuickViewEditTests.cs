using System.Text;
using Navigator.Core.Models;
using Navigator.Presentation.ViewModels;

namespace Navigator.Presentation.Tests;

public sealed class QuickViewEditTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("navigator-quickview-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeHost : IQuickViewEditorHost
    {
        public string Text = string.Empty;
        public bool SaveAnswer = true;
        public bool OverwriteAnswer = true;
        public int Asked;
        public int OverwriteAsked;
        public string? Error;

        public string GetText() => Text;

        public bool AskSaveChanges(string fileName)
        {
            Asked++;
            return SaveAnswer;
        }

        public bool ConfirmOverwrite(string fileName)
        {
            OverwriteAsked++;
            return OverwriteAnswer;
        }

        public void ShowError(string message) => Error = message;
    }

    private string CreateFile(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private static FileItemViewModel Item(string path)
    {
        var info = new FileInfo(path);
        return new FileItemViewModel(new FileEntry(path, info.Name, false, info.Length, info.LastWriteTime, info.Attributes), "Documento di testo");
    }

    private static (QuickViewViewModel Vm, FakeHost Host) Create()
    {
        var host = new FakeHost();
        return (new QuickViewViewModel { Host = host }, host);
    }

    [Fact]
    public async Task Text_file_is_editable_and_saved()
    {
        var path = CreateFile("note.txt", "riga 1\nriga 2");
        var (vm, host) = Create();
        var loaded = 0;
        vm.DocumentLoaded += (_, _) => loaded++;

        await vm.ShowAsync(Item(path));

        Assert.Equal(QuickViewKind.Text, vm.Kind);
        Assert.True(vm.IsEditable);
        Assert.Equal(1, loaded);
        Assert.Equal("UTF-8 · 2 righe", vm.TextStatus);
        Assert.False(vm.SaveChangesCommand.CanExecute(null));

        host.Text = "riga 1\nriga 2 modificata";
        vm.SetModified(true);
        Assert.True(vm.IsDirty);
        Assert.EndsWith("modificato", vm.TextStatus);
        Assert.True(vm.SaveChangesCommand.CanExecute(null));

        Assert.True(vm.Save());
        Assert.False(vm.IsDirty);
        Assert.Equal("riga 1\nriga 2 modificata", File.ReadAllText(path));
        Assert.Equal(0, host.OverwriteAsked);
    }

    [Fact]
    public async Task Switching_file_with_unsaved_changes_asks_to_save()
    {
        var first = CreateFile("a.txt", "A");
        var second = CreateFile("b.txt", "B");
        var (vm, host) = Create();

        await vm.ShowAsync(Item(first));
        host.Text = "A2";
        vm.SetModified(true);

        await vm.ShowAsync(Item(second));

        Assert.Equal(1, host.Asked);
        Assert.Equal("A2", File.ReadAllText(first));
        Assert.Equal("B", vm.Text);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Discarding_changes_leaves_the_file_untouched()
    {
        var first = CreateFile("a.txt", "A");
        var (vm, host) = Create();

        await vm.ShowAsync(Item(first));
        host.Text = "A2";
        host.SaveAnswer = false;
        vm.SetModified(true);

        await vm.ShowAsync(null);

        Assert.Equal(1, host.Asked);
        Assert.Equal("A", File.ReadAllText(first));
        Assert.Equal(QuickViewKind.None, vm.Kind);
    }

    [Fact]
    public async Task Refreshing_the_same_file_keeps_the_edits()
    {
        var path = CreateFile("a.txt", "A");
        var (vm, host) = Create();
        var loaded = 0;
        vm.DocumentLoaded += (_, _) => loaded++;

        await vm.ShowAsync(Item(path));
        host.Text = "A2";
        vm.SetModified(true);

        await vm.ShowAsync(Item(path)); // la cartella si è aggiornata

        Assert.Equal(0, host.Asked);
        Assert.True(vm.IsDirty);
        Assert.Equal(1, loaded);
    }

    [Fact]
    public async Task Revert_reloads_from_disk()
    {
        var path = CreateFile("a.txt", "A");
        var (vm, host) = Create();
        var loaded = 0;
        vm.DocumentLoaded += (_, _) => loaded++;

        await vm.ShowAsync(Item(path));
        vm.SetModified(true);
        vm.RevertCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Equal(2, loaded);
        Assert.Equal("A", vm.Text);
    }

    [Fact]
    public async Task Saving_over_a_file_changed_elsewhere_asks_first()
    {
        var path = CreateFile("a.txt", "A");
        var (vm, host) = Create();

        await vm.ShowAsync(Item(path));
        File.WriteAllText(path, "cambiato da un altro programma");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        host.Text = "mia versione";
        host.OverwriteAnswer = false;
        vm.SetModified(true);

        Assert.False(vm.Save());
        Assert.Equal(1, host.OverwriteAsked);
        Assert.Equal("cambiato da un altro programma", File.ReadAllText(path));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task Read_only_files_are_not_editable()
    {
        var path = CreateFile("a.txt", "A");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var (vm, _) = Create();

        await vm.ShowAsync(Item(path));

        Assert.Equal(QuickViewKind.Text, vm.Kind);
        Assert.False(vm.IsEditable);
        Assert.EndsWith("sola lettura", vm.TextStatus);
    }

    [Fact]
    public async Task Large_files_show_a_partial_read_only_preview()
    {
        var path = Path.Combine(_dir, "grande.log");
        File.WriteAllText(path, new string('x', (int)QuickViewViewModel.MaxEditableBytes + 10));
        var (vm, _) = Create();

        await vm.ShowAsync(Item(path));

        Assert.Equal(QuickViewKind.Text, vm.Kind);
        Assert.False(vm.IsEditable);
        Assert.Contains("anteprima parziale", vm.TextStatus);
    }
}
