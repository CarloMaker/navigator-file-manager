using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Navigator.App.Theming;
using Navigator.Core.Models;
using Navigator.Core.Services;
using Navigator.Presentation.ViewModels;
using Navigator.Core.Localization;

namespace Navigator.App;

public partial class MainWindow : Window
{
    private const string TabDragFormat = "Navigator.Tab";
    private const string PanelDragFormat = "Navigator.Panel";
    private const string BookmarkDragFormat = "Navigator.Bookmark";

    private readonly MainViewModel _vm;
    private readonly IShellService _shell;
    private TabViewModel? _observedTab;
    private double _leftPaneWidth = 240;
    private double _rightPaneWidth = 280;
    private double _bottomPaneHeight = 240;

    // Trascinamento dalla lista file
    private Point _dragStart;
    private bool _dragCandidate;
    private ListViewItem? _pendingSelect;

    // Trascinamento delle schede e del Drop Stack
    private Point _tabDragStart;
    private TabViewModel? _dragTab;
    private Point _dropStackDragStart;

    // Trascinamento di pannelli, segnalibri e cartelle dell'albero
    private Point _panelDragStart;
    private PanelViewModel? _dragPanel;
    private Point _bookmarkDragStart;
    private BookmarkItemViewModel? _dragBookmark;
    private Point _treeDragStart;
    private string? _treeDragPath;
    private InsertionAdorner? _insertionAdorner;
    private bool _showingDropTargets;

    // Viste della lista file
    private readonly ViewBase _detailsView;
    private readonly ItemsPanelTemplate _detailsItemsPanel;
    private bool _restartOnClose;
    private DispatcherTimer? _driveRefreshTimer;

    public MainWindow(MainViewModel viewModel, IShellService shell)
    {
        InitializeComponent();
        _vm = viewModel;
        _shell = shell;
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.QuickView.PropertyChanged += OnQuickViewPropertyChanged;
        viewModel.EditFileRequested += (_, path) => OpenEditor(path);
        viewModel.Layout.LayoutChanged += (_, _) => RebuildDocks();

        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => UpdateMaximizeButton();
        Closing += OnClosing;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        ExplorerTheme.Changed += (_, _) => ApplyWindowFrame();

        _detailsView = FileList.View;
        _detailsItemsPanel = FileList.ItemsPanel;
        TitleRow.SizeChanged += (_, _) => UpdateTabStripWidth();
    }

    // ---- Impostazioni della finestra ----------------------------------------------------------

    public void ApplySettings()
    {
        var s = _vm.Settings;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);

        var bounds = new Rect(s.WindowLeft, s.WindowTop, Width, Height);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop) && screen.IntersectsWith(bounds))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _leftPaneWidth = Math.Clamp(s.LeftPaneWidth, 160, 700);
        _rightPaneWidth = Math.Clamp(s.RightPaneWidth, 160, 700);
        _bottomPaneHeight = Math.Clamp(double.IsFinite(s.BottomPaneHeight) ? s.BottomPaneHeight : 240, 100, 1200);
        RebuildDocks();
        ApplyViewMode();
        UpdateMaximizeButton();
        ObserveTab(_vm.SelectedTab);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        foreach (var editor in Application.Current.Windows.OfType<EditorWindow>().ToList())
        {
            if (!editor.ConfirmDiscard())
            {
                e.Cancel = true;
                return;
            }
        }

        var s = _vm.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
        }

        s.LeftPaneWidth = LeftColumn.ActualWidth > 0 ? LeftColumn.ActualWidth : _leftPaneWidth;
        s.RightPaneWidth = RightColumn.ActualWidth > 0 ? RightColumn.ActualWidth : _rightPaneWidth;
        s.BottomPaneHeight = BottomRow.ActualHeight > 0 ? BottomRow.ActualHeight : _bottomPaneHeight;

        MediaPreview.Stop();
        MediaPreview.Close();
        _vm.SaveState();

        if (_restartOnClose && Environment.ProcessPath is { } exe)
        {
            // Cambio di lingua: Navigator si riapre subito con la nuova lingua.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }
    }

    // ---- Barra del titolo personalizzata ----------------------------------------------------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        HwndSource.FromHwnd(handle)?.AddHook(DeviceChangeHook);
        ApplyWindowFrame();
    }

    /// <summary>Angoli arrotondati di Windows 11 e bordo scuro con il tema scuro.</summary>
    private void ApplyWindowFrame()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(handle, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        var dark = ExplorerTheme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    /// <summary>Da ingrandita la finestra occupa l'area di lavoro, senza coprire la barra delle applicazioni.</summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            var monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Un'unità collegata o scollegata (chiavetta, disco esterno, unità di rete): si aggiornano "Questo PC" e l'albero.
    /// Windows manda più messaggi di fila, quindi l'aggiornamento parte poco dopo l'ultimo.
    /// </summary>
    private IntPtr DeviceChangeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DEVICECHANGE = 0x0219;
        const int DBT_DEVICEARRIVAL = 0x8000;
        const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        if (msg == WM_DEVICECHANGE && (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE))
        {
            _driveRefreshTimer ??= CreateDriveRefreshTimer();
            _driveRefreshTimer.Stop();
            _driveRefreshTimer.Start();
        }

        return IntPtr.Zero;
    }

    private DispatcherTimer CreateDriveRefreshTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var tab in _vm.Tabs.Where(t => t.IsThisPc))
            {
                _ = tab.LoadDrivesAsync();
            }

            _vm.BuildTree();
            _vm.SyncTree();
        };
        return timer;
    }

    // ---- Questo PC -----------------------------------------------------------------------------

    private void OnDriveDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: DriveItemViewModel drive } && e.ChangedButton == MouseButton.Left)
        {
            OpenDrive(drive);
            e.Handled = true;
        }
    }

    private void OnDriveListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: DriveItemViewModel drive })
        {
            OpenDrive(drive);
            e.Handled = true;
        }
    }

    /// <summary>Clic destro su un'unità: il menu di Windows (Apri, Formatta, Espelli, Proprietà...).</summary>
    private void OnDriveRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: DriveItemViewModel drive } item)
        {
            return;
        }

        item.IsSelected = true;
        var point = PointToScreen(e.GetPosition(this));
        _shell.ShowContextMenu(new WindowInteropHelper(this).Handle, [drive.RootPath], (int)point.X, (int)point.Y);
        e.Handled = true;
    }

    private void OpenDrive(DriveItemViewModel drive)
    {
        // Ctrl+doppio clic apre l'unità in una nuova scheda.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _ = _vm.OpenTabAsync(drive.RootPath);
        }
        else
        {
            _vm.NavigateCommand.Execute(drive.RootPath);
        }
    }

    private void UpdateMaximizeButton()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? Strings.Restore : Strings.Maximize;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ---- Reazioni al ViewModel --------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsEditingAddress) when _vm.IsEditingAddress:
                Dispatcher.InvokeAsync(
                    () =>
                    {
                        AddressBox.Focus();
                        AddressBox.SelectAll();
                    },
                    DispatcherPriority.Input);
                break;
            case nameof(MainViewModel.ViewMode):
                ApplyViewMode();
                break;
            case nameof(MainViewModel.SelectedTab):
                ObserveTab(_vm.SelectedTab);
                break;
        }
    }

    private void ObserveTab(TabViewModel? tab)
    {
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged -= OnTabPropertyChanged;
        }

        _observedTab = tab;
        if (tab is not null)
        {
            tab.PropertyChanged += OnTabPropertyChanged;
        }

        UpdateLocationColumn();
        UpdateSortHeaders();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.IsSearchResults))
        {
            UpdateLocationColumn();
        }
        else if (e.PropertyName is nameof(TabViewModel.SortBy) or nameof(TabViewModel.SortDescending))
        {
            UpdateSortHeaders();
        }
        else if (e.PropertyName == nameof(TabViewModel.SelectedItem) && _vm.SelectedTab?.SelectedItem is { } item)
        {
            // Es. "Apri percorso" di un segnalibro: l'elemento selezionato deve essere visibile.
            FileList.ScrollIntoView(item);
        }
    }

    /// <summary>Nei risultati di ricerca compare la colonna "Percorso".</summary>
    private void UpdateLocationColumn() =>
        LocationColumn.Width = _vm.SelectedTab?.IsSearchResults == true ? 280 : 0;

    /// <summary>Freccia nell'intestazione della colonna ordinata, come in Esplora file.</summary>
    private void UpdateSortHeaders()
    {
        var tab = _vm.SelectedTab;
        string Header(string text, SortColumn column) =>
            tab is not null && tab.SortBy == column ? text + (tab.SortDescending ? "  \u25BE" : "  \u25B4") : text;

        NameColumn.Header = Header(Strings.ColumnName, SortColumn.Name);
        ModifiedColumn.Header = Header(Strings.ColumnModified, SortColumn.Modified);
        TypeColumn.Header = Header(Strings.ColumnType, SortColumn.Type);
        SizeColumn.Header = Header(Strings.ColumnSize, SortColumn.Size);
    }

    /// <summary>Dettagli (colonne) oppure icone medie o grandi con miniature.</summary>
    private void ApplyViewMode()
    {
        var mode = _vm.ViewMode;
        if (mode == FileViewMode.Details)
        {
            FileList.ItemTemplate = null;
            FileList.ItemContainerStyle = (Style)FindResource("Nav.FileRow");
            FileList.ItemsPanel = _detailsItemsPanel;
            FileList.View = _detailsView;
            ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Auto);
        }
        else
        {
            FileList.View = null;
            FileList.ItemContainerStyle = (Style)FindResource("Nav.FileTile");
            FileList.ItemTemplate = (DataTemplate)FindResource(mode == FileViewMode.LargeIcons ? "Nav.LargeTile" : "Nav.MediumTile");
            FileList.ItemsPanel = (ItemsPanelTemplate)FindResource("Nav.WrapItemsPanel");
            ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
        }
    }

    /// <summary>Le schede si restringono per lasciare sempre spazio al pulsante "+" e a una zona per trascinare la finestra.</summary>
    private void UpdateTabStripWidth()
    {
        const double dragArea = 120;
        var available = TitleRow.ActualWidth - CaptionButtons.ActualWidth - NewTabButton.ActualWidth
                        - NewTabButton.Margin.Left - 8 - dragArea;
        TabStrip.MaxWidth = Math.Max(120, available);
    }

    private void OnQuickViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(QuickViewViewModel.Kind) or nameof(QuickViewViewModel.FilePath)))
        {
            return;
        }

        var quickView = _vm.QuickView;
        if (quickView.Kind == QuickViewKind.Media && quickView.FilePath is { } path)
        {
            if (MediaPreview.Source?.LocalPath != path)
            {
                MediaPreview.Stop();
                MediaPreview.Source = new Uri(path, UriKind.Absolute);
            }
        }
        else if (MediaPreview.Source is not null)
        {
            MediaPreview.Stop();
            MediaPreview.Close();
            MediaPreview.Source = null;
        }
    }

    private void OnMediaPlay(object sender, RoutedEventArgs e) => MediaPreview.Play();

    private void OnMediaPause(object sender, RoutedEventArgs e) => MediaPreview.Pause();

    private void OnMediaStop(object sender, RoutedEventArgs e) => MediaPreview.Stop();

    // ---- Tastiera ---------------------------------------------------------------------------

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if ((ctrl && key == Key.L) || (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.D))
        {
            _vm.BeginEditAddressCommand.Execute(null);
            e.Handled = true;
        }
        else if ((ctrl && key == Key.F) || (Keyboard.Modifiers == ModifierKeys.None && key == Key.F3))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnFileListKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var tab = _vm.SelectedTab;
        if (tab is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            var selected = tab.SelectedItems;
            if (selected.Count > 1 && selected.All(i => !i.IsDirectory))
            {
                foreach (var item in selected)
                {
                    tab.OpenCommand.Execute(item);
                }
            }
            else
            {
                tab.OpenCommand.Execute(tab.SelectedItem);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            tab.GoUpCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---- Barra indirizzi e pulsanti a discesa -----------------------------------------------

    private void OnAddressBlankClick(object sender, MouseButtonEventArgs e)
    {
        _vm.BeginEditAddressCommand.Execute(null);
        e.Handled = true;
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_vm.IsEditingAddress)
        {
            _vm.CancelEditAddressCommand.Execute(null);
        }
    }

    private void OnDropDownButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            ViewDetailsItem.IsChecked = _vm.ViewMode == FileViewMode.Details;
            ViewMediumItem.IsChecked = _vm.ViewMode == FileViewMode.MediumIcons;
            ViewLargeItem.IsChecked = _vm.ViewMode == FileViewMode.LargeIcons;
            menu.DataContext = _vm;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnLanguageMenuOpened(object sender, RoutedEventArgs e)
    {
        foreach (var item in LanguageMenu.Items.OfType<MenuItem>())
        {
            item.IsChecked = string.Equals(item.Tag as string, _vm.Settings.Language, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        var code = (sender as MenuItem)?.Tag as string ?? string.Empty;
        if (string.Equals(code, _vm.Settings.Language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _vm.ChangeLanguage(code);
        if (MessageBox.Show(this, Strings.RestartForLanguage, "Navigator", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _restartOnClose = true;
            Close();
            _restartOnClose = false; // chiusura annullata (es. modifiche non salvate nell'editor)
        }
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        FileList.SelectAll();
        FileList.Focus();
    }

    private async void OnOpenDataFolderClick(object sender, RoutedEventArgs e) =>
        await _vm.OpenTabAsync(_vm.DataDirectory);

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        MessageBox.Show(
            this,
            $"Navigator {version}\n{Strings.AppTagline}\n\n" +
            Strings.Format(Strings.AboutDataFolder, _vm.DataDirectory) + (_vm.IsPortable ? Strings.PortableSuffix : string.Empty),
            Strings.AboutTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ---- Schede -----------------------------------------------------------------------------

    private void OnTabStripMouseDown(object sender, MouseButtonEventArgs e)
    {
        var tab = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as TabViewModel;
        if (tab is null)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            _vm.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && FindAncestor<Button>(e.OriginalSource as DependencyObject) is null)
        {
            _tabDragStart = e.GetPosition(TabStrip);
            _dragTab = tab;
        }
    }

    private void OnTabStripMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _dragTab = null;
            return;
        }

        if (IsBeyondDragThreshold(_tabDragStart, e.GetPosition(TabStrip)))
        {
            var tab = _dragTab;
            _dragTab = null;
            DragDrop.DoDragDrop(TabStrip, new DataObject(TabDragFormat, tab), DragDropEffects.Move);
        }
    }

    private void OnTabStripDragOver(object sender, DragEventArgs e)
    {
        var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as TabViewModel;
        if (e.Data.GetDataPresent(TabDragFormat))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(BookmarkDragFormat))
        {
            // Un segnalibro rilasciato sulla barra delle schede si apre in una nuova scheda.
            e.Effects = DragDropEffects.Link;
        }
        else
        {
            // File trascinati su una scheda: la scheda si apre e i file si possono rilasciare
            // direttamente sulla linguetta (copia o spostamento nella sua cartella).
            if (target is not null && !ReferenceEquals(target, _vm.SelectedTab))
            {
                _vm.SelectedTab = target;
            }

            e.Effects = target is not null && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
                ? ComputeFileDropEffect(e, paths, target.CurrentPath)
                : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnTabStripDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(TabDragFormat) is TabViewModel dragged
            && FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is TabViewModel target)
        {
            _vm.MoveTab(dragged, _vm.Tabs.IndexOf(target));
        }
        else if (e.Data.GetData(BookmarkDragFormat) is BookmarkItemViewModel bookmark)
        {
            _vm.OpenBookmarkInNewTabCommand.Execute(bookmark);
        }
        else if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is TabViewModel tab
                 && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            var effect = ComputeFileDropEffect(e, paths, tab.CurrentPath);
            if (effect != DragDropEffects.None)
            {
                _vm.HandleDrop(paths, tab.CurrentPath, move: effect == DragDropEffects.Move);
            }
        }

        e.Handled = true;
    }

    // ---- Lista file -------------------------------------------------------------------------

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: not null } header)
        {
            return;
        }

        SortColumn? column = ReferenceEquals(header.Column, NameColumn) ? SortColumn.Name
            : ReferenceEquals(header.Column, ModifiedColumn) ? SortColumn.Modified
            : ReferenceEquals(header.Column, TypeColumn) ? SortColumn.Type
            : ReferenceEquals(header.Column, SizeColumn) ? SortColumn.Size
            : null;

        if (column is { } c)
        {
            _vm.SelectedTab?.SortCommand.Execute(c);
        }
    }

    private void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _vm.SelectedTab?.SetSelection(FileList.SelectedItems.OfType<FileItemViewModel>());

    private void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left
            && FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.DataContext is FileItemViewModel item)
        {
            _vm.SelectedTab?.OpenCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnFileListRightClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ScrollBar>(source) is not null || FindAncestor<GridViewColumnHeader>(source) is not null)
        {
            return;
        }

        var container = FindAncestor<ListViewItem>(source);
        if (container is not null)
        {
            if (!container.IsSelected)
            {
                FileList.SelectedItems.Clear();
                container.IsSelected = true;
            }

            var paths = FileList.SelectedItems.OfType<FileItemViewModel>().Select(i => i.FullPath).ToList();
            var point = PointToScreen(e.GetPosition(this));
            _shell.ShowContextMenu(new WindowInteropHelper(this).Handle, paths, (int)point.X, (int)point.Y);
        }
        else
        {
            FileList.SelectedItems.Clear();
            var menu = (ContextMenu)FindResource("FolderBackgroundMenu");
            menu.DataContext = _vm;
            menu.PlacementTarget = FileList;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        e.Handled = true;
    }

    private void OnFileListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FileList);
        _pendingSelect = null;
        var container = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        _dragCandidate = container is not null;

        // Clic su un elemento già selezionato in una selezione multipla: la selezione resta,
        // così si possono trascinare più file insieme (si riduce al rilascio se non si trascina).
        if (container is { IsSelected: true } && e.ClickCount == 1
            && Keyboard.Modifiers == ModifierKeys.None && FileList.SelectedItems.Count > 1)
        {
            _pendingSelect = container;
            FileList.Focus();
            e.Handled = true;
        }
    }

    private void OnFileListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingSelect is not null)
        {
            FileList.SelectedItems.Clear();
            _pendingSelect.IsSelected = true;
            _pendingSelect.Focus();
            _pendingSelect = null;
        }

        _dragCandidate = false;
    }

    private void OnFileListMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed
            || !IsBeyondDragThreshold(_dragStart, e.GetPosition(FileList)))
        {
            return;
        }

        _dragCandidate = false;
        _pendingSelect = null;
        var paths = FileList.SelectedItems.OfType<FileItemViewModel>().Select(i => i.FullPath).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private void OnFileListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDropEffect(e, out _);
        e.Handled = true;
    }

    private void OnFileListDrop(object sender, DragEventArgs e)
    {
        var effect = GetDropEffect(e, out var target);
        if (effect != DragDropEffects.None && target is not null && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _vm.HandleDrop(paths, target, move: effect == DragDropEffects.Move);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Effetto del rilascio come in Esplora file: stessa unità = sposta, altra unità = copia;
    /// Ctrl forza la copia, Maiusc lo spostamento. Su una cartella si rilascia dentro di essa.
    /// </summary>
    private DragDropEffects GetDropEffect(DragEventArgs e, out string? targetFolder)
    {
        targetFolder = null;

        // Un segnalibro trascinato sulla lista non deve spostare o copiare il file a cui punta.
        if (e.Data.GetDataPresent(BookmarkDragFormat) || e.Data.GetDataPresent(PanelDragFormat))
        {
            return DragDropEffects.None;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return DragDropEffects.None;
        }

        var item = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.DataContext as FileItemViewModel;
        targetFolder = item is { IsDirectory: true } ? item.FullPath : _vm.SelectedTab?.CurrentPath;
        if (string.IsNullOrEmpty(targetFolder))
        {
            return DragDropEffects.None;
        }

        return ComputeFileDropEffect(e, paths, targetFolder);
    }

    /// <summary>Copia o spostamento verso <paramref name="target"/>, con le regole di Esplora file.</summary>
    private static DragDropEffects ComputeFileDropEffect(DragEventArgs e, string[] paths, string target)
    {
        // "Questo PC" non è una cartella: lì non si copia niente.
        if (Core.Utilities.PathHelper.IsThisPc(target))
        {
            return DragDropEffects.None;
        }

        // Non si rilascia una cartella dentro sé stessa, né un file nella cartella in cui si trova già.
        if (paths.Any(p => string.Equals(p.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            || paths.All(p => string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            return DragDropEffects.None;
        }

        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
        {
            return DragDropEffects.Copy;
        }

        if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0)
        {
            return DragDropEffects.Move;
        }

        var sameDrive = string.Equals(Path.GetPathRoot(paths[0]), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);
        return sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    // ---- Segnalibri, sessioni, albero -------------------------------------------------------

    private void OnBookmarkSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.SelectedBookmark = e.NewValue as BookmarkItemViewModel;

    private static BookmarkItemViewModel? BookmarkAt(object? source) =>
        FindAncestor<TreeViewItem>(source as DependencyObject)?.DataContext as BookmarkItemViewModel;

    private void OnBookmarkDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Sulle cartelle virtuali il doppio clic le apre e le chiude (comportamento normale dell'albero).
        if (e.ChangedButton == MouseButton.Left && BookmarkAt(e.OriginalSource) is { IsFolder: false } bookmark)
        {
            _vm.OpenBookmarkCommand.Execute(bookmark);
            e.Handled = true;
        }
    }

    private void OnBookmarkMouseDown(object sender, MouseButtonEventArgs e)
    {
        var bookmark = BookmarkAt(e.OriginalSource);
        if (e.ChangedButton == MouseButton.Middle && bookmark is not null)
        {
            // Clic centrale: nuova scheda; su una cartella virtuale, tutti i suoi segnalibri in schede.
            _vm.OpenBookmarkInNewTabCommand.Execute(bookmark);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            _dragBookmark = FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) is null ? bookmark : null;
            _bookmarkDragStart = e.GetPosition(BookmarkTree);
        }
    }

    /// <summary>
    /// Trascinamento di un segnalibro o di una cartella: dentro l'albero lo sposta, sulla barra delle schede lo apre,
    /// in un'altra app o nel Drop Stack passa il file. Verso l'esterno solo copia o collegamento: mai spostamento.
    /// </summary>
    private void OnBookmarkMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBookmark is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _dragBookmark = null;
            return;
        }

        if (!IsBeyondDragThreshold(_bookmarkDragStart, e.GetPosition(BookmarkTree)))
        {
            return;
        }

        var bookmark = _dragBookmark;
        _dragBookmark = null;

        var data = new DataObject();
        data.SetData(BookmarkDragFormat, bookmark);
        if (bookmark.Path is { } path && (File.Exists(path) || Directory.Exists(path)))
        {
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
        }

        try
        {
            DragDrop.DoDragDrop(BookmarkTree, data, DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally
        {
            RemoveInsertionAdorner();
        }
    }

    private void OnBookmarkDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PanelDragFormat))
        {
            return; // lo gestisce il lato della finestra
        }

        var drop = GetBookmarkDrop(e);
        var dragged = e.Data.GetData(BookmarkDragFormat) as BookmarkItemViewModel;
        var valid = dragged is not null
            ? !ReferenceEquals(drop.Folder, dragged) && !dragged.IsAncestorOf(drop.Folder)
            : e.Data.GetDataPresent(DataFormats.FileDrop);

        if (valid)
        {
            ShowInsertionAdorner(drop.Target, drop.Mode);
            e.Effects = DragDropEffects.Link;
        }
        else
        {
            RemoveInsertionAdorner();
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnBookmarkDragLeave(object sender, DragEventArgs e) => RemoveInsertionAdorner();

    private void OnBookmarkDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PanelDragFormat))
        {
            return;
        }

        var drop = GetBookmarkDrop(e);
        RemoveInsertionAdorner();

        if (e.Data.GetData(BookmarkDragFormat) is BookmarkItemViewModel bookmark)
        {
            _vm.MoveBookmark(bookmark, drop.Folder, drop.Index);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            // File e cartelle da Navigator, da Esplora file o da altre app diventano segnalibri.
            _vm.AddBookmarks(paths, drop.Folder, drop.Index);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Dove va l'elemento rilasciato: al centro di una cartella virtuale = dentro; nella metà alta di un
    /// elemento = prima; nella metà bassa = dopo; sullo spazio vuoto = in fondo alla radice.
    /// </summary>
    private (BookmarkItemViewModel? Folder, int Index, UIElement Target, InsertionMode Mode) GetBookmarkDrop(DragEventArgs e)
    {
        var container = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not BookmarkItemViewModel target)
        {
            var lastRoot = _vm.Bookmarks.Count > 0
                ? BookmarkTree.ItemContainerGenerator.ContainerFromItem(_vm.Bookmarks[^1]) as TreeViewItem
                : null;
            return lastRoot is null
                ? (null, _vm.Bookmarks.Count, BookmarkTree, InsertionMode.Above)
                : (null, _vm.Bookmarks.Count, HeaderOf(lastRoot), InsertionMode.Below);
        }

        var header = HeaderOf(container);
        var y = e.GetPosition(header).Y;
        var height = Math.Max(1, header.ActualHeight);
        var siblings = target.Parent?.Children ?? _vm.Bookmarks;
        var index = siblings.IndexOf(target);

        if (target.IsFolder && y > height * 0.25 && y < height * 0.75)
        {
            return (target, target.Children.Count, header, InsertionMode.Into);
        }

        return y < height / 2
            ? (target.Parent, index, header, InsertionMode.Above)
            : (target.Parent, index + 1, header, InsertionMode.Below);
    }

    /// <summary>La riga dell'elemento, senza i figli di una cartella aperta.</summary>
    private static FrameworkElement HeaderOf(TreeViewItem item) =>
        item.Template?.FindName("PART_Header", item) as FrameworkElement ?? item;

    /// <summary>Menu dei segnalibri: le voci dipendono da cosa c'è sotto il mouse.</summary>
    private void OnBookmarkRightClick(object sender, MouseButtonEventArgs e)
    {
        var container = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var item = container?.DataContext as BookmarkItemViewModel;
        if (container is not null)
        {
            container.IsSelected = true;
        }

        var menu = new ContextMenu { PlacementTarget = BookmarkTree, Placement = PlacementMode.MousePoint, FontFamily = (FontFamily)FindResource("Nav.UiFont") };
        if (item is { IsFolder: true })
        {
            menu.Items.Add(CreateMenuItem(Strings.OpenAllInTabs, () => _vm.OpenBookmarkInNewTabCommand.Execute(item)));
            menu.Items.Add(new Separator());
        }
        else if (item is not null)
        {
            menu.Items.Add(CreateMenuItem(Strings.Open, () => _vm.OpenBookmarkCommand.Execute(item)));
            menu.Items.Add(CreateMenuItem(Strings.OpenInNewTab, () => _vm.OpenBookmarkInNewTabCommand.Execute(item)));
            menu.Items.Add(CreateMenuItem(Strings.OpenLocation, () => _vm.OpenBookmarkLocationCommand.Execute(item)));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(CreateMenuItem(Strings.NewFolder, () => _vm.NewBookmarkFolderCommand.Execute(null)));
        menu.Items.Add(CreateMenuItem(Strings.AddCurrentFolder, () => _vm.AddBookmarkCommand.Execute(null)));

        if (item is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(Strings.Rename, () => _vm.RenameBookmarkCommand.Execute(item)));
            menu.Items.Add(CreateMenuItem(Strings.MoveUp, () => _vm.MoveBookmarkUpCommand.Execute(item)));
            menu.Items.Add(CreateMenuItem(Strings.MoveDown, () => _vm.MoveBookmarkDownCommand.Execute(item)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(item.IsFolder ? Strings.DeleteFolder : Strings.Remove, () => _vm.RemoveBookmarkCommand.Execute(item)));
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        var node = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as FolderNodeViewModel;
        _treeDragPath = node is { IsGroup: false } && node.FullPath.Length > 0 ? node.FullPath : null;
        _treeDragStart = e.GetPosition(FolderTree);
    }

    /// <summary>Una cartella dell'albero si può trascinare nei segnalibri, nel Drop Stack, nella lista o in altre app.</summary>
    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (_treeDragPath is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _treeDragPath = null;
            return;
        }

        if (!IsBeyondDragThreshold(_treeDragStart, e.GetPosition(FolderTree)))
        {
            return;
        }

        var path = _treeDragPath;
        _treeDragPath = null;
        DragDrop.DoDragDrop(FolderTree, new DataObject(DataFormats.FileDrop, new[] { path }),
            DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }
    private void OnSessionDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is Session session)
        {
            _vm.LoadSessionCommand.Execute(session);
        }
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // Selezione fatta dal codice per mostrare la cartella aperta: nessuna navigazione.
        if (_vm.IsSyncingTree)
        {
            return;
        }

        if (e.NewValue is FolderNodeViewModel node && node.FullPath.Length > 0
            && !(_vm.SelectedTab is { } tab && Core.Utilities.PathHelper.AreSame(tab.CurrentPath, node.FullPath)))
        {
            _vm.NavigateCommand.Execute(node.FullPath);
        }
    }

    // ---- Pannelli spostabili ----------------------------------------------------------------

    private FrameworkElement GetPanelElement(PanelId id) => id switch
    {
        PanelId.Bookmarks => BookmarksPanel,
        PanelId.Sessions => SessionsPanel,
        PanelId.FolderTree => FolderTreePanel,
        PanelId.QuickView => QuickViewPanel,
        PanelId.Filters => FiltersPanel,
        _ => DropStackPanel,
    };

    private PanelDock DockOf(Grid dock) =>
        ReferenceEquals(dock, LeftDock) ? PanelDock.Left
        : ReferenceEquals(dock, BottomDock) ? PanelDock.Bottom
        : PanelDock.Right;

    /// <summary>Rimette ogni pannello nel suo lato, nell'ordine e con le altezze del layout; i nascosti tornano nel deposito.</summary>
    private void RebuildDocks()
    {
        RemoveInsertionAdorner();
        foreach (var panel in _vm.Layout.All)
        {
            Detach(GetPanelElement(panel.Id));
        }

        foreach (var dock in new[] { LeftDock, RightDock, BottomDock })
        {
            dock.Children.Clear();
            dock.RowDefinitions.Clear();
            dock.ColumnDefinitions.Clear();
        }

        BuildDock(LeftDock, PanelDock.Left);
        BuildDock(RightDock, PanelDock.Right);
        BuildDock(BottomDock, PanelDock.Bottom);

        foreach (var panel in _vm.Layout.All.Where(p => !p.IsVisible))
        {
            PanelStore.Children.Add(GetPanelElement(panel.Id));
        }

        UpdateDockColumns();
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel parent)
        {
            parent.Children.Remove(element);
        }
    }

    /// <summary>
    /// Mette i pannelli di una zona uno sotto l'altro (lati) o affiancati (sotto la lista),
    /// con un divisore trascinabile tra due pannelli vicini.
    /// </summary>
    private void BuildDock(Grid dock, PanelDock side)
    {
        var horizontal = side == PanelDock.Bottom;
        var panels = _vm.Layout.GetVisible(side);
        for (var i = 0; i < panels.Count; i++)
        {
            if (i > 0)
            {
                var slot = AddSlot(dock, horizontal, GridLength.Auto, 0);

                var splitter = new GridSplitter
                {
                    ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    Background = Brushes.Transparent,
                    Focusable = false,
                };
                var line = new System.Windows.Shapes.Rectangle { IsHitTestVisible = false };
                line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Nav.Divider");
                if (horizontal)
                {
                    splitter.Width = 6;
                    splitter.HorizontalAlignment = HorizontalAlignment.Center;
                    splitter.VerticalAlignment = VerticalAlignment.Stretch;
                    splitter.Cursor = Cursors.SizeWE;
                    line.Width = 1;
                    line.Margin = new Thickness(0, 8, 0, 8);
                    line.HorizontalAlignment = HorizontalAlignment.Center;
                }
                else
                {
                    splitter.Height = 6;
                    splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                    splitter.VerticalAlignment = VerticalAlignment.Center;
                    splitter.Cursor = Cursors.SizeNS;
                    line.Height = 1;
                    line.Margin = new Thickness(12, 0, 12, 0);
                    line.VerticalAlignment = VerticalAlignment.Center;
                }

                splitter.DragCompleted += (_, _) => SaveWeights(side);
                PlaceInSlot(splitter, horizontal, slot);
                PlaceInSlot(line, horizontal, slot);
                dock.Children.Add(splitter);
                dock.Children.Add(line);
            }

            var index = AddSlot(dock, horizontal, new GridLength(panels[i].Weight, GridUnitType.Star), horizontal ? 160 : 72);
            var element = GetPanelElement(panels[i].Id);
            PlaceInSlot(element, horizontal, index);
            dock.Children.Add(element);
        }
    }

    private static int AddSlot(Grid dock, bool horizontal, GridLength size, double min)
    {
        if (horizontal)
        {
            dock.ColumnDefinitions.Add(new ColumnDefinition { Width = size, MinWidth = min });
            return dock.ColumnDefinitions.Count - 1;
        }

        dock.RowDefinitions.Add(new RowDefinition { Height = size, MinHeight = min });
        return dock.RowDefinitions.Count - 1;
    }

    private static void PlaceInSlot(UIElement element, bool horizontal, int index)
    {
        Grid.SetRow(element, horizontal ? 0 : index);
        Grid.SetColumn(element, horizontal ? index : 0);
    }

    /// <summary>Dopo il trascinamento di un divisore le altezze reali diventano i nuovi pesi.</summary>
    private void SaveWeights(PanelDock side)
    {
        var heights = _vm.Layout.GetVisible(side)
            .Select(p => (Panel: p, Height: side == PanelDock.Bottom ? GetPanelElement(p.Id).ActualWidth : GetPanelElement(p.Id).ActualHeight))
            .ToList();
        _vm.Layout.SetWeights(heights);
    }

    /// <summary>Un lato senza pannelli visibili sparisce; ricompare con la larghezza che aveva.</summary>
    private void UpdateDockColumns()
    {
        UpdateDockColumn(LeftColumn, LeftSplitter, LeftDivider, _vm.Layout.GetVisible(PanelDock.Left).Count > 0, ref _leftPaneWidth);
        UpdateDockColumn(RightColumn, RightSplitter, RightDivider, _vm.Layout.GetVisible(PanelDock.Right).Count > 0, ref _rightPaneWidth);
        UpdateBottomRow(_vm.Layout.GetVisible(PanelDock.Bottom).Count > 0);
    }

    /// <summary>La zona sotto la lista c'è solo se contiene pannelli; ricompare con l'altezza che aveva.</summary>
    private void UpdateBottomRow(bool visible)
    {
        if (visible)
        {
            if (BottomRow.Height.Value <= 0 || BottomRow.ActualHeight <= 0)
            {
                BottomRow.Height = new GridLength(_bottomPaneHeight);
            }

            BottomRow.MinHeight = 100;
        }
        else
        {
            if (BottomRow.ActualHeight > 0 && !_showingDropTargets)
            {
                _bottomPaneHeight = BottomRow.ActualHeight;
            }

            BottomRow.MinHeight = 0;
            BottomRow.Height = new GridLength(0);
        }

        BottomSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BottomDivider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDockColumn(ColumnDefinition column, UIElement splitter, UIElement divider, bool visible, ref double width)
    {
        if (visible)
        {
            if (column.Width.Value <= 0 || column.ActualWidth <= 0)
            {
                column.Width = new GridLength(width);
            }

            column.MinWidth = 160;
        }
        else
        {
            if (column.ActualWidth > 0 && !_showingDropTargets)
            {
                width = column.ActualWidth;
            }

            column.MinWidth = 0;
            column.Width = new GridLength(0);
        }

        splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        divider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPanelHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        // I pulsanti dell'intestazione restano cliccabili.
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            _dragPanel = null;
            return;
        }

        _dragPanel = GetPanel(sender);
        _panelDragStart = e.GetPosition(this);
    }

    /// <summary>Trascinando l'intestazione il pannello si sposta: sopra o sotto un altro pannello, anche nell'altro lato.</summary>
    private void OnPanelHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPanel is null || e.LeftButton != MouseButtonState.Pressed)
        {
            _dragPanel = null;
            return;
        }

        if (!IsBeyondDragThreshold(_panelDragStart, e.GetPosition(this)))
        {
            return;
        }

        var panel = _dragPanel;
        _dragPanel = null;
        ShowEmptyDockTargets();
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(PanelDragFormat, panel), DragDropEffects.Move);
        }
        finally
        {
            RemoveInsertionAdorner();
            RebuildDocks();
            _showingDropTargets = false;
        }
    }

    /// <summary>Durante il trascinamento di un pannello, una zona vuota si apre per poterlo ricevere.</summary>
    private void ShowEmptyDockTargets()
    {
        foreach (var (dock, side) in new[] { (LeftDock, PanelDock.Left), (RightDock, PanelDock.Right), (BottomDock, PanelDock.Bottom) })
        {
            if (_vm.Layout.GetVisible(side).Count > 0)
            {
                continue;
            }

            _showingDropTargets = true;
            switch (side)
            {
                case PanelDock.Left:
                    LeftColumn.Width = new GridLength(160);
                    break;
                case PanelDock.Right:
                    RightColumn.Width = new GridLength(160);
                    break;
                default:
                    BottomRow.Height = new GridLength(110);
                    BottomDivider.Visibility = Visibility.Visible;
                    break;
            }

            var hint = new TextBlock
            {
                Text = Strings.DropPanelHere,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Nav.TextSecondary");
            dock.Children.Add(hint);
        }
    }

    private void OnDockPreviewDragOver(object sender, DragEventArgs e)
    {
        if (sender is not Grid dock || e.Data.GetData(PanelDragFormat) is not PanelViewModel panel)
        {
            return;
        }

        var (_, target, mode) = GetPanelInsertion(dock, panel, e);
        ShowInsertionAdorner(target ?? dock, mode);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDockPreviewDragLeave(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(PanelDragFormat))
        {
            RemoveInsertionAdorner();
        }
    }

    private void OnDockPreviewDrop(object sender, DragEventArgs e)
    {
        if (sender is not Grid dock || e.Data.GetData(PanelDragFormat) is not PanelViewModel panel)
        {
            return;
        }

        var (index, _, _) = GetPanelInsertion(dock, panel, e);
        RemoveInsertionAdorner();
        _vm.Layout.Move(panel, DockOf(dock), index);
        e.Handled = true;
    }

    /// <summary>
    /// Posizione del pannello trascinato tra gli altri pannelli della zona: prima del primo pannello
    /// di cui il mouse sta nella metà superiore (o sinistra, sotto la lista), altrimenti in fondo.
    /// </summary>
    private (int Index, FrameworkElement? Target, InsertionMode Mode) GetPanelInsertion(Grid dock, PanelViewModel dragged, DragEventArgs e)
    {
        var side = DockOf(dock);
        var horizontal = side == PanelDock.Bottom;
        var before = horizontal ? InsertionMode.Before : InsertionMode.Above;
        var after = horizontal ? InsertionMode.After : InsertionMode.Below;

        var others = _vm.Layout.GetVisible(side).Where(p => !ReferenceEquals(p, dragged)).ToList();
        for (var i = 0; i < others.Count; i++)
        {
            var element = GetPanelElement(others[i].Id);
            var position = e.GetPosition(element);
            if (horizontal ? position.X < element.ActualWidth / 2 : position.Y < element.ActualHeight / 2)
            {
                return (i, element, before);
            }
        }

        return others.Count == 0 ? (0, null, InsertionMode.Above) : (others.Count, GetPanelElement(others[^1].Id), after);
    }

    private PanelViewModel? GetPanel(object sender) =>
        sender is FrameworkElement { Tag: string id } && Enum.TryParse<PanelId>(id, out var panelId)
            ? _vm.Layout.Get(panelId)
            : null;

    private void OnPanelMenuClick(object sender, RoutedEventArgs e)
    {
        if (GetPanel(sender) is { } panel)
        {
            ShowPanelMenu(panel, (UIElement)sender, PlacementMode.Bottom);
        }
    }

    private void OnPanelHeaderRightClick(object sender, MouseButtonEventArgs e)
    {
        if (GetPanel(sender) is { } panel)
        {
            ShowPanelMenu(panel, (UIElement)sender, PlacementMode.MousePoint);
            e.Handled = true;
        }
    }

    private void ShowPanelMenu(PanelViewModel panel, UIElement target, PlacementMode placement)
    {
        var layout = _vm.Layout;
        var menu = new ContextMenu { PlacementTarget = target, Placement = placement, FontFamily = (FontFamily)FindResource("Nav.UiFont") };
        foreach (var (dock, text) in new[] { (PanelDock.Left, Strings.MoveLeft), (PanelDock.Right, Strings.MoveRight), (PanelDock.Bottom, Strings.MoveToBottom) })
        {
            if (panel.Dock != dock)
            {
                menu.Items.Add(CreateMenuItem(text, () => layout.MoveTo(panel, dock)));
            }
        }

        menu.Items.Add(new Separator());
        var sideBySide = panel.Dock == PanelDock.Bottom;
        menu.Items.Add(CreateMenuItem(sideBySide ? Strings.MoveEarlier : Strings.MoveUp, () => layout.MoveUp(panel), layout.CanMoveUp(panel)));
        menu.Items.Add(CreateMenuItem(sideBySide ? Strings.MoveLater : Strings.MoveDown, () => layout.MoveDown(panel), layout.CanMoveDown(panel)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(Strings.HidePanel, () => layout.Hide(panel)));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(Strings.ResetPanels, layout.Reset));
        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    // ---- Linea di inserimento durante i trascinamenti ---------------------------------------

    private void ShowInsertionAdorner(UIElement target, bool below) =>
        ShowInsertionAdorner(target, below ? InsertionMode.Below : InsertionMode.Above);

    private void ShowInsertionAdorner(UIElement target, InsertionMode mode)
    {
        if (_insertionAdorner is not null && ReferenceEquals(_insertionAdorner.AdornedElement, target) && _insertionAdorner.Mode == mode)
        {
            return;
        }

        RemoveInsertionAdorner();
        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(target);
        if (layer is null)
        {
            return;
        }

        _insertionAdorner = new InsertionAdorner(target, mode, (Brush)FindResource("Nav.Accent"));
        layer.Add(_insertionAdorner);
    }

    private void RemoveInsertionAdorner()
    {
        if (_insertionAdorner is null)
        {
            return;
        }

        System.Windows.Documents.AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement)?.Remove(_insertionAdorner);
        _insertionAdorner = null;
    }

    // ---- Drop Stack -------------------------------------------------------------------------

    private void OnDropStackDragOver(object sender, DragEventArgs e)
    {
        // Il Drop Stack raccoglie solo riferimenti: i file restano dove sono finché non li copi o sposti.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropStackDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _vm.AddToDropStack(paths);
        }

        e.Handled = true;
    }

    private void OnDropStackMouseDown(object sender, MouseButtonEventArgs e) =>
        _dropStackDragStart = e.GetPosition(DropStackList);

    private void OnDropStackMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !IsBeyondDragThreshold(_dropStackDragStart, e.GetPosition(DropStackList)))
        {
            return;
        }

        var paths = DropStackList.SelectedItems.OfType<DropStackItem>().Select(d => d.FullPath).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length > 0)
        {
            DragDrop.DoDragDrop(DropStackList, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move);
        }
    }

    private void OnDropStackRemoveClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in DropStackList.SelectedItems.OfType<DropStackItem>().ToList())
        {
            _vm.RemoveFromDropStackCommand.Execute(item);
        }
    }

    // ---- Editor -----------------------------------------------------------------------------

    private void OpenEditor(string path)
    {
        const long maxSize = 20L * 1024 * 1024;
        if (new FileInfo(path).Length > maxSize)
        {
            MessageBox.Show(this, Strings.FileTooLargeForEditor, "Navigator",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new EditorWindow(path).Show();
    }

    // ---- Utilità ----------------------------------------------------------------------------

    private static bool IsBeyondDragThreshold(Point start, Point current) =>
        Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>Risale l'albero visuale (o logico, per gli elementi di testo) fino a un antenato del tipo dato.</summary>
    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }

    // ---- Win32 ------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
