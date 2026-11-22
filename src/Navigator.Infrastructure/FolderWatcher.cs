using Navigator.Core.Services;

namespace Navigator.Infrastructure;

/// <summary>
/// Osserva una cartella con FileSystemWatcher e raggruppa le notifiche:
/// una copia di 1.000 file produce un solo aggiornamento, non 1.000.
/// </summary>
public sealed class FolderWatcher : IFolderWatcher
{
    public IDisposable Watch(string path, Action onChanged)
    {
        try
        {
            return new Subscription(path, onChanged);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            // Cartelle che non si possono osservare (alcune unità di rete): nessun aggiornamento automatico.
            return new NoOp();
        }
    }

    private sealed class NoOp : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class Subscription : IDisposable
    {
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _timer;
        private readonly Action _onChanged;
        private int _disposed;

        public Subscription(string path, Action onChanged)
        {
            _onChanged = onChanged;
            _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                               | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Changed += OnEvent;
            _watcher.Renamed += OnEvent;
            _watcher.Error += (_, _) => Schedule();
            _watcher.EnableRaisingEvents = true;
        }

        private void OnEvent(object sender, FileSystemEventArgs e) => Schedule();

        private void Schedule()
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _timer.Change(Delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void Fire()
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _onChanged();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _timer.Dispose();
        }
    }
}
