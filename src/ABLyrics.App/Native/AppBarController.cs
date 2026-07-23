using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ABLyrics.App.Native;

/// <summary>
/// 按 Microsoft Application Desktop Toolbar 规范注册 AppBar。
/// ABM_SETPOS 后系统会缩小工作区，最大化窗口会自动避让（见 learn.microsoft.com/win32/shell/application-desktop-toolbars）。
/// </summary>
public sealed class AppBarController : IDisposable
{
    private readonly Window _window;
    private int _heightDip;
    private readonly HwndSourceHook _wndProcHook;
    private bool _registered;
    private nint _hwnd;

    public AppBarController(Window window, int heightDip)
    {
        _window = window;
        _heightDip = heightDip;
        _wndProcHook = WndProc;
    }

    public void Attach(nint hwnd)
    {
        _hwnd = hwnd;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(_wndProcHook);
        AppBarNative.HideFromTaskSwitcher(hwnd);
    }

    public void Detach()
    {
        var source = HwndSource.FromHwnd(_hwnd);
        source?.RemoveHook(_wndProcHook);
    }

    public void Register()
    {
        if (_registered || _hwnd == nint.Zero)
        {
            return;
        }

        var barData = CreateBarData(_hwnd);
        barData.CallbackMessage = AppBarNative.CallbackMessage;
        AppBarNative.SHAppBarMessage(AppBarNative.AbmNew, ref barData);
        QuerySetPos();
        // ABM_NEW 后可能改写扩展样式，再刷一次以免重新出现在 Alt+Tab / Win+Tab。
        AppBarNative.HideFromTaskSwitcher(_hwnd);
        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered || _hwnd == nint.Zero)
        {
            return;
        }

        var barData = CreateBarData(_hwnd);
        AppBarNative.SHAppBarMessage(AppBarNative.AbmRemove, ref barData);
        _registered = false;
    }

    public void UpdateHeight(int heightDip)
    {
        _heightDip = heightDip;
        if (_registered)
        {
            QuerySetPos();
        }
    }

    public void QuerySetPos()
    {
        if (_hwnd == nint.Zero)
        {
            return;
        }

        var monitor = GetMonitorInfo(_hwnd);
        var pixelHeight = DipToPixels(_hwnd, _heightDip);

        // 官方示例：先沿底边拉满宽度，再 QUERYPOS / SETPOS，由系统避开任务栏并缩小工作区。
        var barData = CreateBarData(_hwnd);
        barData.Edge = AppBarNative.AbeBottom;
        barData.Rectangle.Left = monitor.RcMonitor.Left;
        barData.Rectangle.Top = monitor.RcMonitor.Top;
        barData.Rectangle.Right = monitor.RcMonitor.Right;
        barData.Rectangle.Bottom = monitor.RcMonitor.Bottom;

        AppBarNative.SHAppBarMessage(AppBarNative.AbmQueryPos, ref barData);
        barData.Rectangle.Top = barData.Rectangle.Bottom - pixelHeight;
        AppBarNative.SHAppBarMessage(AppBarNative.AbmSetPos, ref barData);

        ApplyBounds(barData.Rectangle);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == AppBarNative.CallbackMessage)
        {
            if (wParam == AppBarNative.AbnPosChanged)
            {
                QuerySetPos();
                handled = true;
            }

            return nint.Zero;
        }

        if (msg == AppBarNative.WmActivate && _registered)
        {
            var barData = CreateBarData(hwnd);
            barData.LParam = wParam == nint.Zero ? nint.Zero : new nint(1);
            AppBarNative.SHAppBarMessage(AppBarNative.AbmActivate, ref barData);
        }
        else if (msg == AppBarNative.WmWindowPosChanged && _registered)
        {
            var barData = CreateBarData(hwnd);
            AppBarNative.SHAppBarMessage(AppBarNative.AbmWindowPosChanged, ref barData);
        }

        return nint.Zero;
    }

    private void ApplyBounds(Rect rect)
    {
        AppBarNative.SetWindowPos(
            _hwnd,
            nint.Zero,
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top,
            AppBarNative.SwpShowWindow);

        var source = HwndSource.FromHwnd(_hwnd);
        if (source?.CompositionTarget is null)
        {
            return;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));
        _window.Left = topLeft.X;
        _window.Top = topLeft.Y;
        _window.Width = bottomRight.X - topLeft.X;
        _window.Height = bottomRight.Y - topLeft.Y;
    }

    private static AppBarData CreateBarData(nint hwnd)
    {
        return new AppBarData
        {
            CbSize = Marshal.SizeOf<AppBarData>(),
            Hwnd = hwnd,
            Edge = AppBarNative.AbeBottom,
        };
    }

    private static MonitorInfo GetMonitorInfo(nint hwnd)
    {
        var monitor = AppBarNative.MonitorFromWindow(hwnd, AppBarNative.MonitorDefaultToNearest);
        var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!AppBarNative.GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("无法获取显示器信息。");
        }

        return info;
    }

    private static int DipToPixels(nint hwnd, int dip)
    {
        var source = HwndSource.FromHwnd(hwnd);
        var scale = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        return Math.Max(1, (int)Math.Round(dip * scale.M22));
    }

    public void Dispose()
    {
        Detach();
        Unregister();
    }
}
