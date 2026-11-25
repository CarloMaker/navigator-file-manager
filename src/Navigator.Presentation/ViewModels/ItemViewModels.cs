using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Core.Utilities;
using Navigator.Core.Localization;

namespace Navigator.Presentation.ViewModels;

/// <summary>Una riga della lista file.</summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(FileEntry entry, string typeName)
    {
        Entry = entry;
        TypeName = typeName;
    }

    public FileEntry Entry { get; }

    public string Name => Entry.Name;

    public string FullPath => Entry.FullPath;

    public bool IsDirectory => Entry.IsDirectory;

    public bool IsHidden => Entry.IsHidden;

    public long Size => Entry.Size;

    public DateTime Modified => Entry.LastWriteTime;

    public string TypeName { get; }

    /// <summary>Stato Git (badge sull'icona); None fuori da un repository.</summary>
    [ObservableProperty]
    private GitItemStatus _gitStatus;

    /// <summary>Dimensione di una cartella calcolata in background (parziale mentre <see cref="IsSizePending"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long? _folderSize;

    /// <summary>Calcolo della dimensione della cartella in attesa o in corso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _isSizePending;

    /// <summary>Dimensione per l'ordinamento: le cartelle non ancora calcolate valgono -1.</summary>
    public long SortSize => IsDirectory ? FolderSize ?? -1 : Size;

    /// <summary>Come in Esplora file: dimensione in KB arrotondata per eccesso. Per le cartelle il totale calcolato.</summary>
    public string SizeText
    {
        get
        {
            if (!IsDirectory)
            {
                return FormatKb(Size);
            }

            if (FolderSize is { } size)
            {
                return IsSizePending ? FormatKb(size) + "…" : FormatKb(size);
            }

            return IsSizePending ? "…" : string.Empty;
        }
    }

    public string ModifiedText => Modified.ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Cartella che contiene l'elemento (utile nei risultati di ricerca).</summary>
    public string Location => PathHelper.GetParent(FullPath) ?? string.Empty;

    private static string FormatKb(long bytes) =>
        string.Create(CultureInfo.CurrentCulture, $"{Math.Ceiling(bytes / 1024.0):N0} KB");
}

/// <summary>Una casella del pannello filtri per estensione.</summary>
public sealed partial class ExtensionFilterViewModel : ObservableObject
{
    private readonly Action _onChanged;

    [ObservableProperty]
    private bool _isChecked = true;

    public ExtensionFilterViewModel(string extension, int count, bool isChecked, Action onChanged)
    {
        Extension = extension;
        Count = count;
        _isChecked = isChecked;
        _onChanged = onChanged;
    }

    public string Extension { get; }

    public int Count { get; }

    public string Display => $"{Extension}  ({Count})";

    /// <summary>Usato per selezionare/deselezionare tutto senza ricalcolare il filtro a ogni casella.</summary>
    internal void SetSilently(bool value)
    {
#pragma warning disable MVVMTK0034 // accesso diretto voluto: niente notifica al filtro
        if (_isChecked != value)
        {
            _isChecked = value;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    partial void OnIsCheckedChanged(bool value) => _onChanged();
}

/// <summary>Un nodo dell'albero cartelle, con caricamento dei figli all'espansione.</summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    private static readonly FolderNodeViewModel Placeholder = new();
    private readonly IFileSystemService? _fileSystem;
    private readonly Func<bool>? _showHidden;
    private bool _loaded;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    private FolderNodeViewModel()
    {
        Name = "…";
        FullPath = string.Empty;
    }

    public FolderNodeViewModel(string name, string fullPath, IFileSystemService fileSystem, Func<bool> showHidden, bool isRoot = false)
    {
        Name = name;
        FullPath = fullPath;
        IsRoot = isRoot;
        _fileSystem = fileSystem;
        _showHidden = showHidden;
        Children.Add(Placeholder);
    }

    /// <summary>
    /// Gruppo dell'albero ("Accesso rapido", "Questo PC"). Con <paramref name="path"/> un clic lo apre
    /// (per "Questo PC" l'elenco delle unità).
    /// </summary>
    public static FolderNodeViewModel CreateGroup(string name, IEnumerable<FolderNodeViewModel> children, string path = "")
    {
        var group = new FolderNodeViewModel { Name = name, FullPath = path, IsGroup = true, _loaded = true };
        foreach (var child in children)
        {
            group.Children.Add(child);
        }

        group.IsExpanded = true;
        return group;
    }

    public string Name { get; private init; }

    public string FullPath { get; private init; }

    public bool IsGroup { get; private init; }

    /// <summary>Cartella di primo livello: usa la sua icona reale (Desktop, Download, unità...).</summary>
    public bool IsRoot { get; private init; }

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded)
        {
            LoadChildren();
        }
    }

    public void LoadChildren()
    {
        if (_fileSystem is null || _showHidden is null)
        {
            return;
        }

        _loaded = true;
        Children.Clear();
        foreach (var dir in _fileSystem.GetSubdirectories(FullPath, _showHidden()))
        {
            Children.Add(new FolderNodeViewModel(dir.Name, dir.FullPath, _fileSystem, _showHidden));
        }
    }
}

/// <summary>Un elemento del Drop Stack.</summary>
public sealed record DropStackItem(string FullPath)
{
    public string Name => PathHelper.GetDisplayName(FullPath);

    public string Location => PathHelper.GetParent(FullPath) ?? string.Empty;
}

/// <summary>Un'unità nella vista "Questo PC": nome, icona e barra dello spazio occupato, come in Esplora file.</summary>
public sealed class DriveItemViewModel
{
    /// <summary>Sopra questa quota di spazio occupato la barra diventa rossa (Esplora file usa il 90%).</summary>
    public const double AlmostFullThreshold = 0.9;

    public DriveItemViewModel(DriveEntry drive)
    {
        Drive = drive;
    }

    public DriveEntry Drive { get; }

    public string RootPath => Drive.RootPath;

    public string Name => Drive.DisplayName;

    public bool IsNetwork => Drive.Type == DriveType.Network;

    /// <summary>false per un lettore senza disco o un'unità di rete scollegata: niente barra.</summary>
    public bool HasSpaceInfo => Drive.IsReady && Drive.TotalSize > 0;

    /// <summary>Quota occupata, da 0 a 1.</summary>
    public double UsedFraction => HasSpaceInfo
        ? Math.Clamp((double)(Drive.TotalSize - Drive.FreeSpace) / Drive.TotalSize, 0, 1)
        : 0;

    public bool IsAlmostFull => HasSpaceInfo && UsedFraction >= AlmostFullThreshold;

    /// <summary>"120 GB disponibili su 476 GB".</summary>
    public string SpaceText => HasSpaceInfo
        ? Strings.Format(Strings.FreeOfTotal, PathHelper.FormatSize(Drive.FreeSpace), PathHelper.FormatSize(Drive.TotalSize))
        : Drive.IsReady ? string.Empty : Strings.DriveNotReady;
}

/// <summary>
/// Un pulsante della barra delle unità sopra l'albero delle cartelle (come in CubicExplorer):
/// icona e lettera dell'unità, evidenziato quando la scheda attiva è su quell'unità.
/// </summary>
public sealed partial class DriveButtonViewModel : ObservableObject
{
    public DriveButtonViewModel(DriveEntry drive)
    {
        Drive = new DriveItemViewModel(drive);
    }

    /// <summary>Usato anche per l'icona (stesso convertitore delle tessere di "Questo PC").</summary>
    public DriveItemViewModel Drive { get; }

    public string RootPath => Drive.RootPath;

    /// <summary>"C" per "C:\".</summary>
    public string Letter => RootPath.TrimEnd('\\', '/').TrimEnd(':');

    /// <summary>"Disco locale (C:)", lo spazio libero e il suggerimento per Ctrl+clic.</summary>
    public string ToolTip
    {
        get
        {
            var lines = new List<string> { Drive.Name };
            if (!string.IsNullOrEmpty(Drive.SpaceText))
            {
                lines.Add(Drive.SpaceText);
            }

            lines.Add(Strings.DriveButtonHint);
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>La scheda attiva mostra una cartella di questa unità.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>true se <paramref name="path"/> sta su questa unità.</summary>
    public bool Contains(string? path) =>
        !string.IsNullOrEmpty(path)
        && path.StartsWith(RootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        && (path.Length == RootPath.TrimEnd('\\').Length || path[RootPath.TrimEnd('\\').Length] is '\\' or '/');
}
