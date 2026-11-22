using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Navigator.Core.Localization;

namespace Navigator.Presentation.ViewModels;

public enum QuickViewKind
{
    None,
    Image,
    Text,
    Media,
    Info,
}

/// <summary>Anteprima del file selezionato: immagini, testo, audio/video, altrimenti solo le informazioni.</summary>
public sealed partial class QuickViewViewModel : ObservableObject
{
    private const int MaxTextChars = 64 * 1024;
    private const long MaxSniffSize = 2 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".jxr", ".wdp",
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".ini", ".cfg", ".conf", ".csv", ".tsv", ".json", ".xml", ".xaml", ".yaml", ".yml",
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".vb", ".fs", ".c", ".h", ".cpp", ".hpp", ".cc",
        ".java", ".kt", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".htm", ".css", ".scss", ".sql", ".ps1",
        ".psm1", ".bat", ".cmd", ".sh", ".rs", ".go", ".php", ".rb", ".lua", ".toml", ".gitignore", ".editorconfig",
        ".uproject", ".uplugin", ".usf", ".ush", ".glsl", ".hlsl", ".shader",
    };

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private QuickViewKind _kind;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _text;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    public static bool IsTextExtension(string extension) => TextExtensions.Contains(extension);

    public async Task ShowAsync(FileItemViewModel? item)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        Text = null;
        if (item is null)
        {
            Kind = QuickViewKind.None;
            FilePath = null;
            Title = string.Empty;
            Details = string.Empty;
            return;
        }

        Title = item.Name;
        Details = item.IsDirectory
            ? item.TypeName + "\n" + Strings.Format(Strings.LastModified, item.ModifiedText)
            : $"{item.TypeName} · {item.SizeText}\n" + Strings.Format(Strings.LastModified, item.ModifiedText);

        // Cartelle e file solo online (OneDrive): nessuna lettura del contenuto.
        if (item.IsDirectory || item.Entry.IsCloudOnly)
        {
            FilePath = item.FullPath;
            Kind = QuickViewKind.Info;
            return;
        }

        var extension = item.Entry.Extension;
        FilePath = item.FullPath;

        if (ImageExtensions.Contains(extension))
        {
            Kind = QuickViewKind.Image;
            return;
        }

        if (MediaExtensions.Contains(extension))
        {
            Kind = QuickViewKind.Media;
            return;
        }

        if (TextExtensions.Contains(extension) || item.Size <= MaxSniffSize)
        {
            var path = item.FullPath;
            string? text;
            try
            {
                text = await Task.Run(() => ReadText(path, requireText: !TextExtensions.Contains(extension)), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (text is not null)
            {
                Text = text;
                Kind = QuickViewKind.Text;
                return;
            }
        }

        Kind = QuickViewKind.Info;
    }

    /// <summary>Legge l'inizio del file; null se sembra binario o non leggibile.</summary>
    private static string? ReadText(string path, bool requireText)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[Math.Min(8192, stream.Length)];
            var read = stream.Read(head, 0, head.Length);

            var hasBom = read >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF));
            if (requireText && !hasBom && Array.IndexOf(head, (byte)0, 0, read) >= 0)
            {
                return null; // byte nulli: file binario
            }

            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[MaxTextChars];
            var chars = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, chars);
            return reader.EndOfStream ? text : text + "\n\n…";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
