using System.Runtime.InteropServices;

namespace SpotLyrics.App.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AppBarData
{
    public int CbSize;
    public nint Hwnd;
    public uint CallbackMessage;
    public uint Edge;
    public Rect Rectangle;
    public nint LParam;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MonitorInfo
{
    public int CbSize;
    public Rect RcMonitor;
    public Rect RcWork;
    public uint DwFlags;
}

internal static class AppBarNative
{
    public const int AbmNew = 0x00000000;
    public const int AbmRemove = 0x00000001;
    public const int AbmQueryPos = 0x00000002;
    public const int AbmSetPos = 0x00000003;
    public const int AbmActivate = 0x00000006;
    public const int AbmWindowPosChanged = 0x00000009;

    public const int AbeBottom = 3;

    public const int AbnPosChanged = 1;

    public const uint MonitorDefaultToNearest = 2;

    public const int WmActivate = 0x0006;
    public const int WmWindowPosChanged = 0x0047;

    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpShowWindow = 0x0040;

    public static readonly uint CallbackMessage = RegisterWindowMessage("SpotLyrics.AppBar.Callback");

    [DllImport("shell32.dll")]
    public static extern uint SHAppBarMessage(int dwMessage, ref AppBarData data);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);
}
