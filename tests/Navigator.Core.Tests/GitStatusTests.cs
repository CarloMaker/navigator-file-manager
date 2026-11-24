using System.Diagnostics;
using Navigator.Core.Models;
using Navigator.Core.Utilities;
using Navigator.Infrastructure;

namespace Navigator.Core.Tests;

public class GitStatusParserTests
{
    private static readonly (string, bool)[] RootChildren =
    [
        (".git", true), ("src", true), ("docs", true), ("bin", true), ("new", true),
        ("a.txt", false), ("b.txt", false), ("c.txt", false), ("d.txt", false), ("e.txt", false),
    ];

    [Fact]
    public void Parse_reads_entries_and_skips_the_old_name_of_renames()
    {
        var entries = GitStatusParser.Parse(" M a.txt\0R  new.txt\0old.txt\0?? dir/\0!! bin/\0");

        Assert.Equal(
            [new(' ', 'M', "a.txt"), new('R', ' ', "new.txt"), new('?', '?', "dir/"), new('!', '!', "bin/")],
            entries);
    }

    [Theory]
    [InlineData('?', '?', GitItemStatus.Untracked)]
    [InlineData('!', '!', GitItemStatus.Ignored)]
    [InlineData(' ', 'M', GitItemStatus.Modified)]
    [InlineData('M', ' ', GitItemStatus.Modified)]
    [InlineData(' ', 'D', GitItemStatus.Modified)]
    [InlineData('A', ' ', GitItemStatus.Added)]
    [InlineData('A', 'M', GitItemStatus.Added)]
    [InlineData('R', ' ', GitItemStatus.Added)]
    [InlineData('U', 'U', GitItemStatus.Conflicted)]
    [InlineData('A', 'A', GitItemStatus.Conflicted)]
    [InlineData('D', 'U', GitItemStatus.Conflicted)]
    public void Codes_map_to_statuses(char x, char y, GitItemStatus expected) =>
        Assert.Equal(expected, GitStatusParser.ToStatus(x, y));

    [Fact]
    public void Children_of_the_root_get_their_own_or_the_worst_nested_status()
    {
        var entries = GitStatusParser.Parse(
            " M a.txt\0A  c.txt\0?? d.txt\0 M src/deep/x.cs\0?? docs/notes.md\0!! bin/\0?? new/\0UU e.txt\0");

        var map = GitStatusParser.MapChildren(entries, string.Empty, RootChildren);

        Assert.False(map.ContainsKey(".git"));
        Assert.Equal(GitItemStatus.Modified, map["a.txt"]);
        Assert.Equal(GitItemStatus.Normal, map["b.txt"]);
        Assert.Equal(GitItemStatus.Added, map["c.txt"]);
        Assert.Equal(GitItemStatus.Untracked, map["d.txt"]);
        Assert.Equal(GitItemStatus.Conflicted, map["e.txt"]);
        Assert.Equal(GitItemStatus.Modified, map["src"]);
        Assert.Equal(GitItemStatus.Normal, map["docs"]); // dentro solo file non versionati: resta aggiornata
        Assert.Equal(GitItemStatus.Ignored, map["bin"]);
        Assert.Equal(GitItemStatus.Untracked, map["new"]);
    }

    [Fact]
    public void Subfolder_paths_are_matched_without_regard_to_case()
    {
        var entries = GitStatusParser.Parse(" M Src/Main.cs\0A  src/sub/new.cs\0");

        var map = GitStatusParser.MapChildren(entries, "src", [("main.cs", false), ("sub", true), ("other.cs", false)]);

        Assert.Equal(GitItemStatus.Modified, map["main.cs"]);
        Assert.Equal(GitItemStatus.Modified, map["sub"]);
        Assert.Equal(GitItemStatus.Normal, map["other.cs"]);
    }

    [Fact]
    public void Everything_inside_an_ignored_or_untracked_folder_inherits_its_status()
    {
        var ignored = GitStatusParser.MapChildren(GitStatusParser.Parse("!! bin/\0"), "bin/Debug", [("app.dll", false), ("x", true)]);
        Assert.All(ignored.Values, s => Assert.Equal(GitItemStatus.Ignored, s));

        var untracked = GitStatusParser.MapChildren(GitStatusParser.Parse("?? new/\0"), "new", [("a.txt", false)]);
        Assert.Equal(GitItemStatus.Untracked, untracked["a.txt"]);
    }

    [Fact]
    public void Summary_of_a_repository()
    {
        Assert.Equal(GitItemStatus.Normal, GitStatusParser.Summarize([]));
        Assert.Equal(GitItemStatus.Normal, GitStatusParser.Summarize(GitStatusParser.Parse("?? x\0")));
        Assert.Equal(GitItemStatus.Modified, GitStatusParser.Summarize(GitStatusParser.Parse("A  x\0")));
        Assert.Equal(GitItemStatus.Conflicted, GitStatusParser.Summarize(GitStatusParser.Parse(" M y\0UU x\0")));
    }
}

/// <summary>Prova con git vero su un repository temporaneo (saltata se git non è installato).</summary>
public sealed class GitStatusServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("navigator-git-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal); // gli oggetti di git sono di sola lettura
        }

        Directory.Delete(_dir, recursive: true);
    }

    private static bool Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static IReadOnlyList<FileEntry> List(string folder) =>
        new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 })
            .Select(i => new FileEntry(i.FullName, i.Name, i is DirectoryInfo, i is FileInfo f ? f.Length : 0, i.LastWriteTime, i.Attributes))
            .ToList();

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Statuses_of_a_real_repository_and_of_a_repository_seen_from_outside()
    {
        var repo = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(repo);
        if (!Git(repo, "init", "-q"))
        {
            return; // git non disponibile
        }

        Write("repo/a.txt", "a");
        Write("repo/src/b.cs", "b");
        Write("repo/.gitignore", "bin/\n");
        Assert.True(Git(repo, "add", "-A"));
        Assert.True(Git(repo, "commit", "-q", "-m", "primo"));

        Write("repo/src/b.cs", "b modificato");
        Write("repo/c.txt", "nuovo, non versionato");
        Write("repo/d.txt", "aggiunto");
        Assert.True(Git(repo, "add", "d.txt"));
        Write("repo/bin/app.dll", "ignorato");

        var service = new GitStatusService();
        var root = service.GetFolderStatus(repo, List(repo), CancellationToken.None);

        Assert.Equal(GitItemStatus.Normal, root["a.txt"]);
        Assert.Equal(GitItemStatus.Normal, root[".gitignore"]);
        Assert.Equal(GitItemStatus.Modified, root["src"]);
        Assert.Equal(GitItemStatus.Untracked, root["c.txt"]);
        Assert.Equal(GitItemStatus.Added, root["d.txt"]);
        Assert.Equal(GitItemStatus.Ignored, root["bin"]);
        Assert.False(root.ContainsKey(".git"));

        var src = service.GetFolderStatus(Path.Combine(repo, "src"), List(Path.Combine(repo, "src")), CancellationToken.None);
        Assert.Equal(GitItemStatus.Modified, src["b.cs"]);

        var bin = service.GetFolderStatus(Path.Combine(repo, "bin"), List(Path.Combine(repo, "bin")), CancellationToken.None);
        Assert.Equal(GitItemStatus.Ignored, bin["app.dll"]);

        // Dalla cartella che contiene il repository: la cartella "repo" ha lo stato riassunto.
        Directory.CreateDirectory(Path.Combine(_dir, "altro"));
        var outside = service.GetFolderStatus(_dir, List(_dir), CancellationToken.None);
        Assert.Equal(GitItemStatus.Modified, outside["repo"]);
        Assert.False(outside.ContainsKey("altro"));

        Assert.True(Git(repo, "commit", "-q", "-am", "secondo"));
        outside = service.GetFolderStatus(_dir, List(_dir), CancellationToken.None);
        Assert.Equal(GitItemStatus.Normal, outside["repo"]);
    }

    [Fact]
    public void Without_git_nothing_is_reported()
    {
        var service = new GitStatusService(Path.Combine(_dir, "git-inesistente"));
        Write("x.txt", "x");
        Assert.Empty(service.GetFolderStatus(_dir, List(_dir), CancellationToken.None));
    }
}

public sealed class FolderSizeServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("navigator-size-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Sums_all_files_including_hidden_and_nested_but_not_links()
    {
        File.WriteAllBytes(Path.Combine(_dir, "a.bin"), new byte[1000]);
        Directory.CreateDirectory(Path.Combine(_dir, "sub", "deep"));
        File.WriteAllBytes(Path.Combine(_dir, "sub", "b.bin"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_dir, "sub", "deep", "c.bin"), new byte[30]);
        var hidden = Path.Combine(_dir, "sub", ".nascosto");
        File.WriteAllBytes(hidden, new byte[4]);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var link = Path.Combine(_dir, "collegamento");
        var hasLink = TryCreateLink(link, Path.Combine(_dir, "sub"));

        var service = new FolderSizeService();
        Assert.Equal(1234, service.GetSize(_dir, null, CancellationToken.None));
        Assert.Equal(234, service.GetSize(Path.Combine(_dir, "sub"), null, CancellationToken.None));
        if (hasLink)
        {
            Assert.Null(service.GetSize(link, null, CancellationToken.None));
        }
    }

    [Fact]
    public void Can_be_cancelled()
    {
        File.WriteAllBytes(Path.Combine(_dir, "a.bin"), new byte[10]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new FolderSizeService().GetSize(_dir, null, cts.Token));
    }

    private static bool TryCreateLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // su Windows serve la modalità sviluppatore
        }
    }
}
