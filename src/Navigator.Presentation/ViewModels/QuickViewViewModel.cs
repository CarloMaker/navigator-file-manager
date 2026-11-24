using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigator.Core.Localization;
using Navigator.Core.Utilities;

namespace Navigator.Presentation.ViewModels;

public enum QuickViewKind
{
    None,
    Image,
    Text,
    Media,
    Info,
}

/// <summary>Collegamento con l'editor della Quick View: il testo modificato e le domande all'utente.</summary>
public interface IQuickViewEditorHost
{
    /// <summary>Testo attualmente nell'editor.</summary>
    string GetText();

    /// <summary>Chiede se salvare le modifiche prima di passare a un altro file: true = salva, false = scarta.</summary>
    bool AskSaveChanges(string fileName);

    /// <summary>Il file è cambiato su disco (o non c'è più) dopo l'apertura: true = salva comunque.</summary>
    bool ConfirmOverwrite(string fileName);

    void ShowError(string message);
}

/// <summary>
/// Anteprima del file selezionato: immagini, testo, audio/video, altrimenti solo le informazioni.
/// I file di testo si possono modificare al volo e salvare (Ctrl+S) mantenendo la codifica originale.
/// </summary>
public sealed partial class QuickViewViewModel : ObservableObject
{
    /// <summary>Oltre questa dimensione il testo si vede solo in parte e non si modifica nella Quick View.</summary>
    public const long MaxEditableBytes = 4L * 1024 * 1024;

    private const long MaxPreviewBytes = 64 * 1024;
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
    private Encoding? _encoding;
    private DateTime _loadedWriteTimeUtc;
    private bool _isPartial;
    private bool _isReadOnlyFile;

    [ObservableProperty]
    private QuickViewKind _kind;

    [ObservableProperty]
    private string? _filePath;

    /// <summary>Testo letto dal disco (o salvato per ultimo). Il testo modificato sta nell'editor.</summary>
    [ObservableProperty]
    private string? _text;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _details = string.Empty;

    /// <summary>True se il testo si può modificare e salvare (file completo, non di sola lettura).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isEditable;

    /// <summary>Ci sono modifiche non salvate nell'editor.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isDirty;

    /// <summary>Codifica, righe e stato (modificato, sola lettura, anteprima parziale).</summary>
    [ObservableProperty]
    private string _textStatus = string.Empty;

    /// <summary>Il testo nell'editor va sostituito con <see cref="Text"/> (nuovo file, ricarica o annulla modifiche).</summary>
    public event EventHandler? DocumentLoaded;

    public IQuickViewEditorHost? Host { get; set; }

    public static bool IsTextExtension(string extension) => TextExtensions.Contains(extension);

    public async Task ShowAsync(FileItemViewModel? item)
    {
        var newPath = item?.FullPath;
        var samePath = newPath is not null && FilePath is not null && PathHelper.AreSame(newPath, FilePath);

        if (IsDirty)
        {
            if (samePath && item is { IsDirectory: false })
            {
                // La stessa voce ricaricata (es. aggiornamento della cartella): si tengono le modifiche.
                SetHeader(item);
                return;
            }

            ResolvePendingChanges();
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        if (!samePath || Kind != QuickViewKind.Text)
        {
            ClearText();
        }

        if (item is null)
        {
            Kind = QuickViewKind.None;
            FilePath = null;
            Title = string.Empty;
            Details = string.Empty;
            return;
        }

        SetHeader(item);

        // Cartelle e file solo online (OneDrive): nessuna lettura del contenuto.
        if (item.IsDirectory || item.Entry.IsCloudOnly)
        {
            ClearText();
            FilePath = item.FullPath;
            Kind = QuickViewKind.Info;
            return;
        }

        var extension = item.Entry.Extension;
        FilePath = item.FullPath;

        if (ImageExtensions.Contains(extension))
        {
            ClearText();
            Kind = QuickViewKind.Image;
            return;
        }

        if (MediaExtensions.Contains(extension))
        {
            ClearText();
            Kind = QuickViewKind.Media;
            return;
        }

        if (TextExtensions.Contains(extension) || item.Size <= MaxSniffSize)
        {
            LoadedText? loaded;
            try
            {
                loaded = await Task.Run(() => LoadText(item.FullPath, requireText: !TextExtensions.Contains(extension)), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (IsDirty)
            {
                return; // l'utente ha iniziato a scrivere mentre il file si ricaricava: si tengono le modifiche
            }

            if (loaded is not null)
            {
                ApplyLoaded(loaded);
                Kind = QuickViewKind.Text;
                return;
            }
        }

        ClearText();
        Kind = QuickViewKind.Info;
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void SaveChanges() => Save();

    /// <summary>Salva il testo dell'editor nel file, con la codifica originale.</summary>
    public bool Save()
    {
        if (!IsEditable || FilePath is not { } path || _encoding is null || Host is null)
        {
            return false;
        }

        var name = Path.GetFileName(path);
        if (HasChangedOnDisk(path) && !Host.ConfirmOverwrite(name))
        {
            return false;
        }

        var text = Host.GetText();
        try
        {
            TextFileCodec.Write(path, text, _encoding);
            _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Host.ShowError(Strings.Format(Strings.SaveFailed, ex.Message));
            return false;
        }

        Text = text;
        IsDirty = false;
        UpdateStatus();
        return true;
    }

    /// <summary>Annulla le modifiche e ricarica il file dal disco.</summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    public void Revert()
    {
        if (FilePath is not { } path)
        {
            return;
        }

        LoadedText? loaded;
        try
        {
            loaded = LoadText(path, requireText: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loaded = null;
        }

        IsDirty = false;
        if (loaded is null)
        {
            ClearText();
            Kind = QuickViewKind.Info;
            return;
        }

        ApplyLoaded(loaded, force: true);
    }

    /// <summary>Chiamato dalla vista quando il testo nell'editor cambia (o torna all'originale con Annulla).</summary>
    public void SetModified(bool modified)
    {
        if (Kind != QuickViewKind.Text || !IsEditable)
        {
            return;
        }

        IsDirty = modified;
        UpdateStatus();
    }

    /// <summary>Prima di chiudere: chiede se salvare. False se il salvataggio non è riuscito.</summary>
    public bool ResolvePendingChanges()
    {
        if (!IsDirty || FilePath is not { } path)
        {
            return true;
        }

        var ok = Host?.AskSaveChanges(Path.GetFileName(path)) != true || Save();
        IsDirty = false;
        return ok;
    }

    private void SetHeader(FileItemViewModel item)
    {
        Title = item.Name;
        Details = item.IsDirectory
            ? item.TypeName + "\n" + Strings.Format(Strings.LastModified, item.ModifiedText)
            : $"{item.TypeName} · {item.SizeText}\n" + Strings.Format(Strings.LastModified, item.ModifiedText);
    }

    private void ClearText()
    {
        Text = null;
        _encoding = null;
        IsEditable = false;
        IsDirty = false;
        TextStatus = string.Empty;
    }

    private void ApplyLoaded(LoadedText loaded, bool force = false)
    {
        var changed = force || !string.Equals(Text, loaded.Content.Text, StringComparison.Ordinal) || _encoding is null;
        _encoding = loaded.Content.Encoding;
        _loadedWriteTimeUtc = loaded.WriteTimeUtc;
        _isPartial = !loaded.Content.IsComplete;
        _isReadOnlyFile = loaded.IsReadOnlyFile;
        Text = loaded.Content.IsComplete ? loaded.Content.Text : loaded.Content.Text + "\n\n…";
        IsEditable = loaded.Content.IsComplete && !loaded.IsReadOnlyFile;
        IsDirty = false;
        UpdateStatus();
        if (changed)
        {
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateStatus()
    {
        if (Text is null || _encoding is null)
        {
            TextStatus = string.Empty;
            return;
        }

        var status = Strings.Format(Strings.EditorStatus, TextFileCodec.Describe(_encoding), CountLines(Text));
        if (_isPartial)
        {
            status += " · " + Strings.QuickViewPartial;
        }
        else if (_isReadOnlyFile)
        {
            status += " · " + Strings.QuickViewReadOnly;
        }
        else if (IsDirty)
        {
            status += " · " + Strings.QuickViewModified;
        }

        TextStatus = status;
    }

    private bool HasChangedOnDisk(string path)
    {
        try
        {
            return !File.Exists(path) || File.GetLastWriteTimeUtc(path) != _loadedWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int CountLines(string text)
    {
        var lines = 1;
        foreach (var c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    /// <summary>Legge il file intero se è abbastanza piccolo, altrimenti solo l'inizio; null se sembra binario o non leggibile.</summary>
    private static LoadedText? LoadText(string path, bool requireText)
    {
        try
        {
            var info = new FileInfo(path);
            var writeTime = info.LastWriteTimeUtc;
            var readOnly = info.IsReadOnly;
            var content = TextFileCodec.Read(path, info.Length <= MaxEditableBytes ? MaxEditableBytes : MaxPreviewBytes, requireText);
            return content is null ? null : new LoadedText(content, writeTime, readOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record LoadedText(TextFileContent Content, DateTime WriteTimeUtc, bool IsReadOnlyFile);
}
