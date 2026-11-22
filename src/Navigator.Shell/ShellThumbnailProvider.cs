using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Navigator.Shell;

/// <summary>
/// Miniature come in Esplora file (foto, video, PDF, documenti...) tramite IShellItemImageFactory.
/// Per i file senza miniatura Windows restituisce l'icona grande. Caricamento in background, con cache.
/// </summary>
public static class ShellThumbnailProvider
{
    private const int MaxCacheEntries = 2000;
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Throttle = new(4);

    public static bool TryGetCached(string path, int size, out ImageSource? image) =>
        Cache.TryGetValue(Key(path, size), out image);

    public static async Task<ImageSource?> GetAsync(string path, int size, CancellationToken cancellationToken = default)
    {
        var key = Key(path, size);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await Throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = await Task.Run(() => Load(path, size), cancellationToken).ConfigureAwait(false);
            if (Cache.Count > MaxCacheEntries)
            {
                Cache.Clear();
            }

            Cache[key] = image;
            return image;
        }
        finally
        {
            Throttle.Release();
        }
    }

    private static string Key(string path, int size)
    {
        long stamp = 0;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return $"{size}|{stamp}|{path}";
    }

    private static ImageSource? Load(string path, int size)
    {
        IShellItemImageFactory? factory = null;
        var hBitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) != 0 || factory is null)
            {
                return null;
            }

            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_RESIZETOFIT, out hBitmap) != 0 || hBitmap == IntPtr.Zero)
            {
                return null;
            }

            return ToBitmapSource(hBitmap);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or ExternalException)
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }

            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    /// <summary>Copia i pixel della bitmap mantenendo la trasparenza (righe dall'alto verso il basso).</summary>
    private static BitmapSource? ToBitmapSource(IntPtr hBitmap)
    {
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0)
        {
            return null;
        }

        var width = bm.bmWidth;
        var height = Math.Abs(bm.bmHeight);
        var stride = width * 4;
        var pixels = new byte[stride * height];

        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // negativo = dall'alto verso il basso
            biPlanes = 1,
            biBitCount = 32,
        };

        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hBitmap, 0, (uint)height, pixels, ref header, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }

        // Le miniature delle foto spesso hanno il canale alfa a zero: in quel caso sono opache.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var source = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private const int SIIGBF_RESIZETOFIT = 0x0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

#pragma warning disable SYSLIB1054
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
#pragma warning restore SYSLIB1054
}
