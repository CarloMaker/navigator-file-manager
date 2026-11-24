using Navigator.Core.Models;

namespace Navigator.Core.Utilities;

/// <summary>Una riga di <c>git status --porcelain=v1 -z</c>: codici X (indice) e Y (cartella di lavoro) e percorso.</summary>
/// <param name="Path">Relativo alla radice del repository, con "/"; le cartelle intere finiscono con "/".</param>
public readonly record struct GitStatusEntry(char Index, char WorkTree, string Path);

/// <summary>Interpreta l'output di git status e assegna a file e cartelle lo stato da mostrare, come TortoiseGit.</summary>
public static class GitStatusParser
{
    /// <summary>Legge l'output di <c>git status --porcelain=v1 -z</c>.</summary>
    public static List<GitStatusEntry> Parse(string output)
    {
        var result = new List<GitStatusEntry>();
        var parts = output.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length < 4 || part[2] != ' ')
            {
                continue;
            }

            var x = part[0];
            var y = part[1];
            result.Add(new GitStatusEntry(x, y, part[3..]));

            // Rinominati e copiati: dopo il nuovo percorso c'è quello vecchio, da saltare.
            if (x is 'R' or 'C' || y is 'R' or 'C')
            {
                i++;
            }
        }

        return result;
    }

    /// <summary>Stato di un singolo elemento dai codici XY.</summary>
    public static GitItemStatus ToStatus(char index, char workTree) => (index, workTree) switch
    {
        ('?', '?') => GitItemStatus.Untracked,
        ('!', '!') => GitItemStatus.Ignored,
        ('U', _) or (_, 'U') or ('A', 'A') or ('D', 'D') => GitItemStatus.Conflicted,
        ('A', _) or ('R', _) or ('C', _) => GitItemStatus.Added,
        _ => GitItemStatus.Modified,
    };

    /// <summary>Riepilogo di un intero repository visto dall'esterno: modificato, in conflitto o aggiornato.</summary>
    public static GitItemStatus Summarize(IEnumerable<GitStatusEntry> entries)
    {
        var status = GitItemStatus.Normal;
        foreach (var entry in entries)
        {
            var s = ToStatus(entry.Index, entry.WorkTree);
            if (s is GitItemStatus.Untracked or GitItemStatus.Ignored)
            {
                continue;
            }

            status = Worst(status, s == GitItemStatus.Conflicted ? GitItemStatus.Conflicted : GitItemStatus.Modified);
        }

        return status;
    }

    /// <summary>
    /// Stato degli elementi diretti di una cartella del repository.
    /// I file non nominati da git status sono versionati e senza modifiche. Una cartella prende lo stato
    /// peggiore dei file versionati che contiene; i file non versionati o ignorati al suo interno non la cambiano.
    /// </summary>
    /// <param name="relativeFolder">Cartella relativa alla radice del repository, con "/"; vuota per la radice.</param>
    public static Dictionary<string, GitItemStatus> MapChildren(
        IEnumerable<GitStatusEntry> entries,
        string relativeFolder,
        IEnumerable<(string Name, bool IsDirectory)> children)
    {
        var prefix = relativeFolder.Trim('/');
        if (prefix.Length > 0)
        {
            prefix += "/";
        }

        var found = new Dictionary<string, GitItemStatus>(StringComparer.OrdinalIgnoreCase);
        GitItemStatus? inherited = null;

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var status = ToStatus(entry.Index, entry.WorkTree);

            // Una cartella antenata intera non versionata o ignorata: vale per tutto il contenuto.
            if (path.EndsWith('/') && prefix.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            {
                inherited = status;
                continue;
            }

            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = path[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0 || slash == rest.Length - 1)
            {
                var name = rest.TrimEnd('/');
                found[name] = found.TryGetValue(name, out var current) ? Worst(current, status) : status;
            }
            else if (status is not (GitItemStatus.Untracked or GitItemStatus.Ignored))
            {
                var folder = rest[..slash];
                var folderStatus = status == GitItemStatus.Conflicted ? GitItemStatus.Conflicted : GitItemStatus.Modified;
                found[folder] = found.TryGetValue(folder, out var current) ? Worst(current, folderStatus) : folderStatus;
            }
        }

        var result = new Dictionary<string, GitItemStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, isDirectory) in children)
        {
            if (isDirectory && name.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (inherited is { } all)
            {
                result[name] = all;
            }
            else
            {
                result[name] = found.TryGetValue(name, out var status) ? status : GitItemStatus.Normal;
            }
        }

        return result;
    }

    /// <summary>Lo stato più importante da mostrare tra due.</summary>
    public static GitItemStatus Worst(GitItemStatus a, GitItemStatus b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(GitItemStatus status) => status switch
    {
        GitItemStatus.Conflicted => 6,
        GitItemStatus.Modified => 5,
        GitItemStatus.Added => 4,
        GitItemStatus.Untracked => 3,
        GitItemStatus.Ignored => 2,
        GitItemStatus.Normal => 1,
        _ => 0,
    };
}
