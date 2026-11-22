using System.Runtime.InteropServices;

namespace Navigator.Shell.Interop;

#pragma warning disable SYSLIB1054 // DllImport va bene: chiamate poco frequenti

internal static class NativeMethods
{
    // ---- SHGetFileInfo ------------------------------------------------------------------
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_TYPENAME = 0x000000400;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint SHGFI_PIDL = 0x000000008;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ---- SHFileOperation ----------------------------------------------------------------
    public const uint FO_MOVE = 0x0001;
    public const uint FO_COPY = 0x0002;
    public const uint FO_DELETE = 0x0003;

    public const ushort FOF_RENAMEONCOLLISION = 0x0008;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_ALLOWUNDO = 0x0040;
    public const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    public const ushort FOF_WANTNUKEWARNING = 0x4000;

    // Layout valido per processi a 64 bit (l'app è compilata x64).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;

        public ushort fFlags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;

        public IntPtr hNameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    // ---- ShellExecuteEx (Proprietà) -----------------------------------------------------
    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    public const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpVerb;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParameters;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectory;

        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpClass;

        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    // ---- Menu contestuale ---------------------------------------------------------------
    public const uint CMF_NORMAL = 0x00000000;
    public const uint CMF_EXPLORE = 0x00000004;
    public const uint CMF_CANRENAME = 0x00000010;
    public const uint CMF_EXTENDEDVERBS = 0x00000100;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint CMIC_MASK_UNICODE = 0x00004000;
    public const uint CMIC_MASK_PTINVOKE = 0x20000000;
    public const int SW_SHOWNORMAL = 1;

    public const int WM_INITMENUPOPUP = 0x0117;
    public const int WM_DRAWITEM = 0x002B;
    public const int WM_MEASUREITEM = 0x002C;
    public const int WM_MENUCHAR = 0x0120;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpParameters;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpDirectory;

        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.LPStr)]
        public string? lpTitle;

        public IntPtr lpVerbW;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpParametersW;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpDirectoryW;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTitleW;

        public POINT ptInvoke;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);
}

[ComImport]
[Guid("000214E6-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig]
    int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

    [PreserveSig]
    int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);

    [PreserveSig]
    int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

    [PreserveSig]
    int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

    [PreserveSig]
    int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);

    [PreserveSig]
    int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

    [PreserveSig]
    int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

    [PreserveSig]
    int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
}

[ComImport]
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    int InvokeCommand(ref NativeMethods.CMINVOKECOMMANDINFOEX pici);

    [PreserveSig]
    int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
}

[ComImport]
[Guid("000214F4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    int InvokeCommand(ref NativeMethods.CMINVOKECOMMANDINFOEX pici);

    [PreserveSig]
    int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

    [PreserveSig]
    int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);
}

[ComImport]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3
{
    [PreserveSig]
    int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

    [PreserveSig]
    int InvokeCommand(ref NativeMethods.CMINVOKECOMMANDINFOEX pici);

    [PreserveSig]
    int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

    [PreserveSig]
    int HandleMenuMsg(int uMsg, IntPtr wParam, IntPtr lParam);

    [PreserveSig]
    int HandleMenuMsg2(int uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
}
