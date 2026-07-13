using System.Diagnostics;

namespace SpotLyrics.App;

internal static class DevExceptionReporter
{
    public static bool IsEnabled { get; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// 将异常信息写入控制台错误输出，便于复制查阅。
    /// 若要展示弹窗，请自行调用 System.Windows.MessageBox.Show()。
    /// </summary>
    public static void Show(Exception exception, string context)
    {
        if (!IsEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {context}");
        Console.Error.WriteLine(exception);
        Console.Error.WriteLine();
    }
}
