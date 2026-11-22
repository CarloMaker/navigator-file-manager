using System.Text.RegularExpressions;
using Navigator.Core.Models;
using Navigator.Core.Localization;

namespace Navigator.Core.Utilities;

/// <summary>
/// Filtro della lista file, come il pannello filtri di CubicExplorer.
/// <list type="bullet">
/// <item>Filtro normale: il testo deve comparire nel nome (i jolly * e ? sono ammessi ovunque).</item>
/// <item>Filtro rigido: il nome intero deve corrispondere al pattern, es. <c>steve*.jpg</c>.</item>
/// <item>Più pattern separati da <c>;</c> sono in OR.</item>
/// <item>Le estensioni escluse nascondono i file con quell'estensione; le cartelle restano sempre visibili.</item>
/// </list>
/// </summary>
public static class EntryFilter
{
    public static Func<FileEntry, bool> Build(string? text, bool strict, IReadOnlySet<string>? excludedExtensions)
    {
        var patterns = (text ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => ToRegex(p, strict))
            .ToArray();

        var excluded = excludedExtensions ?? new HashSet<string>();

        return entry =>
        {
            if (!entry.IsDirectory && excluded.Count > 0)
            {
                var ext = entry.Extension.Length == 0 ? NoExtensionKey : entry.Extension;
                if (excluded.Contains(ext))
                {
                    return false;
                }
            }

            if (patterns.Length == 0)
            {
                return true;
            }

            foreach (var regex in patterns)
            {
                if (regex.IsMatch(entry.Name))
                {
                    return true;
                }
            }

            return false;
        };
    }

    /// <summary>Chiave usata per i file senza estensione nel pannello filtri.</summary>
    public static string NoExtensionKey => Strings.NoExtension;

    public static Regex ToRegex(string pattern, bool strict)
    {
        var body = Regex.Escape(pattern)
            .Replace(@"\*", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);

        if (strict)
        {
            body = "^" + body + "$";
        }

        return new Regex(body, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
