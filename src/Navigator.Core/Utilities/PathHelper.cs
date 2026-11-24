using System.Globalization;
using Navigator.Core.Models;
using Navigator.Core.Localization;

namespace Navigator.Core.Utilities;

public static class PathHelper
{
    /// <summary>
    /// Percorso virtuale di "Questo PC" (l'elenco delle unità). È anche il nome shell che Windows riconosce,
    /// quindi resta valido se finisce in un comando di sistema.
    /// </summary>
    public const string ThisPc = "shell:MyComputerFolder";

    private const string ThisPcClsid = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

    /// <summary>true per "Questo PC" scritto in uno qualsiasi dei modi accettati (anche il nome nella lingua attuale).</summary>
    public static bool IsThisPc(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var p = path.Trim().Trim('"').TrimEnd('\\');
        return string.Equals(p, ThisPc, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, ThisPcClsid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, Strings.ThisPC, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, "This PC", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>true per la radice di un'unità con lettera ("C:\").</summary>
    public static bool IsDriveRoot(string path)
    {
        var p = Normalize(path);
        return p.Length == 3 && p[1] == ':' && p[2] == '\\' && char.IsAsciiLetter(p[0]);
    }

    /// <summary>true per la radice di un'unità ("C:\") o di una condivisione di rete ("\\server\condivisione").</summary>
    public static bool IsVolumeRoot(string path)
    {
        if (IsDriveRoot(path))
        {
            return true;
        }

        var p = Normalize(path).TrimEnd('\\');
        return p.StartsWith(@"\\", StringComparison.Ordinal)
               && p[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length == 2;
    }

    /// <summary>Dove porta il pulsante "Su": la cartella superiore; dalla radice di un'unità, "Questo PC".</summary>
    public static string? GetNavigationParent(string path) =>
        IsThisPc(path) ? null : GetParent(path) ?? (IsDriveRoot(path) ? ThisPc : null);

    /// <summary>Segmenti della barra indirizzi: per i percorsi su un'unità c'è "Questo PC" davanti, come in Esplora file.</summary>
    public static IReadOnlyList<BreadcrumbSegment> GetBreadcrumbs(string path)
    {
        var segments = GetSegments(path);
        if (segments.Count == 0 || IsThisPc(path) || !IsDriveRoot(segments[0].FullPath))
        {
            return segments;
        }

        return [new BreadcrumbSegment(Strings.ThisPC, ThisPc), .. segments];
    }

    /// <summary>Testo per la barra indirizzi: il percorso, oppure il nome di "Questo PC".</summary>
    public static string ToAddressText(string? path) => IsThisPc(path) ? Strings.ThisPC : path ?? string.Empty;

    /// <summary>
    /// Divide un percorso Windows nei segmenti della barra indirizzi.
    /// "C:\Users\Anna" → "C:" (C:\), "Users" (C:\Users), "Anna" (C:\Users\Anna).
    /// "\\server\share\dir" → "\\server\share", "dir".
    /// </summary>
    public static IReadOnlyList<BreadcrumbSegment> GetSegments(string path)
    {
        var result = new List<BreadcrumbSegment>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return result;
        }

        if (IsThisPc(path))
        {
            result.Add(new BreadcrumbSegment(Strings.ThisPC, ThisPc));
            return result;
        }

        var normalized = path.Replace('/', '\\');
        string root;
        string rest;

        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // UNC: \\server\share
            var parts = normalized.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                result.Add(new BreadcrumbSegment(normalized, normalized));
                return result;
            }

            root = $@"\\{parts[0]}\{parts[1]}\";
            result.Add(new BreadcrumbSegment($@"\\{parts[0]}\{parts[1]}", root));
            rest = string.Join('\\', parts.Skip(2));
        }
        else if (normalized.Length >= 2 && normalized[1] == ':')
        {
            root = char.ToUpperInvariant(normalized[0]) + @":\";
            result.Add(new BreadcrumbSegment(root.TrimEnd('\\'), root));
            rest = normalized.Length > 3 ? normalized[3..] : string.Empty;
        }
        else
        {
            result.Add(new BreadcrumbSegment(normalized, normalized));
            return result;
        }

        var current = root;
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.EndsWith('\\') ? current + part : current + "\\" + part;
            result.Add(new BreadcrumbSegment(part, current));
        }

        return result;
    }

    /// <summary>Cartella superiore, o null se il percorso è già una radice.</summary>
    public static string? GetParent(string path)
    {
        // Basato sui segmenti, così funziona allo stesso modo anche nei test su Linux.
        var segments = GetSegments(path);
        return segments.Count <= 1 ? null : segments[^2].FullPath;
    }

    /// <summary>Nome da mostrare per un percorso: nome della cartella, o "C:" per una radice.</summary>
    public static string GetDisplayName(string path)
    {
        var segments = GetSegments(path);
        return segments.Count == 0 ? string.Empty : segments[^1].Name;
    }

    /// <summary>Unisce cartella e nome con il separatore di Windows.</summary>
    public static string Combine(string folder, string name) =>
        folder.EndsWith('\\') ? folder + name : folder + "\\" + name;

    /// <summary>"C:" e "C:\" diventano "C:\"; le virgolette incollate vengono tolte.</summary>
    public static string Normalize(string path)
    {
        var p = path.Trim().Trim('"');
        if (IsThisPc(p))
        {
            return ThisPc;
        }

        if (p.Length == 2 && p[1] == ':')
        {
            p += "\\";
        }

        return p;
    }

    public static bool AreSame(string a, string b) =>
        string.Equals(Normalize(a).TrimEnd('\\'), Normalize(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Formato dimensione come in Esplora file: "12 KB", "3,4 MB".</summary>
    public static string FormatSize(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 0)
        {
            return string.Empty;
        }

        if (bytes < 1024)
        {
            return string.Format(culture, Strings.SizeBytes, bytes);
        }

        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);

        var format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, culture) + " " + units[unit];
    }
}
