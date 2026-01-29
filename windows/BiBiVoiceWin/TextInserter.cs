using System.Windows.Forms;

namespace BiBiVoiceWin;

public enum InsertMode
{
    SendInput = 1,
    Clipboard = 2
}

public static class InsertModeParser
{
    public static InsertMode ParseOrDefault(string? text, InsertMode defaultMode = InsertMode.SendInput)
    {
        if (string.IsNullOrWhiteSpace(text)) return defaultMode;
        return text.Trim().ToLowerInvariant() switch
        {
            "sendinput" => InsertMode.SendInput,
            "clipboard" => InsertMode.Clipboard,
            "paste" => InsertMode.Clipboard,
            _ => defaultMode
        };
    }
}

/// <summary>
/// 将识别文本写入“录音开始时的前台窗口”的当前焦点输入控件。
/// </summary>
public sealed class TextInserter
{
    private readonly InsertMode _mode;
    private readonly bool _appendSpace;

    public TextInserter(InsertMode mode, bool appendSpace)
    {
        _mode = mode;
        _appendSpace = appendSpace;
    }

    public async Task InsertAsync(IntPtr targetWindow, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var finalText = _appendSpace ? (text + " ") : text;

        // 尽量把目标窗口拉回前台，保证 SendInput / Ctrl+V 能落到正确位置。
        Win32.TrySetForegroundWindow(targetWindow);

        switch (_mode)
        {
            case InsertMode.SendInput:
                if (!Win32.SendUnicodeText(finalText))
                {
                    // 某些应用可能对 SendInput 兼容性差；这里提供“软降级”：
                    await InsertByClipboardAsync(finalText, ct);
                }
                break;
            case InsertMode.Clipboard:
                await InsertByClipboardAsync(finalText, ct);
                break;
            default:
                Win32.SendUnicodeText(finalText);
                break;
        }
    }

    private static async Task InsertByClipboardAsync(string text, CancellationToken ct)
    {
        // 注意：Clipboard API 需要在 STA 线程调用。此项目主线程是 WinForms STA。
        IDataObject? backup = null;
        try
        {
            backup = Clipboard.GetDataObject();
        }
        catch
        {
            // 某些场景下剪贴板可能被占用；不影响“最小可运行链路”，继续尝试写入。
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 如果连剪贴板也写不进去，那就只能放弃插入。
            return;
        }

        Win32.SendCtrlV();

        // 给目标应用一点时间完成粘贴，再恢复剪贴板，尽量不打扰用户。
        try
        {
            await Task.Delay(250, ct);
        }
        catch
        {
            // ignore
        }

        if (backup is null) return;
        try
        {
            Clipboard.SetDataObject(backup);
        }
        catch
        {
            // ignore
        }
    }
}

