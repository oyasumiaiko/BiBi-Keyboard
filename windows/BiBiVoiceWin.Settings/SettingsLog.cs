using System.Runtime.InteropServices;

namespace BiBiVoiceWin.Settings;

internal static class SettingsLog
{
    private static readonly object LockObj = new();

    public static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BiBiVoiceWin",
            "logs",
            "settings.log"
        );

    public static void Init()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Write("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {message}";
            if (ex is not null) text += Environment.NewLine + ex;

            lock (LockObj)
            {
                File.AppendAllText(LogPath, text + Environment.NewLine);
            }
        }
        catch
        {
            // 写日志失败时不再抛出，避免二次崩溃。
        }
    }

    public static void ShowFatal(string message)
    {
        try
        {
            // 直接弹系统消息框，避免 WinUI 窗口尚未创建时没有可用 UI。
            MessageBoxW(IntPtr.Zero, $"{message}\n日志路径：{LogPath}", "BiBiVoiceWin 设置", 0x10);
        }
        catch
        {
            // 兜底：弹窗失败也不影响进程退出。
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
