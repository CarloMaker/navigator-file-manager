using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Navigator.App;

/// <summary>
/// Righe che si compattano da sole: finché le voci entrano nel pannello all'altezza normale
/// l'elenco resta arioso; quando non ci stanno più, <see cref="IsCompactProperty"/> diventa vero
/// e gli stili degli elementi riducono la riga a circa 23 px (vedi i trigger in App.xaml e MainWindow.xaml).
/// La decisione usa il numero di righe visibili e l'altezza normale misurata, non l'estensione attuale
/// dello scroll: così passare da una modalità all'altra non fa oscillare il risultato.
/// </summary>
public static class CompactRows
{
    /// <summary>Attiva il comportamento su un TreeView o un ListBox.</summary>
    public static readonly DependencyProperty AutoProperty = DependencyProperty.RegisterAttached(
        "Auto", typeof(bool), typeof(CompactRows), new PropertyMetadata(false, OnAutoChanged));

    /// <summary>Vero quando le righe sono compatte; si eredita fino agli elementi della lista.</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(CompactRows),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(CompactRows), new PropertyMetadata(null));

    public static bool GetAuto(DependencyObject d) => (bool)d.GetValue(AutoProperty);
    public static void SetAuto(DependencyObject d, bool value) => d.SetValue(AutoProperty, value);
    public static bool GetIsCompact(DependencyObject d) => (bool)d.GetValue(IsCompactProperty);
    public static void SetIsCompact(DependencyObject d, bool value) => d.SetValue(IsCompactProperty, value);

    private sealed class State
    {
        /// <summary>Altezza di una riga in modalità normale, misurata l'ultima volta che non era compatta.</summary>
        public double NormalRowHeight;
        public bool Pending;
        public NotifyCollectionChangedEventHandler? ItemsChanged;
    }

    private static void OnAutoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl ic) return;
        if ((bool)e.NewValue)
        {
            if (ic.GetValue(StateProperty) is State) return;
            var state = new State();
            state.ItemsChanged = (_, _) => Schedule(ic);
            ic.SetValue(StateProperty, state);
            ic.Loaded += OnChanged;
            ic.SizeChanged += OnChanged;
            ic.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged), true);
            ic.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(OnChanged), true);
            ic.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(OnChanged), true);
            ((INotifyCollectionChanged)ic.Items).CollectionChanged += state.ItemsChanged;
            Schedule(ic);
        }
        else if (ic.GetValue(StateProperty) is State state)
        {
            ic.Loaded -= OnChanged;
            ic.SizeChanged -= OnChanged;
            ic.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
            ic.RemoveHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(OnChanged));
            ic.RemoveHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler(OnChanged));
            ((INotifyCollectionChanged)ic.Items).CollectionChanged -= state.ItemsChanged;
            ic.ClearValue(StateProperty);
            ic.ClearValue(IsCompactProperty);
        }
    }

    private static void OnChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsControl ic) Schedule(ic);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Lo scorrimento da solo non cambia nulla: conta solo se cambia il contenuto o lo spazio disponibile.
        if (sender is ItemsControl ic && (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0))
            Schedule(ic);
    }

    /// <summary>Raggruppa più eventi in un solo calcolo, dopo che il layout è stato completato.</summary>
    private static void Schedule(ItemsControl ic)
    {
        if (ic.GetValue(StateProperty) is not State state || state.Pending) return;
        state.Pending = true;
        ic.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            state.Pending = false;
            if (ic.GetValue(StateProperty) == state) Update(ic, state);
        });
    }

    private static void Update(ItemsControl ic, State state)
    {
        if (!ic.IsLoaded || ic.ActualHeight <= 0) return;

        var compact = GetIsCompact(ic);
        int rows = CountRows(ic);
        if (rows == 0)
        {
            if (compact) SetIsCompact(ic, false);
            return;
        }

        // In modalità normale si misura l'altezza reale di una riga (dipende da tema, carattere e DPI).
        if (!compact && MeasureRowHeight(ic) is double measured)
            state.NormalRowHeight = measured;
        if (state.NormalRowHeight <= 0) return; // nessuna riga ancora generata: si riprova al prossimo layout

        double available = ic.ActualHeight
                           - ic.Padding.Top - ic.Padding.Bottom
                           - ic.BorderThickness.Top - ic.BorderThickness.Bottom;
        bool shouldCompact = rows * state.NormalRowHeight > available + 0.5;
        if (shouldCompact != compact) SetIsCompact(ic, shouldCompact);
    }

    /// <summary>Righe visibili: le voci di primo livello più i figli dei nodi aperti.</summary>
    private static int CountRows(ItemsControl ic)
    {
        int n = ic.Items.Count;
        if (ic is TreeView || ic is TreeViewItem)
        {
            for (int i = 0; i < ic.Items.Count; i++)
                if (ic.ItemContainerGenerator.ContainerFromIndex(i) is TreeViewItem { IsExpanded: true } child)
                    n += CountRows(child);
        }
        return n;
    }

    /// <summary>
    /// Altezza media di una riga: somma delle altezze degli elementi di primo livello già generati
    /// (in un albero comprendono i figli aperti) divisa per le righe che coprono.
    /// </summary>
    private static double? MeasureRowHeight(ItemsControl ic)
    {
        double height = 0;
        int covered = 0;
        for (int i = 0; i < ic.Items.Count; i++)
        {
            if (ic.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c || c.ActualHeight <= 0) continue;
            height += c.ActualHeight + c.Margin.Top + c.Margin.Bottom;
            covered += 1 + (c is TreeViewItem { IsExpanded: true } t ? CountRows(t) : 0);
        }
        if (covered == 0) return null;
        double row = height / covered;
        return row is >= 12 and <= 80 ? row : null;
    }
}
