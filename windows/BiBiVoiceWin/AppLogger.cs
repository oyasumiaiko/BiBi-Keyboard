using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BiBiVoiceWin;

internal static class AppLogger
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;
    private static bool _consoleReady;
    private static string _logPath = "";

    public static string LogPath
    {
        get
        {
            EnsureInitialized();
            return _logPath;
        }
    }

    public static void Status(string title, string message)
    {
        var line = FormatLine("INFO", title, message);
        WriteLine(line);
    }

    private static string FormatLine(string level, string title, string message)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "-" : title.Trim();
        var safeMessage = message ?? "";
        return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {safeTitle} {safeMessage}";
    }

    private static void WriteLine(string line)
    {
        EnsureInitialized();

        try { Debug.WriteLine(line); } catch { }

        if (_consoleReady)
        {
            try { Console.WriteLine(line); } catch { }
        }

        try
        {
            lock (SyncRoot)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 写日志失败不影响主流程
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (SyncRoot)
        {
            if (_initialized) return;

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BiBiVoiceWin",
                "logs"
            );

            try
            {
                Directory.CreateDirectory(logDir);
                _logPath = Path.Combine(logDir, "app.log");
            }
            catch
            {
                _logPath = Path.Combine(Path.GetTempPath(), "BiBiVoiceWin.app.log");
            }

            TryEnableConsoleOutput();
            _initialized = true;
        }
    }

    private static void TryEnableConsoleOutput()
    {
        if (_consoleReady) return;

        var attached = false;
        try
        {
            // 只尝试附加父控制台，避免弹出新窗口
            attached = AttachConsole(ATTACH_PARENT_PROCESS);
            if (!attached && Marshal.GetLastWin32Error() == 5)
            {
                // 已经有控制台的场景（例如调试器启动）
                attached = true;
            }
        }
        catch
        {
            attached = false;
        }

        if (!attached) return;

        try
        {
            var stdout = Console.OpenStandardOutput();
            var writer = new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true };
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            Console.SetOut(writer);
            Console.SetError(writer);
            _consoleReady = true;
        }
        catch
        {
            _consoleReady = false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
}
