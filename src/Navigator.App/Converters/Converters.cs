using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Navigator.Presentation.ViewModels;
using Navigator.Shell;

namespace Navigator.App.Converters;

/// <summary>Icona di sistema per file, cartelle, nodi dell'albero, schede e Drop Stack.</summary>
public sealed class IconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var large = parameter as string == "large";
        return value switch
        {
            FileItemViewModel item => ShellIconProvider.GetIcon(item.FullPath, item.IsDirectory, large),
            FolderNodeViewModel { IsGroup: false } node => ShellIconProvider.GetIcon(node.FullPath, true, large, exact: node.IsRoot),
            DriveItemViewModel drive => ShellIconProvider.GetIcon(drive.RootPath, true, large, exact: true),
            FolderNodeViewModel { IsGroup: true } group when Navigator.Core.Utilities.PathHelper.IsThisPc(group.FullPath)
                => ShellIconProvider.GetShellFolderIcon(Navigator.Core.Utilities.PathHelper.ThisPc, large),
            DropStackItem drop => ShellIconProvider.GetIcon(drop.FullPath, Directory.Exists(drop.FullPath), large),
            // Una stringa è un percorso (icona della scheda, della barra indirizzi o della Quick View).
            string path when Navigator.Core.Utilities.PathHelper.IsThisPc(path) => ShellIconProvider.GetShellFolderIcon(Navigator.Core.Utilities.PathHelper.ThisPc, large),
            string path when !string.IsNullOrEmpty(path) => ShellIconProvider.GetIcon(path, Directory.Exists(path), large, exact: true),
            _ => null,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v != Visibility.Visible;
}

/// <summary>null o stringa vuota → Collapsed.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible se il valore (enum) ha il nome indicato nel parametro.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>File nascosti semitrasparenti, come in Esplora file.</summary>
public sealed class HiddenToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.55 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Carica un'immagine per l'anteprima senza bloccare il file su disco.</summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 640;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException or UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formatta il valore con la stringa passata come parametro.</summary>
public sealed class StringFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Format(culture, parameter as string ?? "{0}", value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Segnalibri a file o cartelle che non esistono più: semitrasparenti.</summary>
public sealed class PathExistsToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => 1.0, // cartella virtuale dei segnalibri
            string path when Directory.Exists(path) || File.Exists(path) => 1.0,
            _ => 0.45,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
