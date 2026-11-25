using System.IO;
using Navigator.Core.Models;
using Navigator.Presentation.ViewModels;

namespace Navigator.Presentation.Tests;

public class DriveBarTests
{
    private static DriveButtonViewModel Button(string root) =>
        new(new DriveEntry(root, string.Empty, 100, 40, true, DriveType.Fixed));

    [Fact]
    public void LetterIsTheDriveLetterOnly()
    {
        Assert.Equal("C", Button(@"C:\").Letter);
        Assert.Equal("Z", Button(@"Z:\").Letter);
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:", true)]
    [InlineData(@"C:\Windows", true)]
    [InlineData(@"c:\windows\system32", true)]
    [InlineData(@"D:\", false)]
    [InlineData(@"CD:\x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsMatchesPathsOnTheDrive(string? path, bool expected) =>
        Assert.Equal(expected, Button(@"C:\").Contains(path));

    [Fact]
    public void ToolTipShowsNameAndFreeSpace()
    {
        var tip = Button(@"C:\").ToolTip;
        Assert.Contains("(C:)", tip);
        Assert.True(tip.Split(Environment.NewLine).Length >= 3);
    }
}
