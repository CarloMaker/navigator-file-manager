using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Core.Utilities;
using Navigator.Core.Localization;

namespace Navigator.Presentation.ViewModels;

/// <summary>
/// Finestra principale: schede, albero, segnalibri, sessioni, Drop Stack e operazioni sui file.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;
    private readonly IShellService _shell;
    private readonly IFileOperations _operations;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _store;
    private readonly IFolderWatcher _watcher;
    private readonly IUiDispatcher _ui;
    private readonly IKnownFolders _knownFolders;
    private bool _initializing;

    [ObservableProperty]
    private TabViewModel? _selectedTab;

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private bool _restoreLastSession = true;

    [ObservableProperty]
    private FileViewMode _viewMode = FileViewMode.Details;

    [ObservableProperty]
    private bool _syncFolderTree = true;

    [ObservableProperty]
    private string _freeSpaceText = string.Empty;

    [ObservableProperty]
    private bool _isEditingAddress;

    [ObservableProperty]
    private string _addressText = string.Empty;

    [ObservableProperty]
    private BookmarkItemViewModel? _selectedBookmark;

    [ObservableProperty]
    private Session? _selectedSession;

    public MainViewModel(
        IFileSystemService fileSystem,
        IShellService shell,
        IFileOperations operations,
        IClipboardService clipboard,
        IDialogService dialogs,
        ISettingsStore store,
        IFolderWatcher watcher,
        IUiDispatcher ui,
        IKnownFolders knownFolders)
    {
        _fileSystem = fileSystem;
        _shell = shell;
        _operations = operations;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _store = store;
        _watcher = watcher;
        _ui = ui;
        _knownFolders = knownFolders;
        Settings = new AppSettings();
        Layout.QuickView.PropertyChanged += OnQuickViewPanelChanged;
    }

    /// <summary>Richiesta di aprire un file nell'editor integrato (gestita dalla vista).</summary>
    public event EventHandler<string>? EditFileRequested;

    public AppSettings Settings { get; private set; }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public ObservableCollection<FolderNodeViewModel> TreeRoots { get; } = [];

    /// <summary>Radice dell'albero dei segnalibri: segnalibri e cartelle virtuali.</summary>
    public ObservableCollection<BookmarkItemViewModel> Bookmarks { get; } = [];

    public ObservableCollection<Session> Sessions { get; } = [];

    public ObservableCollection<DropStackItem> DropStack { get; } = [];

    public QuickViewViewModel QuickView { get; } = new();

    /// <summary>Lato, posizione, altezza e visibilità dei pannelli.</summary>
    public PanelLayoutViewModel Layout { get; } = new();

    public string DataDirectory => _store.DataDirectory;

    public bool IsPortable => _store.IsPortable;

    /// <summary>Cartella esistente oppure "Questo PC": si apre in una scheda.</summary>
    private bool IsFolder(string path) => PathHelper.IsThisPc(path) || _fileSystem.DirectoryExists(path);

    /// <summary>Cartella in cui incollare o creare; null in "Questo PC", dove non si può scrivere.</summary>
    private string? CurrentFolder => SelectedTab is { IsThisPc: true } ? null : SelectedTab?.CurrentPath ?? _knownFolders.Home;

    // ---- Avvio e chiusura -----------------------------------------------------------------

    public async Task InitializeAsync(IReadOnlyList<string>? startupPaths = null)
    {
        _initializing = true;
        Settings = _store.LoadSettings();
        ShowHidden = Settings.ShowHidden;
        if (Settings.Panels is { Count: > 0 } panels)
        {
            Layout.Apply(panels);
        }
        else
        {
            // Impostazioni salvate prima dei pannelli spostabili.
            Layout.QuickView.IsVisible = Settings.ShowQuickView;
            Layout.Filters.IsVisible = Settings.ShowFilters;
            Layout.DropStack.IsVisible = Settings.ShowDropStack;
        }
        RestoreLastSession = Settings.RestoreLastSession;
        ViewMode = Settings.ViewMode;
        SyncFolderTree = Settings.SyncFolderTree;

        foreach (var node in _store.LoadBookmarks())
        {
            Bookmarks.Add(BookmarkItemViewModel.FromNode(node, null));
        }

        foreach (var session in _store.LoadSessions())
        {
            Sessions.Add(session);
        }

        BuildTree();
        _initializing = false;

        var paths = (startupPaths ?? []).Where(IsFolder).ToList();
        var selected = 0;
        if (paths.Count == 0 && RestoreLastSession && Settings.LastSession is { } last)
        {
            paths = last.TabPaths.Where(IsFolder).ToList();
            selected = last.SelectedIndex;
        }

        if (paths.Count == 0)
        {
            paths.Add(_knownFolders.Home);
        }

        foreach (var path in paths)
        {
            var tab = CreateTab();
            Tabs.Add(tab);
            _ = tab.NavigateToAsync(path);
        }

        SelectedTab = Tabs[Math.Clamp(selected, 0, Tabs.Count - 1)];
        await Task.CompletedTask;
    }

    /// <summary>Salva impostazioni e schede aperte (da chiamare alla chiusura della finestra).</summary>
    public void SaveState()
    {
        Settings.ShowHidden = ShowHidden;
        Settings.Panels = Layout.Capture();
        Settings.ShowQuickView = Layout.QuickView.IsVisible;
        Settings.ShowFilters = Layout.Filters.IsVisible;
        Settings.ShowDropStack = Layout.DropStack.IsVisible;
        Settings.RestoreLastSession = RestoreLastSession;
        Settings.ViewMode = ViewMode;
        Settings.SyncFolderTree = SyncFolderTree;
        Settings.LastSession = CaptureSession(string.Empty);
        _store.SaveSettings(Settings);
        SaveBookmarks();
        _store.SaveSessions(Sessions);

        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }
    }

    private Session CaptureSession(string name) => new(
        name,
        Tabs.Select(t => t.CurrentPath).Where(p => !string.IsNullOrEmpty(p)).ToList(),
        SelectedTab is null ? 0 : Math.Max(0, Tabs.IndexOf(SelectedTab)));

    // ---- Schede -----------------------------------------------------------------------------

    private TabViewModel CreateTab()
    {
        var tab = new TabViewModel(new TabServices(_fileSystem, _shell, _watcher, _ui), () => ShowHidden);
        tab.PropertyChanged += OnTabPropertyChanged;
        return tab;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedTab))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(TabViewModel.CurrentPath):
                UpdateFreeSpace();
                if (!IsEditingAddress)
                {
                    AddressText = PathHelper.ToAddressText(SelectedTab?.CurrentPath);
                }

                SyncTree();
                break;
            case nameof(TabViewModel.SelectedItem):
                UpdateQuickView();
                break;
        }
    }

    partial void OnSelectedTabChanged(TabViewModel? value)
    {
        IsEditingAddress = false;
        AddressText = PathHelper.ToAddressText(value?.CurrentPath);
        UpdateFreeSpace();
        UpdateQuickView();
        SyncTree();
    }

    public async Task<TabViewModel> OpenTabAsync(string path, bool select = true)
    {
        var tab = CreateTab();
        var index = SelectedTab is null ? Tabs.Count : Tabs.IndexOf(SelectedTab) + 1;
        Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
        if (select)
        {
            SelectedTab = tab;
        }

        await tab.NavigateToAsync(path);
        return tab;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task NewTabAsync() => OpenTabAsync(_knownFolders.Home);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task DuplicateTabAsync(TabViewModel? tab) => OpenTabAsync((tab ?? SelectedTab)?.CurrentPath ?? _knownFolders.Home);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CloseTabAsync(TabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null)
        {
            return;
        }

        if (Tabs.Count == 1)
        {
            // L'ultima scheda non si chiude: torna alla cartella utente.
            await tab.NavigateToAsync(_knownFolders.Home);
            return;
        }

        var index = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(tab, SelectedTab);
        tab.PropertyChanged -= OnTabPropertyChanged;
        Tabs.Remove(tab);
        tab.Dispose();

        if (wasSelected)
        {
            SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }
    }

    [RelayCommand]
    private void CloseOtherTabs(TabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null)
        {
            return;
        }

        foreach (var other in Tabs.Where(t => !ReferenceEquals(t, tab)).ToList())
        {
            other.PropertyChanged -= OnTabPropertyChanged;
            Tabs.Remove(other);
            other.Dispose();
        }

        SelectedTab = tab;
    }

    [RelayCommand]
    private void NextTab() => MoveSelection(+1);

    [RelayCommand]
    private void PreviousTab() => MoveSelection(-1);

    private void MoveSelection(int delta)
    {
        if (Tabs.Count == 0 || SelectedTab is null)
        {
            return;
        }

        var index = (Tabs.IndexOf(SelectedTab) + delta + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[index];
    }

    /// <summary>Sposta una scheda (trascinamento nella barra delle schede).</summary>
    public void MoveTab(TabViewModel tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        if (oldIndex < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, Tabs.Count - 1);
        if (oldIndex != newIndex)
        {
            Tabs.Move(oldIndex, newIndex);
            SelectedTab = tab;
        }
    }

    // ---- Barra indirizzi e albero -----------------------------------------------------------

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task NavigateAsync(string? path)
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await SelectedTab.NavigateToAsync(Environment.ExpandEnvironmentVariables(path));
    }

    [RelayCommand]
    private void BeginEditAddress()
    {
        AddressText = PathHelper.ToAddressText(SelectedTab?.CurrentPath);
        IsEditingAddress = true;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CommitAddressAsync()
    {
        // Il testo va letto prima: chiudendo la casella, la perdita del focus lo riporta al percorso attuale.
        var target = AddressText;
        IsEditingAddress = false;
        await NavigateAsync(target);
    }

    [RelayCommand]
    private void CancelEditAddress()
    {
        IsEditingAddress = false;
        AddressText = PathHelper.ToAddressText(SelectedTab?.CurrentPath);
    }

    [RelayCommand]
    public void BuildTree()
    {
        TreeRoots.Clear();
        var quick = _knownFolders.GetQuickAccess()
            .Select(f => new FolderNodeViewModel(f.Name, f.Path, _fileSystem, () => ShowHidden, isRoot: true));
        TreeRoots.Add(FolderNodeViewModel.CreateGroup(Strings.QuickAccess, quick));

        var drives = _fileSystem.GetDrives()
            .Select(d => new FolderNodeViewModel(d.DisplayName, d.RootPath, _fileSystem, () => ShowHidden, isRoot: true));
        TreeRoots.Add(FolderNodeViewModel.CreateGroup(Strings.ThisPC, drives, PathHelper.ThisPc));
    }

    /// <summary>true mentre l'albero viene aperto dal codice: la vista non deve navigare.</summary>
    public bool IsSyncingTree { get; private set; }

    /// <summary>Apre l'albero delle cartelle fino alla cartella della scheda attiva e la seleziona.</summary>
    public void SyncTree()
    {
        var path = SelectedTab?.CurrentPath;
        if (!SyncFolderTree || string.IsNullOrEmpty(path) || SelectedTab?.IsSearchResults == true)
        {
            return;
        }

        var segments = PathHelper.GetSegments(path);
        var thisPc = TreeRoots.LastOrDefault(g => g.IsGroup);
        if (PathHelper.IsThisPc(path))
        {
            if (thisPc is not null)
            {
                IsSyncingTree = true;
                try
                {
                    thisPc.IsSelected = true;
                }
                finally
                {
                    IsSyncingTree = false;
                }
            }

            return;
        }

        var node = segments.Count == 0
            ? null
            : thisPc?.Children.FirstOrDefault(n => PathHelper.AreSame(n.FullPath, segments[0].FullPath));
        if (node is null)
        {
            return;
        }

        IsSyncingTree = true;
        try
        {
            foreach (var segment in segments.Skip(1))
            {
                node.IsExpanded = true;
                var child = node.Children.FirstOrDefault(c => PathHelper.AreSame(c.FullPath, segment.FullPath));
                if (child is null)
                {
                    break;
                }

                node = child;
            }

            node.IsSelected = true;
        }
        finally
        {
            IsSyncingTree = false;
        }
    }

    partial void OnSyncFolderTreeChanged(bool value)
    {
        Settings.SyncFolderTree = value;
        if (value)
        {
            SyncTree();
        }
    }

    [RelayCommand]
    private void SetViewMode(FileViewMode mode) => ViewMode = mode;

    partial void OnViewModeChanged(FileViewMode value) => Settings.ViewMode = value;

    /// <summary>Lingua scelta dal menu; vale dal prossimo avvio. Codice vuoto = lingua di Windows.</summary>
    public void ChangeLanguage(string code)
    {
        Settings.Language = code;
        _store.SaveSettings(Settings);
    }

    private void UpdateFreeSpace()
    {
        var path = SelectedTab?.CurrentPath;
        var drive = string.IsNullOrEmpty(path) || PathHelper.IsThisPc(path) ? null : _fileSystem.GetDriveFor(path);
        FreeSpaceText = drive is { IsReady: true }
            ? Strings.Format(Strings.FreeSpace, drive.DisplayName, PathHelper.FormatSize(drive.FreeSpace), PathHelper.FormatSize(drive.TotalSize))
            : string.Empty;
    }

    private void UpdateQuickView() =>
        _ = QuickView.ShowAsync(Layout.QuickView.IsVisible ? SelectedTab?.SelectedItem : null);

    // ---- Impostazioni di visualizzazione ----------------------------------------------------

    partial void OnShowHiddenChanged(bool value)
    {
        if (_initializing)
        {
            return;
        }

        Settings.ShowHidden = value;
        foreach (var tab in Tabs)
        {
            _ = tab.RefreshAsync();
        }

        BuildTree();
        SyncTree();
    }

    private void OnQuickViewPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.IsVisible))
        {
            UpdateQuickView();
        }
    }

    // ---- Segnalibri -------------------------------------------------------------------------
    // Albero di segnalibri (a cartelle o a file) e cartelle virtuali per organizzarli.

    /// <summary>Tutti i segnalibri e le cartelle virtuali, a qualsiasi profondità.</summary>
    public IEnumerable<BookmarkItemViewModel> AllBookmarks() =>
        Bookmarks.SelectMany(b => new[] { b }.Concat(b.Descendants()));

    private ObservableCollection<BookmarkItemViewModel> ChildrenOf(BookmarkItemViewModel? folder) =>
        folder?.Children ?? Bookmarks;

    private void SaveBookmarks() => _store.SaveBookmarks(Bookmarks.Select(b => b.ToNode()).ToList());

    /// <summary>Dove finiscono i nuovi elementi: dentro la cartella selezionata, subito dopo il segnalibro selezionato, oppure in fondo.</summary>
    private (BookmarkItemViewModel? Folder, int Index) InsertionPoint()
    {
        var selected = SelectedBookmark;
        if (selected is null)
        {
            return (null, Bookmarks.Count);
        }

        if (selected.IsFolder)
        {
            return (selected, selected.Children.Count);
        }

        return (selected.Parent, ChildrenOf(selected.Parent).IndexOf(selected) + 1);
    }

    private void Insert(BookmarkItemViewModel item, BookmarkItemViewModel? folder, int index)
    {
        var list = ChildrenOf(folder);
        item.Parent = folder;
        list.Insert(Math.Clamp(index, 0, list.Count), item);
        if (folder is not null)
        {
            folder.IsExpanded = true;
        }
    }

    partial void OnSelectedBookmarkChanged(BookmarkItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        // L'elemento selezionato dal codice deve essere visibile: si aprono le cartelle che lo contengono.
        for (var p = value.Parent; p is not null; p = p.Parent)
        {
            p.IsExpanded = true;
        }

        value.IsSelected = true;
    }

    /// <summary>Ctrl+D: aggiunge la cartella corrente, chiedendo il nome.</summary>
    [RelayCommand]
    private void AddBookmark()
    {
        var path = SelectedTab?.CurrentPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var (folder, index) = InsertionPoint();
        if (FindIn(ChildrenOf(folder), path) is { } existing)
        {
            SelectedBookmark = existing;
            return;
        }

        var name = _dialogs.Prompt(Strings.AddBookmarkTitle, Strings.BookmarkNamePrompt, PathHelper.GetDisplayName(path));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var bookmark = BookmarkItemViewModel.CreateLink(name.Trim(), path);
        Insert(bookmark, folder, index);
        SelectedBookmark = bookmark;
        SaveBookmarks();
    }

    /// <summary>Aggiunge ai segnalibri i file e le cartelle selezionati; senza selezione, la cartella corrente.</summary>
    [RelayCommand]
    private void AddSelectionToBookmarks()
    {
        var paths = SelectedPaths;
        if (paths.Count == 0)
        {
            AddBookmark();
            return;
        }

        var (folder, index) = InsertionPoint();
        AddBookmarks(paths, folder, index);
    }

    /// <summary>
    /// Aggiunge segnalibri a file o cartelle (anche trascinati da Esplora file) nella cartella virtuale
    /// <paramref name="folder"/> (null = radice), alla posizione <paramref name="index"/> (-1 = in fondo).
    /// Salta i percorsi inesistenti e quelli già presenti nella stessa cartella. Restituisce quanti ne ha aggiunti.
    /// </summary>
    public int AddBookmarks(IEnumerable<string> paths, BookmarkItemViewModel? folder = null, int index = -1)
    {
        if (folder is { IsFolder: false })
        {
            folder = folder.Parent;
        }

        var list = ChildrenOf(folder);
        var insertAt = index < 0 || index > list.Count ? list.Count : index;
        var added = 0;
        foreach (var raw in paths)
        {
            var path = PathHelper.Normalize(raw);
            if (path.Length == 0 || (!_fileSystem.DirectoryExists(path) && !_fileSystem.FileExists(path)))
            {
                continue;
            }

            if (FindIn(list, path) is { } existing)
            {
                SelectedBookmark = existing;
                continue;
            }

            var bookmark = BookmarkItemViewModel.CreateLink(PathHelper.GetDisplayName(path), path);
            Insert(bookmark, folder, insertAt++);
            SelectedBookmark = bookmark;
            added++;
        }

        if (added > 0)
        {
            SaveBookmarks();
        }

        return added;
    }

    /// <summary>Crea una cartella virtuale dentro la cartella selezionata (o accanto al segnalibro selezionato).</summary>
    [RelayCommand]
    private void NewBookmarkFolder()
    {
        var name = _dialogs.Prompt(Strings.NewBookmarkFolderTitle, Strings.FolderNamePrompt, Strings.NewFolder);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var (parent, index) = InsertionPoint();
        var folder = BookmarkItemViewModel.CreateFolder(name.Trim());
        Insert(folder, parent, index);
        SelectedBookmark = folder;
        SaveBookmarks();
    }

    /// <summary>
    /// Sposta un segnalibro o una cartella (trascinamento) dentro <paramref name="folder"/> (null = radice).
    /// <paramref name="insertIndex"/> è la posizione di inserimento contata nella lista di destinazione
    /// com'era prima dello spostamento. Una cartella non può finire dentro sé stessa.
    /// </summary>
    public bool MoveBookmark(BookmarkItemViewModel item, BookmarkItemViewModel? folder, int insertIndex)
    {
        if (folder is { IsFolder: false } || ReferenceEquals(folder, item) || item.IsAncestorOf(folder))
        {
            return false;
        }

        var source = ChildrenOf(item.Parent);
        var target = ChildrenOf(folder);
        var from = source.IndexOf(item);
        if (from < 0)
        {
            return false;
        }

        if (ReferenceEquals(source, target))
        {
            var to = Math.Clamp(insertIndex > from ? insertIndex - 1 : insertIndex, 0, target.Count - 1);
            if (to != from)
            {
                target.Move(from, to);
            }
        }
        else
        {
            source.RemoveAt(from);
            Insert(item, folder, insertIndex);
        }

        SelectedBookmark = item;
        SaveBookmarks();
        return true;
    }

    /// <summary>true se il segnalibro punta a un file (e non a una cartella).</summary>
    public bool IsFileBookmark(BookmarkItemViewModel bookmark) =>
        bookmark.Path is { } path && !_fileSystem.DirectoryExists(path) && _fileSystem.FileExists(path);

    private static BookmarkItemViewModel? FindIn(IEnumerable<BookmarkItemViewModel> list, string path) =>
        list.FirstOrDefault(b => b.Path is not null && PathHelper.AreSame(b.Path, path));

    [RelayCommand]
    private void RenameBookmark(BookmarkItemViewModel? bookmark)
    {
        bookmark ??= SelectedBookmark;
        if (bookmark is null)
        {
            return;
        }

        var title = bookmark.IsFolder ? Strings.RenameFolderTitle : Strings.RenameBookmarkTitle;
        var name = _dialogs.Prompt(title, Strings.NewNamePrompt, bookmark.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            bookmark.Name = name.Trim();
            SaveBookmarks();
        }
    }

    /// <summary>Rimuove un segnalibro, oppure una cartella virtuale con tutto il suo contenuto (dopo conferma).</summary>
    [RelayCommand]
    private void RemoveBookmark(BookmarkItemViewModel? bookmark)
    {
        bookmark ??= SelectedBookmark;
        if (bookmark is null)
        {
            return;
        }

        if (bookmark.IsFolder && bookmark.Children.Count > 0)
        {
            var count = bookmark.Descendants().Count(d => !d.IsFolder);
            var message = Strings.Format(Strings.DeleteFolderConfirm, bookmark.Name, count);
            if (!_dialogs.Confirm(Strings.DeleteFolder, message))
            {
                return;
            }
        }

        if (ChildrenOf(bookmark.Parent).Remove(bookmark))
        {
            if (ReferenceEquals(SelectedBookmark, bookmark) || bookmark.IsAncestorOf(SelectedBookmark))
            {
                SelectedBookmark = null;
            }

            SaveBookmarks();
        }
    }

    [RelayCommand]
    private void MoveBookmarkUp(BookmarkItemViewModel? bookmark) => MoveAmongSiblings(bookmark ?? SelectedBookmark, -1);

    [RelayCommand]
    private void MoveBookmarkDown(BookmarkItemViewModel? bookmark) => MoveAmongSiblings(bookmark ?? SelectedBookmark, +1);

    private void MoveAmongSiblings(BookmarkItemViewModel? bookmark, int delta)
    {
        if (bookmark is null)
        {
            return;
        }

        var list = ChildrenOf(bookmark.Parent);
        var index = list.IndexOf(bookmark);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
        {
            return;
        }

        list.Move(index, target);
        SelectedBookmark = bookmark;
        SaveBookmarks();
    }

    /// <summary>Cartella: la apre nella scheda corrente. File: lo apre con il programma predefinito. Cartella virtuale: la apre o la chiude.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenBookmarkAsync(BookmarkItemViewModel? bookmark)
    {
        bookmark ??= SelectedBookmark;
        if (bookmark is null)
        {
            return;
        }

        if (bookmark.Path is not { } path)
        {
            bookmark.IsExpanded = !bookmark.IsExpanded;
        }
        else if (IsFolder(path))
        {
            await NavigateAsync(path);
        }
        else if (_fileSystem.FileExists(path))
        {
            OpenFile(path);
        }
        else
        {
            ReportMissing(bookmark);
        }
    }

    /// <summary>
    /// Cartella: nuova scheda. File: apre la sua cartella in una nuova scheda e lo seleziona.
    /// Cartella virtuale: apre in schede tutti i segnalibri a cartelle che contiene.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenBookmarkInNewTabAsync(BookmarkItemViewModel? bookmark)
    {
        bookmark ??= SelectedBookmark;
        if (bookmark is null)
        {
            return;
        }

        if (bookmark.Path is not { } path)
        {
            await OpenInTabsAsync(bookmark.Descendants());
        }
        else if (IsFolder(path))
        {
            await OpenTabAsync(path);
        }
        else if (_fileSystem.FileExists(path))
        {
            await RevealAsync(path, newTab: true);
        }
        else
        {
            ReportMissing(bookmark);
        }
    }

    /// <summary>Apre la cartella che contiene l'elemento del segnalibro e lo seleziona.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenBookmarkLocationAsync(BookmarkItemViewModel? bookmark)
    {
        bookmark ??= SelectedBookmark;
        if (bookmark?.Path is not { } path)
        {
            return;
        }

        if (_fileSystem.DirectoryExists(path) || _fileSystem.FileExists(path))
        {
            await RevealAsync(path, newTab: false);
        }
        else
        {
            ReportMissing(bookmark);
        }
    }

    /// <summary>La funzione tipica di CubicExplorer: tutti i segnalibri a cartelle aperti in schede.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenAllBookmarksAsync() => OpenInTabsAsync(AllBookmarks());

    private async Task OpenInTabsAsync(IEnumerable<BookmarkItemViewModel> items)
    {
        var opened = 0;
        foreach (var path in items.Select(b => b.Path).OfType<string>().Where(IsFolder).ToList())
        {
            await OpenTabAsync(path, select: false);
            opened++;
        }

        if (opened > 0)
        {
            SelectedTab = Tabs[^1];
        }
    }

    /// <summary>Apre la cartella superiore di <paramref name="path"/> e ci seleziona l'elemento.</summary>
    private async Task RevealAsync(string path, bool newTab)
    {
        var parent = PathHelper.GetParent(path);
        if (parent is null)
        {
            // Un'unità: non ha una cartella superiore, la apriamo e basta.
            if (newTab)
            {
                await OpenTabAsync(path);
            }
            else
            {
                await NavigateAsync(path);
            }

            return;
        }

        var tab = newTab ? null : SelectedTab;
        if (tab is null)
        {
            tab = await OpenTabAsync(parent);
        }
        else
        {
            await tab.NavigateToAsync(parent);
        }

        tab.SelectByPath(path);
    }

    private void OpenFile(string path)
    {
        try
        {
            _shell.Open(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private void ReportMissing(BookmarkItemViewModel bookmark) =>
        _dialogs.ShowError(Strings.Format(Strings.BookmarkMissing, bookmark.Name, bookmark.Path));

    // ---- Sessioni ---------------------------------------------------------------------------

    [RelayCommand]
    private void SaveSession()
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var name = _dialogs.Prompt(Strings.SaveSession, Strings.SessionNamePrompt, Strings.Format(Strings.SessionDefaultName, Sessions.Count + 1));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        var existing = Sessions.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            if (!_dialogs.Confirm(Strings.SaveSession, Strings.Format(Strings.SessionReplaceConfirm, name)))
            {
                return;
            }

            Sessions[Sessions.IndexOf(existing)] = CaptureSession(name);
        }
        else
        {
            Sessions.Add(CaptureSession(name));
        }

        _store.SaveSessions(Sessions);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadSessionAsync(Session? session)
    {
        session ??= SelectedSession;
        if (session is null)
        {
            return;
        }

        var paths = session.TabPaths.Where(_fileSystem.DirectoryExists).ToList();
        if (paths.Count == 0)
        {
            _dialogs.ShowError(Strings.SessionFoldersMissing);
            return;
        }

        foreach (var tab in Tabs.ToList())
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
            tab.Dispose();
        }

        Tabs.Clear();
        foreach (var path in paths)
        {
            var tab = CreateTab();
            Tabs.Add(tab);
            _ = tab.NavigateToAsync(path);
        }

        SelectedTab = Tabs[Math.Clamp(session.SelectedIndex, 0, Tabs.Count - 1)];
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void DeleteSession(Session? session)
    {
        session ??= SelectedSession;
        if (session is null)
        {
            return;
        }

        if (_dialogs.Confirm(Strings.DeleteSessionTitle, Strings.Format(Strings.DeleteSessionConfirm, session.Name)))
        {
            Sessions.Remove(session);
            _store.SaveSessions(Sessions);
        }
    }

    // ---- Operazioni sui file ----------------------------------------------------------------

    private IReadOnlyList<string> SelectedPaths =>
        SelectedTab?.SelectedItems.Select(i => i.FullPath).ToList() ?? [];

    [RelayCommand]
    private void Copy() => SetClipboard(cut: false);

    [RelayCommand]
    private void Cut() => SetClipboard(cut: true);

    private void SetClipboard(bool cut)
    {
        var paths = SelectedPaths;
        if (paths.Count > 0)
        {
            _clipboard.SetFiles(paths, cut);
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        var paths = SelectedPaths;
        var text = paths.Count > 0 ? string.Join(Environment.NewLine, paths.Select(p => $"\"{p}\"")) : CurrentFolder is { } folder ? $"\"{folder}\"" : null;
        if (text is not null)
        {
            _clipboard.SetText(text);
        }
    }

    [RelayCommand]
    private void Paste()
    {
        if (SelectedTab is null || CurrentFolder is not { } folder || !_clipboard.HasFiles())
        {
            return;
        }

        var (paths, isCut) = _clipboard.GetFiles();
        RunOperation(() =>
        {
            if (isCut)
            {
                _operations.Move(paths, folder);
            }
            else
            {
                _operations.Copy(paths, folder);
            }
        });
    }

    [RelayCommand]
    private void Delete() => DeleteSelection(permanent: false);

    [RelayCommand]
    private void DeletePermanently() => DeleteSelection(permanent: true);

    private void DeleteSelection(bool permanent)
    {
        var paths = SelectedPaths;
        if (paths.Count > 0)
        {
            RunOperation(() => _operations.Delete(paths, permanent));
        }
    }

    [RelayCommand]
    private void Rename()
    {
        var item = SelectedTab?.SelectedItem;
        if (item is null)
        {
            return;
        }

        var name = _dialogs.Prompt(Strings.RenameTitle, Strings.NewNamePrompt, item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name)
        {
            return;
        }

        RunOperation(() => _operations.Rename(item.FullPath, name.Trim()));
    }

    [RelayCommand]
    private void NewFolder()
    {
        if (SelectedTab is null || SelectedTab.IsSearchResults || CurrentFolder is not { } folder)
        {
            return;
        }

        var name = _dialogs.Prompt(Strings.NewFolderTitle, Strings.FolderNamePrompt, Strings.NewFolder);
        if (!string.IsNullOrWhiteSpace(name))
        {
            RunOperation(() => _operations.CreateFolder(folder, name.Trim()));
        }
    }

    [RelayCommand]
    private void Properties()
    {
        var path = SelectedTab?.SelectedItem?.FullPath ?? CurrentFolder;
        if (path is not null)
        {
            _shell.ShowProperties(path);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenInNewTabAsync()
    {
        var folders = SelectedTab?.SelectedItems.Where(i => i.IsDirectory).ToList() ?? [];
        foreach (var folder in folders)
        {
            await OpenTabAsync(folder.FullPath, select: false);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenContainingFolderAsync()
    {
        var item = SelectedTab?.SelectedItem;
        if (item is not null)
        {
            await RevealAsync(item.FullPath, newTab: true);
        }
    }

    [RelayCommand]
    private void EditFile()
    {
        var item = SelectedTab?.SelectedItem;
        if (item is { IsDirectory: false })
        {
            EditFileRequested?.Invoke(this, item.FullPath);
        }
    }

    /// <summary>File trascinati sulla lista, su una cartella o sul Drop Stack.</summary>
    public void HandleDrop(IReadOnlyList<string> paths, string targetFolder, bool move)
    {
        if (paths.Count == 0 || string.IsNullOrEmpty(targetFolder) || PathHelper.IsThisPc(targetFolder))
        {
            return;
        }

        RunOperation(() =>
        {
            if (move)
            {
                _operations.Move(paths, targetFolder);
            }
            else
            {
                _operations.Copy(paths, targetFolder);
            }
        });
    }

    private void RunOperation(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _dialogs.ShowError(ex.Message);
        }

        // Il FolderWatcher aggiorna comunque la lista; questo rende il risultato immediato.
        if (SelectedTab is { } tab)
        {
            _ = tab.RefreshAsync();
        }
    }

    // ---- Drop Stack -------------------------------------------------------------------------

    public void AddToDropStack(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!DropStack.Any(d => string.Equals(d.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                DropStack.Add(new DropStackItem(path));
            }
        }

        Layout.DropStack.IsVisible = true;
    }

    [RelayCommand]
    private void AddSelectionToDropStack() => AddToDropStack(SelectedPaths);

    [RelayCommand]
    private void RemoveFromDropStack(DropStackItem? item)
    {
        if (item is not null)
        {
            DropStack.Remove(item);
        }
    }

    [RelayCommand]
    private void ClearDropStack() => DropStack.Clear();

    [RelayCommand]
    private void DropStackCopyHere()
    {
        var paths = DropStack.Select(d => d.FullPath).ToList();
        if (paths.Count > 0 && CurrentFolder is { } folder)
        {
            RunOperation(() => _operations.Copy(paths, folder));
        }
    }

    [RelayCommand]
    private void DropStackMoveHere()
    {
        var paths = DropStack.Select(d => d.FullPath).ToList();
        if (paths.Count == 0 || CurrentFolder is not { } folder)
        {
            return;
        }

        var ok = false;
        RunOperation(() => ok = _operations.Move(paths, folder));
        if (ok)
        {
            DropStack.Clear();
        }
    }
}
