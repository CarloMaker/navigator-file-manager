namespace Navigator.Core.Utilities;

/// <summary>
/// Ordinamento "naturale" come in Esplora file: "file2" viene prima di "file10".
/// Non distingue maiuscole e minuscole.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (si < x.Length && x[si] == '0') si++;
                while (sj < y.Length && y[sj] == '0') sj++;
                int ei = si, ej = sj;
                while (ei < x.Length && char.IsDigit(x[ei])) ei++;
                while (ej < y.Length && char.IsDigit(y[ej])) ej++;

                var lenX = ei - si;
                var lenY = ej - sj;
                if (lenX != lenY)
                {
                    return lenX.CompareTo(lenY);
                }

                var cmp = string.CompareOrdinal(x, si, y, sj, lenX);
                if (cmp != 0)
                {
                    return cmp;
                }

                i = ei;
                j = ej;
                continue;
            }

            var c = string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
            if (c != 0)
            {
                return c;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
