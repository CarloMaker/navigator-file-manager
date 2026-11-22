using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Navigator.App.Theming;

/// <summary>
/// Colori di Esplora file di Windows 11, in versione chiara e scura, scelti in base al tema di sistema.
/// Le chiavi "Nav.*" sono usate come DynamicResource in tutta l'interfaccia: cambiando tema in
/// Impostazioni di Windows, la finestra si aggiorna subito.
/// Per ritoccare un colore basta modificare le due tabelle qui sotto.
/// </summary>
public static class ExplorerTheme
{
    private static readonly Dictionary<string, Color> Light = new()
    {
        ["Nav.TitleBar"] = C("#F3F3F3"),           // barra delle schede (Mica chiaro)
        ["Nav.TabActive"] = C("#F9F9F9"),          // scheda attiva = barra indirizzi
        ["Nav.TabHover"] = C("#EAEAEA"),
        ["Nav.Toolbar"] = C("#F9F9F9"),            // barra indirizzi e barra dei comandi
        ["Nav.Content"] = C("#FFFFFF"),            // lista file
        ["Nav.NavPane"] = C("#FFFFFF"),            // riquadro di spostamento
        ["Nav.Divider"] = C("#E5E5E5"),
        ["Nav.DriveBarTrack"] = C("#E6E6E6"),       // barra dello spazio in "Questo PC"
        ["Nav.DriveBarBorder"] = C("#BCBCBC"),
        ["Nav.DriveBarFull"] = C("#C42B1C"),
        ["Nav.Text"] = C("#1A1A1A"),
        ["Nav.TextSecondary"] = C("#5F5F5F"),
        ["Nav.ItemHover"] = C("#E5F3FF"),          // passaggio del mouse sui file
        ["Nav.ItemSelected"] = C("#CCE8FF"),       // file selezionato
        ["Nav.ItemSelectedBorder"] = C("#99D1FF"),
        ["Nav.ItemSelectedHover"] = C("#B9DEFF"),
        ["Nav.NavHover"] = C("#F0F0F0"),
        ["Nav.NavSelected"] = C("#E5E5E5"),
        ["Nav.InputBackground"] = C("#FFFFFF"),
        ["Nav.InputBorder"] = C("#E0E0E0"),
        ["Nav.ButtonHover"] = C("#EAEAEA"),
        ["Nav.ButtonPressed"] = C("#E0E0E0"),
        ["Nav.CloseHover"] = C("#C42B1C"),
        ["Nav.InfoBarBackground"] = C("#FFF4CE"),
        ["Nav.InfoBarBorder"] = C("#F2D57E"),
        ["Nav.CodeBackground"] = C("#FBFBFB"),
    };

    private static readonly Dictionary<string, Color> Dark = new()
    {
        ["Nav.TitleBar"] = C("#202020"),
        ["Nav.TabActive"] = C("#2C2C2C"),
        ["Nav.TabHover"] = C("#2A2A2A"),
        ["Nav.Toolbar"] = C("#2C2C2C"),
        ["Nav.Content"] = C("#191919"),
        ["Nav.NavPane"] = C("#191919"),
        ["Nav.Divider"] = C("#333333"),
        ["Nav.DriveBarTrack"] = C("#3A3A3A"),
        ["Nav.DriveBarBorder"] = C("#5A5A5A"),
        ["Nav.DriveBarFull"] = C("#FF99A4"),
        ["Nav.Text"] = C("#FFFFFF"),
        ["Nav.TextSecondary"] = C("#C5C5C5"),
        ["Nav.ItemHover"] = C("#2D2D2D"),
        ["Nav.ItemSelected"] = C("#414141"),
        ["Nav.ItemSelectedBorder"] = C("#5A5A5A"),
        ["Nav.ItemSelectedHover"] = C("#4A4A4A"),
        ["Nav.NavHover"] = C("#2D2D2D"),
        ["Nav.NavSelected"] = C("#383838"),
        ["Nav.InputBackground"] = C("#1F1F1F"),
        ["Nav.InputBorder"] = C("#3A3A3A"),
        ["Nav.ButtonHover"] = C("#383838"),
        ["Nav.ButtonPressed"] = C("#333333"),
        ["Nav.CloseHover"] = C("#C42B1C"),
        ["Nav.InfoBarBackground"] = C("#433519"),
        ["Nav.InfoBarBorder"] = C("#6B5420"),
        ["Nav.CodeBackground"] = C("#1E1E1E"),
    };

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(Application app)
    {
        IsDark = ReadIsDark();
        var palette = IsDark ? Dark : Light;
        foreach (var (key, color) in palette)
        {
            app.Resources[key] = Brush(color);
        }

        var accent = ReadAccentColor() ?? C("#0078D4");
        app.Resources["Nav.Accent"] = Brush(accent);
        app.Resources["Nav.AccentColor"] = accent;

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static bool ReadIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Colore d'accento scelto in Impostazioni → Personalizzazione → Colori.</summary>
    private static Color? ReadAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int raw)
            {
                var v = unchecked((uint)raw); // formato 0xAABBGGRR
                return Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
            }
        }
        catch (System.Security.SecurityException)
        {
        }

        return null;
    }
}
