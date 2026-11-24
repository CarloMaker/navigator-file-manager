using System.Text;
using Navigator.Core.Utilities;

namespace Navigator.Core.Tests;

public sealed class TextFileCodecTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("navigator-codec-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FileWith(byte[] bytes)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Utf8_without_bom_stays_without_bom()
    {
        var path = FileWith(Encoding.UTF8.GetBytes("città\r\nperché"));
        var content = TextFileCodec.Read(path, 1 << 20, requireText: true)!;

        Assert.Equal("città\r\nperché", content.Text);
        Assert.Equal("UTF-8", TextFileCodec.Describe(content.Encoding));
        Assert.True(content.IsComplete);

        TextFileCodec.Write(path, content.Text + "!", content.Encoding);
        Assert.Equal(Encoding.UTF8.GetBytes("città\r\nperché!"), File.ReadAllBytes(path));
    }

    [Fact]
    public void Utf8_bom_is_kept()
    {
        var path = FileWith([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("ciao")]);
        var content = TextFileCodec.Read(path, 1 << 20, requireText: true)!;

        Assert.Equal("ciao", content.Text);
        Assert.Equal("UTF-8 BOM", TextFileCodec.Describe(content.Encoding));

        TextFileCodec.Write(path, "salve", content.Encoding);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("salve")], File.ReadAllBytes(path));
    }

    [Fact]
    public void Utf16_with_bom_is_text_even_with_null_bytes()
    {
        var path = FileWith([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("abc")]);
        var content = TextFileCodec.Read(path, 1 << 20, requireText: true)!;

        Assert.Equal("abc", content.Text);
        Assert.Equal("UTF-16 LE", TextFileCodec.Describe(content.Encoding));

        TextFileCodec.Write(path, "xyz", content.Encoding);
        Assert.Equal([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("xyz")], File.ReadAllBytes(path));
    }

    [Fact]
    public void Ansi_files_are_not_corrupted_on_save()
    {
        // "perché €" in Windows-1252: non è UTF-8 valido.
        byte[] ansi = [0x70, 0x65, 0x72, 0x63, 0x68, 0xE9, 0x20, 0x80];
        var path = FileWith(ansi);
        var content = TextFileCodec.Read(path, 1 << 20, requireText: true)!;

        Assert.Equal("perché €", content.Text);
        Assert.Equal("WINDOWS-1252", TextFileCodec.Describe(content.Encoding));

        TextFileCodec.Write(path, content.Text, content.Encoding);
        Assert.Equal(ansi, File.ReadAllBytes(path));
    }

    [Fact]
    public void Binary_files_are_rejected_when_text_is_required()
    {
        var path = FileWith([0x4D, 0x5A, 0x00, 0x01, 0x02]);
        Assert.Null(TextFileCodec.Read(path, 1 << 20, requireText: true));
    }

    [Fact]
    public void Partial_read_is_marked_incomplete_and_tolerates_a_cut_character()
    {
        var path = FileWith(Encoding.UTF8.GetBytes(new string('a', 9) + "è" + new string('b', 20)));
        var content = TextFileCodec.Read(path, 10, requireText: true)!; // "è" tagliata a metà

        Assert.False(content.IsComplete);
        Assert.Equal("UTF-8", TextFileCodec.Describe(content.Encoding));
        Assert.StartsWith(new string('a', 9), content.Text);
    }

    [Fact]
    public void Write_replaces_longer_content()
    {
        var path = FileWith(Encoding.UTF8.GetBytes("un testo piuttosto lungo"));
        TextFileCodec.Write(path, "corto", new UTF8Encoding(false));
        Assert.Equal("corto", File.ReadAllText(path));
    }
}
