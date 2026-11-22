using System.Windows;
using System.Windows.Controls;
using Navigator.Presentation.ViewModels;
using Navigator.Shell;

namespace Navigator.App;

/// <summary>
/// Proprietà collegata per le viste a icone: mostra subito l'icona del file, poi la sostituisce
/// con la miniatura quando è pronta. Uso: &lt;Image local:Thumbnail.Item="{Binding}" local:Thumbnail.Size="96" /&gt;.
/// </summary>
public static class Thumbnail
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.RegisterAttached(
        "Item", typeof(FileItemViewModel), typeof(Thumbnail), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size", typeof(int), typeof(Thumbnail), new PropertyMetadata(48, OnChanged));

    public static FileItemViewModel? GetItem(DependencyObject element) => (FileItemViewModel?)element.GetValue(ItemProperty);

    public static void SetItem(DependencyObject element, FileItemViewModel? value) => element.SetValue(ItemProperty, value);

    public static int GetSize(DependencyObject element) => (int)element.GetValue(SizeProperty);

    public static void SetSize(DependencyObject element, int value) => element.SetValue(SizeProperty, value);

    private static async void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
        {
            return;
        }

        var item = GetItem(image);
        var size = GetSize(image);
        if (item is null)
        {
            image.Source = null;
            return;
        }

        if (ShellThumbnailProvider.TryGetCached(item.FullPath, size, out var cached) && cached is not null)
        {
            image.Source = cached;
            return;
        }

        // Subito l'icona grande; la miniatura arriva dopo. I file solo online non vengono letti.
        image.Source = ShellIconProvider.GetIcon(item.FullPath, item.IsDirectory, large: true);
        if (item.Entry.IsCloudOnly)
        {
            return;
        }

        var thumbnail = await ShellThumbnailProvider.GetAsync(item.FullPath, size);

        // Il contenitore può essere stato riciclato per un altro file nel frattempo.
        if (thumbnail is not null && ReferenceEquals(GetItem(image), item) && GetSize(image) == size)
        {
            image.Source = thumbnail;
        }
    }
}

/// <summary>
/// Pannello della barra delle schede: le schede hanno tutte la stessa larghezza (al massimo 240)
/// e si restringono quando non c'è più spazio, come in Esplora file.
/// </summary>
public sealed class TabStripPanel : Panel
{
    private const double MaxTabWidth = 240;
    private const double MinTabWidth = 64;
    private double _tabWidth = MaxTabWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
        {
            return new Size(0, 0);
        }

        _tabWidth = double.IsInfinity(availableSize.Width)
            ? MaxTabWidth
            : Math.Clamp(Math.Floor(availableSize.Width / count), MinTabWidth, MaxTabWidth);

        var height = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_tabWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(_tabWidth * count, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Arrange(new Rect(x, 0, _tabWidth, finalSize.Height));
            x += _tabWidth;
        }

        return finalSize;
    }
}
