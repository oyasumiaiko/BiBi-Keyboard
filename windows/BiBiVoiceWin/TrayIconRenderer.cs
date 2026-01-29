using System.Drawing;
using System.Runtime.InteropServices;

namespace BiBiVoiceWin;

/// <summary>
/// 动态托盘图标：上半表示麦克风是否在录音，下半表示流式传输是否活跃。
/// </summary>
internal static class TrayIconRenderer
{
    public static Icon Create(bool micOn, bool streamOn)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        var off = Color.FromArgb(60, 60, 60);
        var micColor = micOn ? Color.OrangeRed : off;
        var streamColor = streamOn ? Color.LimeGreen : off;

        // 上半：麦克风
        using (var brush = new SolidBrush(micColor))
        {
            g.FillRectangle(brush, 0, 0, size, size / 2);
        }
        // 下半：流式传输
        using (var brush = new SolidBrush(streamColor))
        {
            g.FillRectangle(brush, 0, size / 2, size, size / 2);
        }

        // 边框
        using (var pen = new Pen(Color.FromArgb(120, 120, 120)))
        {
            g.DrawRectangle(pen, 0, 0, size - 1, size - 1);
            g.DrawLine(pen, 0, size / 2, size - 1, size / 2);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            var icon = (Icon)Icon.FromHandle(hIcon).Clone();
            return icon;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
