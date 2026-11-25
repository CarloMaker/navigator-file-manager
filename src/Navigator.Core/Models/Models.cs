using Navigator.Core.Localization;

namespace Navigator.Core.Models;

/// <summary>Un file o una cartella letti dal disco.</summary>
public sealed record FileEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime LastWriteTime,
    FileAttributes Attributes)
{
    /// <summary>Estensione in minuscolo con il punto (".jpg"); vuota per le cartelle.</summary>
    public string Extension => IsDirectory ? string.Empty : System.IO.Path.GetExtension(Name).ToLowerInvariant();

    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;

    public bool IsSystem => (Attributes & FileAttributes.System) != 0;

    /// <summary>File di OneDrive presente solo online: leggerlo ne forza il download.</summary>
    public bool IsCloudOnly => (Attributes & (FileAttributes)0x00400000) != 0 // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
                               || (Attributes & FileAttributes.Offline) != 0;
}

/// <summary>Un'unità disco.</summary>
public sealed record DriveEntry(
    string RootPath,
    string Label,
    long TotalSize,
    long FreeSpace,
    bool IsReady,
    DriveType Type = DriveType.Unknown)
{
    /// <summary>Come in Esplora file: "Dati (D:)", oppure "Disco locale (C:)" se l'unità non ha un'etichetta.</summary>
    public string DisplayName
    {
        get
        {
            var letter = RootPath.TrimEnd('\\');
            var name = !string.IsNullOrWhiteSpace(Label) ? Label : Type switch
            {
                DriveType.Fixed => Strings.LocalDisk,
                DriveType.Removable => Strings.RemovableDrive,
                DriveType.CDRom => Strings.OpticalDrive,
                DriveType.Network => Strings.NetworkDrive,
                _ => null,
            };
            return name is null ? letter : $"{name} ({letter})";
        }
    }
}

/// <summary>
/// Un elemento dei segnalibri salvato in bookmarks.json: un segnalibro (con <see cref="Path"/>, a una cartella
/// o a un file) oppure una cartella virtuale per organizzarli (senza Path, con <see cref="Children"/>).
/// Il vecchio formato, un semplice elenco di { name, path }, si legge senza conversioni.
/// </summary>
public sealed class BookmarkNode
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Percorso del file o della cartella; null per le cartelle virtuali.</summary>
    public string? Path { get; set; }

    /// <summary>Contenuto di una cartella virtuale.</summary>
    public List<BookmarkNode>? Children { get; set; }

    /// <summary>Cartella virtuale aperta o chiusa nell'albero.</summary>
    public bool IsExpanded { get; set; } = true;

    public static BookmarkNode Link(string name, string path) => new() { Name = name, Path = path };

    public static BookmarkNode Folder(string name, params BookmarkNode[] children) =>
        new() { Name = name, Children = [.. children] };
}

/// <summary>Un insieme di schede salvato con un nome.</summary>
public sealed record Session(string Name, IReadOnlyList<string> TabPaths, int SelectedIndex);

/// <summary>Impostazioni dell'applicazione, salvate in settings.json.</summary>
public sealed class AppSettings
{
    public bool ShowHidden { get; set; }
    public bool RestoreLastSession { get; set; } = true;
    public Session? LastSession { get; set; }

    // Usati solo per leggere le impostazioni salvate prima dei pannelli spostabili.
    public bool ShowQuickView { get; set; } = true;
    public bool ShowFilters { get; set; } = true;
    public bool ShowDropStack { get; set; }

    /// <summary>Disposizione dei pannelli: lato, ordine, altezza, visibilità. Null = predefinita.</summary>
    public List<PanelState>? Panels { get; set; }

    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    /// <summary>Lingua dell'interfaccia ("it", "en", "de", "fr", "es"); vuota = lingua di Windows.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Visualizzazione della lista file.</summary>
    public FileViewMode ViewMode { get; set; } = FileViewMode.Details;

    /// <summary>L'albero delle cartelle si apre fino alla cartella mostrata nella scheda.</summary>
    public bool SyncFolderTree { get; set; } = true;

    /// <summary>Barra con le unità (C, D, E...) sopra l'albero delle cartelle, come in CubicExplorer.</summary>
    public bool ShowDriveBar { get; set; } = true;

    /// <summary>Badge di stato Git (aggiornato, modificato, nuovo...) sulle icone della lista.</summary>
    public bool ShowGitStatus { get; set; } = true;

    /// <summary>Dimensione delle cartelle calcolata in background.</summary>
    public bool ShowFolderSizes { get; set; } = true;

    public double LeftPaneWidth { get; set; } = 240;
    public double RightPaneWidth { get; set; } = 280;

    public double BottomPaneHeight { get; set; } = 240;
}

/// <summary>Un segmento della barra indirizzi a breadcrumb.</summary>
public sealed record BreadcrumbSegment(string Name, string FullPath);

/// <summary>I pannelli che si possono spostare tra i due lati della finestra.</summary>
public enum PanelId
{
    Bookmarks,
    Sessions,
    FolderTree,
    QuickView,
    Filters,
    DropStack,
}

/// <summary>Lato della finestra in cui sta un pannello.</summary>
public enum PanelDock
{
    Left,
    Right,

    /// <summary>Sotto la lista dei file, con i pannelli affiancati.</summary>
    Bottom,
}

/// <summary>Stato salvato di un pannello.</summary>
public sealed class PanelState
{
    public PanelId Id { get; set; }

    public PanelDock Dock { get; set; }

    /// <summary>Posizione dall'alto nel suo lato (0 = primo).</summary>
    public int Order { get; set; }

    /// <summary>Altezza relativa rispetto agli altri pannelli dello stesso lato.</summary>
    public double Weight { get; set; } = 1;

    public bool IsVisible { get; set; } = true;
}

/// <summary>Come vengono mostrati i file: elenco con colonne o icone con miniature.</summary>
public enum FileViewMode
{
    Details,
    MediumIcons,
    LargeIcons,
}

/// <summary>Stato di un file o di una cartella in un repository Git, come gli overlay di TortoiseGit.</summary>
public enum GitItemStatus
{
    /// <summary>Fuori da un repository (nessun badge).</summary>
    None,

    /// <summary>Versionato e senza modifiche.</summary>
    Normal,

    /// <summary>Modificato (per le cartelle: contiene modifiche).</summary>
    Modified,

    /// <summary>Aggiunto all'indice ma non ancora nel commit.</summary>
    Added,

    /// <summary>Non versionato.</summary>
    Untracked,

    /// <summary>Ignorato (.gitignore).</summary>
    Ignored,

    /// <summary>In conflitto dopo un merge.</summary>
    Conflicted,
}
