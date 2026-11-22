using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigator.Core.Models;
using Navigator.Core.Localization;

namespace Navigator.Presentation.ViewModels;

/// <summary>Un pannello spostabile: lato, posizione, altezza relativa e visibilità.</summary>
public sealed partial class PanelViewModel : ObservableObject
{
    [ObservableProperty]
    private PanelDock _dock;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private double _weight = 1;

    [ObservableProperty]
    private bool _isVisible = true;

    public PanelViewModel(PanelId id, string title)
    {
        Id = id;
        Title = title;
    }

    public PanelId Id { get; }

    public string Title { get; }
}

/// <summary>
/// Disposizione dei pannelli nei due lati della finestra e sotto la lista dei file, come i pannelli agganciabili di CubicExplorer.
/// La vista ricostruisce i due lati quando scatta <see cref="LayoutChanged"/>.
/// </summary>
public sealed partial class PanelLayoutViewModel : ObservableObject
{
    private const double MinWeight = 0.05;
    private bool _suspendNotifications;

    public PanelLayoutViewModel()
    {
        Bookmarks = new PanelViewModel(PanelId.Bookmarks, Strings.PanelBookmarks);
        Sessions = new PanelViewModel(PanelId.Sessions, Strings.PanelSessions);
        FolderTree = new PanelViewModel(PanelId.FolderTree, Strings.PanelFolders);
        QuickView = new PanelViewModel(PanelId.QuickView, Strings.PanelQuickView);
        Filters = new PanelViewModel(PanelId.Filters, Strings.PanelFilters);
        DropStack = new PanelViewModel(PanelId.DropStack, Strings.PanelDropStack);
        All = [Bookmarks, Sessions, FolderTree, QuickView, Filters, DropStack];

        foreach (var panel in All)
        {
            panel.PropertyChanged += OnPanelPropertyChanged;
        }

        Reset();
    }

    /// <summary>Lato, ordine o visibilità di un pannello sono cambiati: la vista deve ricostruire i lati.</summary>
    public event EventHandler? LayoutChanged;

    public PanelViewModel Bookmarks { get; }

    public PanelViewModel Sessions { get; }

    public PanelViewModel FolderTree { get; }

    public PanelViewModel QuickView { get; }

    public PanelViewModel Filters { get; }

    public PanelViewModel DropStack { get; }

    public IReadOnlyList<PanelViewModel> All { get; }

    public PanelViewModel Get(PanelId id) => All.First(p => p.Id == id);

    /// <summary>Pannelli visibili di un lato, dall'alto in basso.</summary>
    public IReadOnlyList<PanelViewModel> GetVisible(PanelDock dock) =>
        All.Where(p => p.Dock == dock && p.IsVisible).OrderBy(p => p.Order).ToList();

    /// <summary>
    /// Sposta un pannello nel lato indicato, alla posizione <paramref name="index"/> tra i pannelli visibili
    /// (oltre la fine = in fondo). Il pannello diventa visibile.
    /// </summary>
    public void Move(PanelViewModel panel, PanelDock dock, int index)
    {
        Batch(() =>
        {
            var visible = GetVisible(dock).Where(p => !ReferenceEquals(p, panel)).ToList();
            visible.Insert(Math.Clamp(index, 0, visible.Count), panel);

            var hidden = All.Where(p => p.Dock == dock && !p.IsVisible && !ReferenceEquals(p, panel)).OrderBy(p => p.Order);
            var order = 0;
            foreach (var p in visible.Concat(hidden))
            {
                p.Order = order++;
            }

            panel.Dock = dock;
            panel.IsVisible = true;

            // Le zone di provenienza vengono rinumerate senza buchi.
            foreach (var other in Enum.GetValues<PanelDock>().Where(d => d != dock))
            {
                Renumber(other);
            }
        });
    }

    /// <summary>Sposta un pannello in fondo a un'altra zona (lato sinistro, lato destro o sotto la lista).</summary>
    public void MoveTo(PanelViewModel panel, PanelDock dock)
    {
        if (panel.Dock != dock)
        {
            Move(panel, dock, int.MaxValue);
        }
    }

    [RelayCommand]
    public void MoveToOtherSide(PanelViewModel? panel)
    {
        if (panel is null)
        {
            return;
        }

        var target = panel.Dock == PanelDock.Left ? PanelDock.Right : PanelDock.Left;
        Move(panel, target, int.MaxValue);
    }

    [RelayCommand]
    public void MoveUp(PanelViewModel? panel)
    {
        if (panel is null)
        {
            return;
        }

        var index = IndexInDock(panel);
        if (index > 0)
        {
            Move(panel, panel.Dock, index - 1);
        }
    }

    [RelayCommand]
    public void MoveDown(PanelViewModel? panel)
    {
        if (panel is null)
        {
            return;
        }

        var index = IndexInDock(panel);
        if (index >= 0 && index < GetVisible(panel.Dock).Count - 1)
        {
            Move(panel, panel.Dock, index + 1);
        }
    }

    [RelayCommand]
    public void Hide(PanelViewModel? panel)
    {
        if (panel is not null)
        {
            panel.IsVisible = false;
        }
    }

    public bool CanMoveUp(PanelViewModel panel) => IndexInDock(panel) > 0;

    public bool CanMoveDown(PanelViewModel panel)
    {
        var index = IndexInDock(panel);
        return index >= 0 && index < GetVisible(panel.Dock).Count - 1;
    }

    /// <summary>Altezze dopo il trascinamento di un divisore (le altezze reali fanno da pesi relativi).</summary>
    public void SetWeights(IReadOnlyList<(PanelViewModel Panel, double Height)> heights)
    {
        foreach (var (panel, height) in heights)
        {
            panel.Weight = Math.Max(MinWeight, height);
        }
    }

    /// <summary>Disposizione predefinita: segnalibri, sessioni e cartelle a sinistra; anteprima e filtri a destra.</summary>
    [RelayCommand]
    public void Reset()
    {
        Batch(() =>
        {
            Set(Bookmarks, PanelDock.Left, 0, 1.0, true);
            Set(Sessions, PanelDock.Left, 1, 0.6, true);
            Set(FolderTree, PanelDock.Left, 2, 3.0, true);
            Set(QuickView, PanelDock.Right, 0, 3.0, true);
            Set(Filters, PanelDock.Right, 1, 2.0, true);
            Set(DropStack, PanelDock.Right, 2, 1.5, false);
        });
    }

    /// <summary>Ripristina lo stato salvato; i pannelli mancanti restano come nella disposizione predefinita.</summary>
    public void Apply(IEnumerable<PanelState>? states)
    {
        if (states is null)
        {
            return;
        }

        Batch(() =>
        {
            foreach (var state in states)
            {
                var panel = All.FirstOrDefault(p => p.Id == state.Id);
                if (panel is not null)
                {
                    var dock = Enum.IsDefined(state.Dock) ? state.Dock : PanelDock.Right;
                    Set(panel, dock, state.Order, state.Weight, state.IsVisible);
                }
            }

            foreach (var dock in Enum.GetValues<PanelDock>())
            {
                Renumber(dock);
            }
        });
    }

    public List<PanelState> Capture() => All
        .Select(p => new PanelState
        {
            Id = p.Id,
            Dock = p.Dock,
            Order = p.Order,
            Weight = p.Weight,
            IsVisible = p.IsVisible,
        })
        .ToList();

    private int IndexInDock(PanelViewModel panel)
    {
        var visible = GetVisible(panel.Dock);
        for (var i = 0; i < visible.Count; i++)
        {
            if (ReferenceEquals(visible[i], panel))
            {
                return i;
            }
        }

        return -1;
    }

    private void Renumber(PanelDock dock)
    {
        var order = 0;
        foreach (var p in All.Where(p => p.Dock == dock).OrderBy(p => p.Order).ThenBy(p => p.Id))
        {
            p.Order = order++;
        }
    }

    private static void Set(PanelViewModel panel, PanelDock dock, int order, double weight, bool visible)
    {
        panel.Dock = dock;
        panel.Order = order;
        panel.Weight = double.IsFinite(weight) && weight > 0 ? weight : 1;
        panel.IsVisible = visible;
    }

    /// <summary>Più modifiche, una sola notifica alla vista.</summary>
    private void Batch(Action changes)
    {
        _suspendNotifications = true;
        try
        {
            changes();
        }
        finally
        {
            _suspendNotifications = false;
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Il peso cambia trascinando un divisore: la vista è già aggiornata.
        if (_suspendNotifications || e.PropertyName == nameof(PanelViewModel.Weight))
        {
            return;
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
