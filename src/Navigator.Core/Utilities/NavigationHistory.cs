namespace Navigator.Core.Utilities;

/// <summary>Cronologia avanti/indietro di una scheda.</summary>
public sealed class NavigationHistory
{
    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private const int MaxEntries = 100;

    public string? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public IReadOnlyList<string> BackEntries => _back;

    public IReadOnlyList<string> ForwardEntries => _forward;

    /// <summary>Registra una nuova posizione e svuota la cronologia "avanti".</summary>
    public void Push(string path)
    {
        if (Current is not null && PathHelper.AreSame(Current, path))
        {
            return;
        }

        if (Current is not null)
        {
            _back.Add(Current);
            if (_back.Count > MaxEntries)
            {
                _back.RemoveAt(0);
            }
        }

        _forward.Clear();
        Current = path;
    }

    public string? GoBack()
    {
        if (!CanGoBack)
        {
            return null;
        }

        if (Current is not null)
        {
            _forward.Add(Current);
        }

        Current = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        return Current;
    }

    public string? GoForward()
    {
        if (!CanGoForward)
        {
            return null;
        }

        if (Current is not null)
        {
            _back.Add(Current);
        }

        Current = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        return Current;
    }
}
