using System.Globalization;
using Navigator.Core.Models;
using Navigator.Core.Utilities;

namespace Navigator.Core.Tests;

public class PathHelperTests
{
    [Theory]
    [InlineData("shell:MyComputerFolder")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("Questo PC")]
    [InlineData(" this pc ")]
    public void This_pc_is_recognized(string text)
    {
        Assert.True(PathHelper.IsThisPc(text));
        Assert.Equal(PathHelper.ThisPc, PathHelper.Normalize(text));
    }

    [Fact]
    public void This_pc_navigation()
    {
        Assert.Equal(PathHelper.ThisPc, PathHelper.GetNavigationParent(@"D:\"));
        Assert.Equal(@"D:\", PathHelper.GetNavigationParent(@"D:\Foto"));
        Assert.Null(PathHelper.GetNavigationParent(PathHelper.ThisPc));
        Assert.Null(PathHelper.GetNavigationParent(@"\\server\share"));
        Assert.Equal("Questo PC", PathHelper.GetDisplayName(PathHelper.ThisPc));
        Assert.Equal("Questo PC", PathHelper.ToAddressText(PathHelper.ThisPc));
        Assert.Equal(new[] { "Questo PC", "D:", "Foto" }, PathHelper.GetBreadcrumbs(@"D:\Foto").Select(s => s.Name).ToArray());
        Assert.Equal(new[] { @"\\server\share" }, PathHelper.GetBreadcrumbs(@"\\server\share").Select(s => s.Name).ToArray());
        Assert.False(PathHelper.IsThisPc(@"C:\"));
    }

    [Fact]
    public void Segments_of_a_local_path()
    {
        var segments = PathHelper.GetSegments(@"C:\Users\Anna\Documents");

        Assert.Equal(new[] { "C:", "Users", "Anna", "Documents" }, segments.Select(s => s.Name).ToArray());
        Assert.Equal(@"C:\", segments[0].FullPath);
        Assert.Equal(@"C:\Users\Anna", segments[2].FullPath);
    }

    [Fact]
    public void Segments_of_a_drive_root()
    {
        var segments = PathHelper.GetSegments(@"d:\");

        Assert.Single(segments);
        Assert.Equal("D:", segments[0].Name);
        Assert.Equal(@"D:\", segments[0].FullPath);
    }

    [Fact]
    public void Segments_of_a_unc_path()
    {
        var segments = PathHelper.GetSegments(@"\\server\share\dir\sub");

        Assert.Equal(new[] { @"\\server\share", "dir", "sub" }, segments.Select(s => s.Name).ToArray());
        Assert.Equal(@"\\server\share\dir", segments[1].FullPath);
    }

    [Theory]
    [InlineData(@"C:\Users\Anna", @"C:\Users")]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\Users\", @"C:\")]
    [InlineData(@"C:\", null)]
    public void Parent(string path, string? expected) => Assert.Equal(expected, PathHelper.GetParent(path));

    [Theory]
    [InlineData(@"C:\Users\Anna", "Anna")]
    [InlineData(@"C:\", "C:")]
    public void Display_name(string path, string expected) => Assert.Equal(expected, PathHelper.GetDisplayName(path));

    [Theory]
    [InlineData("\"C:\\Temp\"", @"C:\Temp")]
    [InlineData("c:", @"c:\")]
    public void Normalize(string input, string expected) => Assert.Equal(expected, PathHelper.Normalize(input));

    [Fact]
    public void Same_path_ignores_case_and_trailing_slash() =>
        Assert.True(PathHelper.AreSame(@"C:\Temp\", @"c:\temp"));

    [Theory]
    [InlineData(512, "512 byte")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024 * 1024, "10 MB")]
    public void Format_size(long bytes, string expected) =>
        Assert.Equal(expected, PathHelper.FormatSize(bytes, CultureInfo.InvariantCulture));
}

public class EntryFilterTests
{
    private static FileEntry File(string name) => new($@"C:\x\{name}", name, false, 1, DateTime.Now, FileAttributes.Normal);

    private static FileEntry Folder(string name) => new($@"C:\x\{name}", name, true, 0, DateTime.Now, FileAttributes.Directory);

    [Fact]
    public void Normal_filter_matches_text_anywhere_in_the_name()
    {
        var filter = EntryFilter.Build("steve", strict: false, null);

        Assert.True(filter(File("photo_steve_01.jpg")));
        Assert.False(filter(File("anna.jpg")));
    }

    [Fact]
    public void Strict_filter_needs_the_whole_name_to_match()
    {
        var filter = EntryFilter.Build("steve*.jpg", strict: true, null);

        Assert.True(filter(File("steve_beach.JPG")));
        Assert.False(filter(File("my_steve.jpg")));
        Assert.False(filter(File("steve.png")));
    }

    [Fact]
    public void Several_patterns_are_alternatives()
    {
        var filter = EntryFilter.Build("*.jpg; *.png", strict: true, null);

        Assert.True(filter(File("a.png")));
        Assert.True(filter(File("b.jpg")));
        Assert.False(filter(File("c.gif")));
    }

    [Fact]
    public void Excluded_extensions_hide_files_but_not_folders()
    {
        var filter = EntryFilter.Build(null, strict: false, new HashSet<string> { ".tmp", EntryFilter.NoExtensionKey });

        Assert.False(filter(File("a.tmp")));
        Assert.False(filter(File("README")));
        Assert.True(filter(File("a.txt")));
        Assert.True(filter(Folder("cache.tmp")));
    }

    [Fact]
    public void Regex_special_characters_are_literal() =>
        Assert.True(EntryFilter.Build("(1)", strict: false, null)(File("file (1).txt")));
}

public class NaturalStringComparerTests
{
    [Fact]
    public void Numbers_sort_by_value()
    {
        var names = new[] { "file10.txt", "file2.txt", "File1.txt", "file02b.txt" };

        var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(new[] { "File1.txt", "file2.txt", "file02b.txt", "file10.txt" }, sorted);
    }

    [Fact]
    public void Case_is_ignored() => Assert.Equal(0, NaturalStringComparer.Instance.Compare("ABC", "abc"));
}

public class NavigationHistoryTests
{
    [Fact]
    public void Back_and_forward()
    {
        var history = new NavigationHistory();
        history.Push(@"C:\a");
        history.Push(@"C:\b");
        history.Push(@"C:\c");

        Assert.Equal(@"C:\b", history.GoBack());
        Assert.Equal(@"C:\a", history.GoBack());
        Assert.False(history.CanGoBack);
        Assert.Equal(@"C:\b", history.GoForward());
        Assert.True(history.CanGoForward);
    }

    [Fact]
    public void A_new_location_clears_forward()
    {
        var history = new NavigationHistory();
        history.Push(@"C:\a");
        history.Push(@"C:\b");
        history.GoBack();
        history.Push(@"C:\z");

        Assert.False(history.CanGoForward);
        Assert.Equal(@"C:\a", history.GoBack());
    }

    [Fact]
    public void Same_location_is_not_duplicated()
    {
        var history = new NavigationHistory();
        history.Push(@"C:\a");
        history.Push(@"c:\A\");

        Assert.False(history.CanGoBack);
    }
}
