using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Navigator.Shell.Interop;
using static Navigator.Shell.Interop.NativeMethods;

namespace Navigator.Shell;

/// <summary>
/// Menu contestuale nativo di Esplora file (Apri con, Invia a, estensioni di terze parti...).
/// Deve essere chiamato sul thread dell'interfaccia (STA).
/// </summary>
internal static class ShellContextMenu
{
    private static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;

    public static void Show(IntPtr owner, IReadOnlyList<string> paths, int screenX, int screenY, bool extended)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var absolutePidls = new List<IntPtr>();
        var childPidls = new List<IntPtr>();
        IShellFolder? parent = null;
        IContextMenu? menu = null;
        IntPtr hMenu = IntPtr.Zero;
        HwndSource? source = null;
        HwndSourceHook? hook = null;

        try
        {
            // Il menu condiviso richiede elementi con la stessa cartella padre: usiamo la prima.
            var parentPath = GetParentPath(paths[0]);
            foreach (var path in paths)
            {
                if (!string.Equals(GetParentPath(path), parentPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                {
                    continue;
                }

                absolutePidls.Add(pidl);
                if (SHBindToParent(pidl, ref IID_IShellFolder, out var folderPtr, out var childPidl) != 0)
                {
                    continue;
                }

                if (parent is null)
                {
                    parent = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
                }

                Marshal.Release(folderPtr);
                childPidls.Add(childPidl); // punta dentro il PIDL assoluto: non va liberato a parte
            }

            if (parent is null || childPidls.Count == 0)
            {
                return;
            }

            var hr = parent.GetUIObjectOf(owner, (uint)childPidls.Count, childPidls.ToArray(), ref IID_IContextMenu, IntPtr.Zero, out var menuPtr);
            if (hr != 0 || menuPtr == IntPtr.Zero)
            {
                return;
            }

            menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);
            Marshal.Release(menuPtr);

            hMenu = CreatePopupMenu();
            var flags = CMF_NORMAL | CMF_EXPLORE | (extended ? CMF_EXTENDEDVERBS : 0);
            if (menu.QueryContextMenu(hMenu, 0, FirstCommand, LastCommand, flags) < 0)
            {
                return;
            }

            // I sottomenu ("Invia a", "Apri con") vengono riempiti tramite i messaggi della finestra.
            var menu2 = menu as IContextMenu2;
            var menu3 = menu as IContextMenu3;
            source = HwndSource.FromHwnd(owner);
            if (source is not null && (menu2 is not null || menu3 is not null))
            {
                hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
                    {
                        if (menu3 is not null && menu3.HandleMenuMsg2(msg, wParam, lParam, out var result) == 0)
                        {
                            handled = true;
                            return result;
                        }

                        if (menu2 is not null && menu2.HandleMenuMsg(msg, wParam, lParam) == 0)
                        {
                            handled = true;
                            return IntPtr.Zero;
                        }
                    }

                    return IntPtr.Zero;
                };
                source.AddHook(hook);
            }

            var command = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screenX, screenY, owner, IntPtr.Zero);
            if (command <= 0)
            {
                return;
            }

            var verb = (IntPtr)(command - (int)FirstCommand);
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                hwnd = owner,
                lpVerb = verb,
                lpVerbW = verb,
                lpDirectoryW = parentPath,
                nShow = SW_SHOWNORMAL,
                ptInvoke = new POINT { X = screenX, Y = screenY },
            };
            menu.InvokeCommand(ref info);
        }
        finally
        {
            if (source is not null && hook is not null)
            {
                source.RemoveHook(hook);
            }

            if (hMenu != IntPtr.Zero)
            {
                DestroyMenu(hMenu);
            }

            if (menu is not null)
            {
                Marshal.ReleaseComObject(menu);
            }

            if (parent is not null)
            {
                Marshal.ReleaseComObject(parent);
            }

            foreach (var pidl in absolutePidls)
            {
                CoTaskMemFree(pidl);
            }
        }
    }

    private static string GetParentPath(string path)
    {
        var trimmed = path.Length > 3 ? path.TrimEnd('\\') : path;
        return Path.GetDirectoryName(trimmed) ?? string.Empty;
    }
}
