using System.Windows.Forms;

namespace BiBiVoiceWin;

internal static class ApplicationConfiguration
{
    public static void Initialize()
    {
        // WinForms 模板默认初始化：
        // - 高 DPI 模式
        // - 视觉样式
        // - 文本渲染兼容开关
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }
}

