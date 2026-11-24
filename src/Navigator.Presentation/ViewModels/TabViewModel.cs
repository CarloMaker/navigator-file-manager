using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Core.Utilities;
using Navigator.Core.Localization;

namespace Navigator.Presentation.ViewModels;

/// <summary>Servizi usati da ogni scheda.</summary>
public sealed record TabServices(
    IFileSystemService FileSystem,
    IShellService Shell,
    IFolderWatcher Watcher,
    IUiDispatcher Ui,
    IGitStatusService? Git = null,
    IFolderSizeService? FolderSizes = null,
    Func<bool>? ShowGitStatus = null,
    Func<bool>? ShowFolderSizes = null);

/// <summary>Colonne ordinabili della lista file.</summary>
public enum SortColumn
{
    Name,
    Modified,
    Type,
    Size,
}

/// <summary>
/// Una scheda: cartella corrente, cronologia, contenuto, filtri e ricerca.
/// </summary>
public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    public const int MaxSearchResults = 20_000;

    private readonly TabServices _services;
    private readonly Func<bool> _showHidden;
    private readonly NavigationHistory _history = new();
    private List<FileItemViewModel> _all = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _gitCts;
    private FolderSizeJob? _sizeJob;
    private Dictionary<string, FileItemViewModel> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dimensioni delle cartelle già calcolate (percorso → data di modifica e dimensione), riusate quando la cartella si aggiorna da sola.</summary>
    private readonly Dictionary<string, (DateTime Modified, long? Size)> _folderSizeCache = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _watch;
    private bool _suspendFilter;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _title = Strings.NewTab;

    [ObservableProperty]
    private IReadOnlyList<FileItemViewModel> _items = [];

    [ObservableProperty]
    private IReadOnlyList<BreadcrumbSegment> _breadcrumbs = [];

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _strictFilter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSearchResults;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    /// <summary>La scheda mostra "Questo PC": l'elenco delle unità al posto della lista dei file.</summary>
    [ObservableProperty]
    private bool _isThisPc;

    /// <summary>Unità locali e rimovibili ("Dispositivi e unità").</summary>
    [ObservableProperty]
    private IReadOnlyList<DriveItemViewModel> _drives = [];

    /// <summary>Unità di rete collegate ("Percorsi di rete").</summary>
    [ObservableProperty]
    private IReadOnlyList<DriveItemViewModel> _networkDrives = [];

    [ObservableProperty]
    private SortColumn _sortBy = SortColumn.Name;

    [ObservableProperty]
    private bool _sortDescending;

    public TabViewModel(TabServices services, Func<bool> showHidden)
    {
        _services = services;
        _showHidden = showHidden;
    }

    public ObservableCollection<ExtensionFilterViewModel> ExtensionFilters { get; } = [];

    public IReadOnlyList<FileItemViewModel> SelectedItems { get; private set; } = [];

    public bool CanGoBack => _history.CanGoBack;

    public bool CanGoForward => _history.CanGoForward;

    public bool CanGoUp => PathHelper.GetNavigationParent(CurrentPath) is not null;

    public bool IsFiltered => Items.Count != _all.Count;

    /// <summary>Chiamato dalla vista quando cambia la selezione multipla.</summary>
    public void SetSelection(IEnumerable<FileItemViewModel> items)
    {
        SelectedItems = items.ToList();
        UpdateStatus();
    }

    /// <summary>Seleziona l'elemento con questo percorso, se è nella lista (es. "Apri percorso file").</summary>
    public void SelectByPath(string path)
    {
        var item = Items.FirstOrDefault(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            SelectedItem = item;
        }
    }

    // ---- Navigazione ----------------------------------------------------------------------

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task NavigateAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await NavigateToAsync(path, record: true);
    }

    public async Task NavigateToAsync(string path, bool record = true)
    {
        path = PathHelper.Normalize(path);

        if (path == PathHelper.ThisPc)
        {
            await ShowThisPcAsync(record);
            return;
        }

        if (!_services.FileSystem.DirectoryExists(path))
        {
            // Un percorso di file scritto nella barra indirizzi lo apre, come in Esplora file.
            if (_services.FileSystem.FileExists(path))
            {
                TryOpen(path);
                return;
            }

            ErrorMessage = Strings.Format(Strings.PathNotFound, path);
            return;
        }

        if (record)
        {
            _history.Push(path);
        }

        IsSearchResults = false;
        SearchText = string.Empty;
        IsThisPc = false;
        Drives = [];
        NetworkDrives = [];
        if (!PathHelper.AreSame(CurrentPath, path))
        {
            CancelBackgroundDetails();
            _folderSizeCache.Clear();
        }

        CurrentPath = path;
        Title = PathHelper.GetDisplayName(path);
        Breadcrumbs = PathHelper.GetBreadcrumbs(path);
        NotifyHistoryChanged();
        StartWatching(path);
        await LoadAsync(keepFilters: false);
    }

    /// <summary>"Questo PC": le unità con lo spazio libero, come in Esplora file.</summary>
    private async Task ShowThisPcAsync(bool record)
    {
        if (record)
        {
            _history.Push(PathHelper.ThisPc);
        }

        _watch?.Dispose();
        _watch = null;
        CancelBackgroundDetails();
        IsSearchResults = false;
        SearchText = string.Empty;
        ErrorMessage = null;
        SetEntries([], keepFilters: false);
        IsThisPc = true;
        CurrentPath = PathHelper.ThisPc;
        Title = Strings.ThisPC;
        Breadcrumbs = PathHelper.GetBreadcrumbs(PathHelper.ThisPc);
        NotifyHistoryChanged();
        await LoadDrivesAsync();
    }

    /// <summary>Rilegge le unità (anche quando se ne collega o scollega una).</summary>
    public async Task LoadDrivesAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        try
        {
            // Un'unità di rete scollegata può far attendere qualche secondo: la lettura va in background.
            var drives = await Task.Run(() => _services.FileSystem.GetDrives(), cts.Token);
            if (cts.IsCancellationRequested || !IsThisPc)
            {
                return;
            }

            var items = drives.Select(d => new DriveItemViewModel(d)).ToList();
            Drives = items.Where(d => !d.IsNetwork).ToList();
            NetworkDrives = items.Where(d => d.IsNetwork).ToList();
            StatusText = items.Count == 1 ? Strings.ItemsOne : string.Format(CultureInfo.CurrentCulture, Strings.ItemsMany, items.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack), AllowConcurrentExecutions = true)]
    private async Task GoBackAsync()
    {
        var path = _history.GoBack();
        if (path is not null)
        {
            await NavigateToAsync(path, record: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward), AllowConcurrentExecutions = true)]
    private async Task GoForwardAsync()
    {
        var path = _history.GoForward();
        if (path is not null)
        {
            await NavigateToAsync(path, record: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoUp), AllowConcurrentExecutions = true)]
    private async Task GoUpAsync()
    {
        var parent = PathHelper.GetNavigationParent(CurrentPath);
        if (parent is not null)
        {
            await NavigateToAsync(parent);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefreshAsync()
    {
        if (IsThisPc)
        {
            await LoadDrivesAsync();
        }
        else if (IsSearchResults)
        {
            await SearchAsync();
        }
        else if (!string.IsNullOrEmpty(CurrentPath))
        {
            CancelBackgroundDetails(); // F5: si ricalcolano anche stato Git e dimensioni delle cartelle
            _folderSizeCache.Clear();
            await LoadAsync(keepFilters: true);
        }
    }

    /// <summary>Doppio clic / Invio: entra nella cartella o apre il file.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task OpenAsync(FileItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            await NavigateToAsync(item.FullPath);
        }
        else
        {
            TryOpen(item.FullPath);
        }
    }

    private void TryOpen(string path)
    {
        try
        {
            _services.Shell.Open(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
    }

    private void StartWatching(string path)
    {
        _watch?.Dispose();
        _watch = _services.Watcher.Watch(path, () => _services.Ui.Post(() =>
        {
            if (!IsSearchResults && PathHelper.AreSame(CurrentPath, path))
            {
                _ = LoadAsync(keepFilters: true);
            }
        }));
    }

    private async Task LoadAsync(bool keepFilters)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var entries = await _services.FileSystem.GetEntriesAsync(CurrentPath, _showHidden(), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                SetEntries(entries, keepFilters);
                StartBackgroundDetails(entries);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = Strings.AccessDenied;
            SetEntries([], keepFilters: false);
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException)
        {
            ErrorMessage = ex.Message;
            SetEntries([], keepFilters: false);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    // ---- Ricerca --------------------------------------------------------------------------

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            await ClearSearchAsync();
            return;
        }

        // In "Questo PC" non c'è una cartella in cui cercare.
        if (string.IsNullOrEmpty(CurrentPath) || IsThisPc)
        {
            return;
        }

        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var root = CurrentPath;
        var includeHidden = _showHidden();
        var results = new List<FileEntry>();

        CancelBackgroundDetails();
        IsLoading = true;
        IsSearchResults = true;
        ErrorMessage = null;
        StatusText = Strings.Searching;
        Title = Strings.Format(Strings.SearchTitle, query);

        try
        {
            await Task.Run(
                () =>
                {
                    foreach (var entry in _services.FileSystem.Search(root, query, includeHidden, cts.Token))
                    {
                        results.Add(entry);
                        if (results.Count >= MaxSearchResults)
                        {
                            break;
                        }

                        if (results.Count % 250 == 0)
                        {
                            var found = results.Count;
                            _services.Ui.Post(() =>
                            {
                                if (ReferenceEquals(_loadCts, cts))
                                {
                                    StatusText = Strings.Format(Strings.SearchingCount, found);
                                }
                            });
                        }
                    }
                },
                cts.Token);

            if (!cts.IsCancellationRequested)
            {
                SetEntries(results, keepFilters: false);
                if (results.Count >= MaxSearchResults)
                {
                    ErrorMessage = Strings.Format(Strings.SearchLimit, MaxSearchResults);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        if (!IsSearchResults)
        {
            return;
        }

        IsSearchResults = false;
        Title = PathHelper.GetDisplayName(CurrentPath);
        await LoadAsync(keepFilters: false);
    }

    // ---- Filtri e ordinamento ---------------------------------------------------------------

    [RelayCommand]
    private void Sort(SortColumn column)
    {
        if (SortBy == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            _suspendFilter = true;
            SortBy = column;
            SortDescending = false;
            _suspendFilter = false;
        }

        ApplyFilterAndSort();
    }

    [RelayCommand]
    private void CheckAllExtensions() => SetAllExtensions(true);

    [RelayCommand]
    private void UncheckAllExtensions() => SetAllExtensions(false);

    [RelayCommand]
    private void ClearFilter()
    {
        _suspendFilter = true;
        FilterText = string.Empty;
        StrictFilter = false;
        foreach (var f in ExtensionFilters)
        {
            f.SetSilently(true);
        }

        _suspendFilter = false;
        ApplyFilterAndSort();
    }

    private void SetAllExtensions(bool value)
    {
        foreach (var f in ExtensionFilters)
        {
            f.SetSilently(value);
        }

        ApplyFilterAndSort();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilterAndSort();

    partial void OnStrictFilterChanged(bool value) => ApplyFilterAndSort();

    partial void OnSortDescendingChanged(bool value) => ApplyFilterAndSort();

    private void SetEntries(IReadOnlyList<FileEntry> entries, bool keepFilters)
    {
        var excluded = keepFilters
            ? ExtensionFilters.Where(f => !f.IsChecked).Select(f => f.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _all = entries
            .Select(e => new FileItemViewModel(e, _services.Shell.GetTypeName(e.FullPath, e.IsDirectory)))
            .ToList();
        _itemsByPath = new Dictionary<string, FileItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _all)
        {
            _itemsByPath.TryAdd(item.FullPath, item);
        }

        ExtensionFilters.Clear();
        var groups = _all
            .Where(i => !i.IsDirectory)
            .GroupBy(i => i.Entry.Extension.Length == 0 ? EntryFilter.NoExtensionKey : i.Entry.Extension, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups)
        {
            ExtensionFilters.Add(new ExtensionFilterViewModel(g.Key, g.Count(), !excluded.Contains(g.Key), ApplyFilterAndSort));
        }

        if (!keepFilters)
        {
            _suspendFilter = true;
            FilterText = string.Empty;
            StrictFilter = false;
            _suspendFilter = false;
        }

        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        if (_suspendFilter)
        {
            return;
        }

        var excluded = ExtensionFilters
            .Where(f => !f.IsChecked)
            .Select(f => f.Extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var predicate = EntryFilter.Build(FilterText, StrictFilter, excluded);

        var previous = SelectedItem?.FullPath;
        var filtered = _all.Where(i => predicate(i.Entry));
        Items = SortItems(filtered).ToList();

        if (previous is not null)
        {
            SelectedItem = Items.FirstOrDefault(i => string.Equals(i.FullPath, previous, StringComparison.OrdinalIgnoreCase));
        }

        UpdateStatus();
    }

    private IEnumerable<FileItemViewModel> SortItems(IEnumerable<FileItemViewModel> source)
    {
        // Come in Esplora file le cartelle restano raggruppate in cima.
        var folders = source.OrderByDescending(i => i.IsDirectory);
        IOrderedEnumerable<FileItemViewModel> ordered = SortBy switch
        {
            SortColumn.Modified => SortDescending ? folders.ThenByDescending(i => i.Modified) : folders.ThenBy(i => i.Modified),
            SortColumn.Type => SortDescending
                ? folders.ThenByDescending(i => i.TypeName, StringComparer.CurrentCultureIgnoreCase)
                : folders.ThenBy(i => i.TypeName, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Size => SortDescending ? folders.ThenByDescending(i => i.SortSize) : folders.ThenBy(i => i.SortSize),
            _ => SortDescending
                ? folders.ThenByDescending(i => i.Name, NaturalStringComparer.Instance)
                : folders.ThenBy(i => i.Name, NaturalStringComparer.Instance),
        };

        return SortBy == SortColumn.Name ? ordered : ordered.ThenBy(i => i.Name, NaturalStringComparer.Instance);
    }

    private void UpdateStatus()
    {
        var culture = CultureInfo.CurrentCulture;
        var text = Items.Count == 1 ? Strings.ItemsOne : string.Format(culture, Strings.ItemsMany, Items.Count);
        if (IsFiltered)
        {
            text += string.Format(culture, Strings.FilteredFrom, _all.Count);
        }

        if (SelectedItems.Count > 0)
        {
            text += SelectedItems.Count == 1 ? Strings.SelectedOne : string.Format(culture, Strings.SelectedMany, SelectedItems.Count);
            var sized = SelectedItems.Where(i => !i.IsDirectory || (i.FolderSize is not null && !i.IsSizePending)).ToList();
            if (sized.Count > 0)
            {
                text += "  " + PathHelper.FormatSize(sized.Sum(i => i.IsDirectory ? i.FolderSize!.Value : i.Size), culture);
            }
        }

        StatusText = text;
        OnPropertyChanged(nameof(IsFiltered));
    }

    // ---- Stato Git e dimensione delle cartelle (in background) ------------------------------

    /// <summary>Calcolo delle dimensioni delle sottocartelle di una cartella, su un thread separato a bassa priorità.</summary>
    private sealed class FolderSizeJob(string folder)
    {
        public string Folder { get; } = folder;

        public CancellationTokenSource Cts { get; } = new();

        public object Lock { get; } = new();

        public Queue<(string Path, DateTime Modified)> Queue { get; } = new();

        public string? Current { get; set; }

        public bool Running { get; set; }
    }

    private void CancelBackgroundDetails()
    {
        _gitCts?.Cancel();
        _gitCts = null;
        _sizeJob?.Cts.Cancel();
        _sizeJob = null;
    }

    /// <summary>
    /// Dopo ogni lettura della cartella: stato Git e dimensioni delle sottocartelle, ognuno su un proprio thread.
    /// Se la cartella si aggiorna da sola (file cambiati), il calcolo delle dimensioni in corso continua.
    /// </summary>
    private void StartBackgroundDetails(IReadOnlyList<FileEntry> entries)
    {
        StartGitStatus(entries);
        StartFolderSizes();
    }

    private void StartGitStatus(IReadOnlyList<FileEntry> entries)
    {
        _gitCts?.Cancel();
        _gitCts = null;
        if (_services.Git is not { } git || _services.ShowGitStatus?.Invoke() == false || _all.Count == 0)
        {
            return;
        }

        var cts = _gitCts = new CancellationTokenSource();
        var token = cts.Token;
        var folder = CurrentPath;
        var items = _all.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

        _ = Task.Run(() =>
        {
            IReadOnlyDictionary<string, GitItemStatus> statuses;
            try
            {
                statuses = git.GetFolderStatus(folder, entries, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return;
            }

            if (statuses.Count == 0 || token.IsCancellationRequested)
            {
                return;
            }

            _services.Ui.Post(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                foreach (var (name, status) in statuses)
                {
                    if (items.TryGetValue(name, out var item))
                    {
                        item.GitStatus = status;
                    }
                }
            });
        }, token);
    }

    private void StartFolderSizes()
    {
        // Nella radice di un'unità o di una condivisione non si calcola: sarebbe come contare tutto il disco.
        if (_services.FolderSizes is not { } sizes || _services.ShowFolderSizes?.Invoke() == false || PathHelper.IsVolumeRoot(CurrentPath))
        {
            _sizeJob?.Cts.Cancel();
            _sizeJob = null;
            return;
        }

        if (_sizeJob is null || !PathHelper.AreSame(_sizeJob.Folder, CurrentPath))
        {
            _sizeJob?.Cts.Cancel();
            _sizeJob = new FolderSizeJob(CurrentPath);
        }

        var job = _sizeJob;

        // Nell'ordine in cui si vedono: prima le cartelle in cima alla lista.
        var folders = Items.Where(i => i.IsDirectory).Concat(_all.Where(i => i.IsDirectory)).Distinct().ToList();
        bool start;
        lock (job.Lock)
        {
            job.Queue.Clear();
            foreach (var item in folders)
            {
                if (_folderSizeCache.TryGetValue(item.FullPath, out var cached) && cached.Modified == item.Modified)
                {
                    item.FolderSize = cached.Size;
                    continue;
                }

                item.IsSizePending = true;
                if (job.Current is null || !PathHelper.AreSame(job.Current, item.FullPath))
                {
                    job.Queue.Enqueue((item.FullPath, item.Modified));
                }
            }

            start = job.Queue.Count > 0 && !job.Running;
            if (start)
            {
                job.Running = true;
            }
        }

        if (start)
        {
            var thread = new Thread(() => RunFolderSizeJob(sizes, job))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "Navigator: dimensione cartelle",
            };
            thread.Start();
        }
    }

    private void RunFolderSizeJob(IFolderSizeService sizes, FolderSizeJob job)
    {
        var token = job.Cts.Token;
        while (true)
        {
            (string Path, DateTime Modified) next;
            lock (job.Lock)
            {
                if (job.Queue.Count == 0 || token.IsCancellationRequested)
                {
                    job.Running = false;
                    job.Current = null;
                    break;
                }

                next = job.Queue.Dequeue();
                job.Current = next.Path;
            }

            var path = next.Path;
            long? size;
            try
            {
                size = sizes.GetSize(path, partial => _services.Ui.Post(() =>
                {
                    if (!token.IsCancellationRequested && _itemsByPath.TryGetValue(path, out var item) && item.IsSizePending)
                    {
                        item.FolderSize = partial;
                    }
                }), token);
            }
            catch (OperationCanceledException)
            {
                lock (job.Lock)
                {
                    job.Running = false;
                    job.Current = null;
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                size = null;
            }

            _services.Ui.Post(() =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _folderSizeCache[path] = (next.Modified, size);
                if (_itemsByPath.TryGetValue(path, out var item))
                {
                    item.FolderSize = size;
                    item.IsSizePending = false;
                    if (SelectedItems.Contains(item))
                    {
                        UpdateStatus();
                    }
                }
            });
        }

        // Ordinate per dimensione: quando tutte sono pronte si riordina una volta sola.
        _services.Ui.Post(() =>
        {
            if (!token.IsCancellationRequested && SortBy == SortColumn.Size)
            {
                ApplyFilterAndSort();
            }
        });
    }

    public void Dispose()
    {
        CancelBackgroundDetails();
        _loadCts?.Cancel();
        _watch?.Dispose();
        _watch = null;
    }
}
