using System.Runtime.InteropServices;

namespace ABLyrics.App.Native;

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

    public const int GwlExstyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExAppWindow = 0x00040000;

    public static readonly uint CallbackMessage = RegisterWindowMessage("ABLyrics.AppBar.Callback");

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    /// <summary>
    /// 从 Alt+Tab / Win+Tab 任务切换器中隐藏窗口。
    /// 仅设 ShowInTaskbar=False 对已注册的 AppBar 不够可靠。
    /// </summary>
    public static void HideFromTaskSwitcher(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExstyle);
        exStyle = (nint)(((long)exStyle | WsExToolWindow) & ~WsExAppWindow);
        SetWindowLongPtr(hwnd, GwlExstyle, exStyle);
    }

    private static nint GetWindowLongPtr(nint hWnd, int nIndex)
    {
        return nint.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : GetWindowLong32(hWnd, nIndex);
    }

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
    {
        return nint.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
    }
}
