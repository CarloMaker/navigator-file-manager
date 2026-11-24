using Navigator.Core.Models;

namespace Navigator.Core.Services;

/// <summary>Lettura del file system.</summary>
public interface IFileSystemService
{
    Task<IReadOnlyList<FileEntry>> GetEntriesAsync(string path, bool includeHidden, CancellationToken cancellationToken);

    IReadOnlyList<FileEntry> GetSubdirectories(string path, bool includeHidden);

    IReadOnlyList<DriveEntry> GetDrives();

    DriveEntry? GetDriveFor(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>Ricerca ricorsiva per nome. Il pattern accetta * e ?; senza jolly cerca il testo nel nome.</summary>
    IEnumerable<FileEntry> Search(string root, string pattern, bool includeHidden, CancellationToken cancellationToken);
}

/// <summary>Operazioni che modificano i file, con le finestre di avanzamento di Windows.</summary>
public interface IFileOperations
{
    bool Copy(IReadOnlyList<string> sources, string destinationFolder);

    bool Move(IReadOnlyList<string> sources, string destinationFolder);

    /// <summary>Elimina: nel Cestino, oppure definitivamente se <paramref name="permanent"/>.</summary>
    bool Delete(IReadOnlyList<string> paths, bool permanent);

    /// <summary>Rinomina e restituisce il nuovo percorso completo.</summary>
    string Rename(string path, string newName);

    /// <summary>Crea una cartella con nome univoco e ne restituisce il percorso.</summary>
    string CreateFolder(string parentFolder, string baseName);
}

/// <summary>Integrazione con la shell di Windows.</summary>
public interface IShellService
{
    void Open(string path);

    void ShowProperties(string path);

    /// <summary>Mostra il menu contestuale nativo di Esplora file. Le coordinate sono in pixel di schermo.</summary>
    void ShowContextMenu(IntPtr ownerWindow, IReadOnlyList<string> paths, int screenX, int screenY);

    /// <summary>Descrizione del tipo, come nella colonna "Tipo" di Esplora file.</summary>
    string GetTypeName(string path, bool isDirectory);
}

/// <summary>Appunti di Windows per i file (compatibili con Esplora file).</summary>
public interface IClipboardService
{
    void SetFiles(IReadOnlyList<string> paths, bool cut);

    (IReadOnlyList<string> Paths, bool IsCut) GetFiles();

    bool HasFiles();

    void SetText(string text);
}

/// <summary>Finestre di dialogo, astratte per poter testare i ViewModel.</summary>
public interface IDialogService
{
    string? Prompt(string title, string message, string defaultValue);

    bool Confirm(string title, string message);

    void ShowError(string message);
}

/// <summary>Esegue codice sul thread dell'interfaccia.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Notifica le modifiche a una cartella fatte anche da altri programmi.</summary>
public interface IFolderWatcher
{
    IDisposable Watch(string path, Action onChanged);
}

/// <summary>Salvataggio di impostazioni, segnalibri e sessioni.</summary>
public interface ISettingsStore
{
    string DataDirectory { get; }

    bool IsPortable { get; }

    AppSettings LoadSettings();

    void SaveSettings(AppSettings settings);

    IReadOnlyList<BookmarkNode> LoadBookmarks();

    void SaveBookmarks(IEnumerable<BookmarkNode> bookmarks);

    IReadOnlyList<Session> LoadSessions();

    void SaveSessions(IEnumerable<Session> sessions);
}

/// <summary>Cartelle note dell'utente (Desktop, Documenti, Download...).</summary>
public interface IKnownFolders
{
    IReadOnlyList<(string Name, string Path)> GetQuickAccess();

    string Home { get; }
}

/// <summary>Stato Git degli elementi di una cartella, calcolato da Navigator (serve Git per Windows).</summary>
public interface IGitStatusService
{
    /// <summary>
    /// Stato di ogni elemento della cartella, per nome. Vuoto se la cartella non è in un repository
    /// e non contiene repository, o se Git non è installato. Lento: va chiamato in background.
    /// </summary>
    IReadOnlyDictionary<string, GitItemStatus> GetFolderStatus(string folder, IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken);
}

/// <summary>Dimensione totale di una cartella (file in tutte le sottocartelle).</summary>
public interface IFolderSizeService
{
    /// <summary>
    /// Somma le dimensioni dei file. <paramref name="progress"/> riceve di tanto in tanto il totale parziale.
    /// Le cartelle non accessibili vengono saltate; i collegamenti (junction) non si seguono.
    /// </summary>
    /// <returns>Null se la cartella è un collegamento (junction o link simbolico), che non si conta.</returns>
    long? GetSize(string folder, Action<long>? progress, CancellationToken cancellationToken);
}
