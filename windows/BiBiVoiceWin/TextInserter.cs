using System.Windows.Forms;
using System.Threading;

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
    private const int InsertLogBudget = 4;

    public TextInserter(InsertMode mode, bool appendSpace)
    {
        _mode = mode;
        _appendSpace = appendSpace;
    }

    public StreamingSession StartStreamingSession(IntPtr targetWindow)
    {
        return new StreamingSession(this, targetWindow);
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
                    await InsertByClipboardAsync(targetWindow, finalText, ct);
                }
                break;
            case InsertMode.Clipboard:
                await InsertByClipboardAsync(targetWindow, finalText, ct);
                break;
            default:
                Win32.SendUnicodeText(finalText);
                break;
        }
    }

    private static async Task<bool> InsertByClipboardAsync(IntPtr targetWindow, string text, CancellationToken ct)
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
            AppLogger.Status("插入", "剪贴板写入失败");
            return false;
        }

        Win32.TrySetForegroundWindow(targetWindow);
        var pasted = Win32.SendCtrlV();
        if (!pasted)
        {
            AppLogger.Status("插入", "SendCtrlV 失败");
        }

        // 给目标应用一点时间完成粘贴，再恢复剪贴板，尽量不打扰用户。
        try
        {
            await Task.Delay(250, ct);
        }
        catch
        {
            // ignore
        }

        if (backup is null) return pasted;
        try
        {
            Clipboard.SetDataObject(backup);
        }
        catch
        {
            // ignore
        }

        return pasted;
    }

    /// <summary>
    /// 流式插入会话：用于“边说边改”的增量更新。
    /// 说明：基于“前缀复用 + 回退删除”策略，尽量减少对输入框的干扰。
    /// </summary>
    public sealed class StreamingSession
    {
        private readonly TextInserter _owner;
        private IntPtr _targetWindow;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string _lastText = "";
        private bool _finalized;
        private int _insertLogBudget = InsertLogBudget;

        public StreamingSession(TextInserter owner, IntPtr targetWindow)
        {
            _owner = owner;
            _targetWindow = targetWindow;
        }

        public bool IsFinalized => _finalized;

        public Task ApplyPartialAsync(string text, CancellationToken ct)
        {
            return ApplyAsync(text, isFinal: false, ct);
        }

        public Task ApplyFinalAsync(string text, CancellationToken ct)
        {
            return ApplyAsync(text, isFinal: true, ct);
        }

        private async Task ApplyAsync(string text, bool isFinal, CancellationToken ct)
        {
            if (_finalized) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                if (isFinal) _finalized = true;
                return;
            }

            var normalized = text.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                if (isFinal) _finalized = true;
                return;
            }

            LogInsert($"阶段={(isFinal ? "final" : "partial")} 长度={normalized.Length} 模式={_owner._mode}");

            await _gate.WaitAsync(ct).ConfigureAwait(true);
            try
            {
                // 避免重复刷屏
                if (normalized == _lastText)
                {
                    if (isFinal)
                    {
                        await AppendSpaceIfNeededAsync(ct).ConfigureAwait(true);
                        _finalized = true;
                    }
                    return;
                }

                var currentWindow = Win32.GetForegroundWindow();
                if (currentWindow != IntPtr.Zero && currentWindow != _targetWindow)
                {
                    // 用户切换了输入目标：避免在新窗口回退删除旧内容
                    _targetWindow = currentWindow;
                    _lastText = "";
                }

                Win32.TrySetForegroundWindow(_targetWindow);

                var common = GetCommonPrefixLength(_lastText, normalized);
                if (common < _lastText.Length)
                {
                    // 回退删除已有差异部分
                    Win32.SendBackspace(_lastText.Length - common);
                }

                var append = normalized.Substring(common);
                if (!string.IsNullOrEmpty(append))
                {
                    await InsertTextAsync(append, allowClipboard: isFinal, ct).ConfigureAwait(true);
                }

                _lastText = normalized;

                if (isFinal)
                {
                    await AppendSpaceIfNeededAsync(ct).ConfigureAwait(true);
                    _finalized = true;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task AppendSpaceIfNeededAsync(CancellationToken ct)
        {
            if (!_owner._appendSpace) return;
            await InsertTextAsync(" ", allowClipboard: true, ct).ConfigureAwait(true);
        }

        private async Task InsertTextAsync(string text, bool allowClipboard, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (_owner._mode == InsertMode.Clipboard)
            {
                // 强制走剪贴板粘贴，避免某些应用拦截 Unicode SendInput
                var ok = await InsertByClipboardAsync(_targetWindow, text, ct).ConfigureAwait(true);
                if (!ok)
                {
                    LogInsert("剪贴板粘贴失败");
                }
                return;
            }

            // 流式时优先使用 SendInput，避免频繁污染剪贴板。
            if (Win32.SendUnicodeText(text)) return;
            LogInsert($"SendInput 失败，尝试剪贴板（长度={text.Length}）");
            if (!allowClipboard) return;

            var ok2 = await InsertByClipboardAsync(_targetWindow, text, ct).ConfigureAwait(true);
            if (!ok2)
            {
                LogInsert("剪贴板粘贴失败");
            }
        }

        private void LogInsert(string message)
        {
            if (_insertLogBudget <= 0) return;
            _insertLogBudget--;
            AppLogger.Status("插入", message);
        }

        private static int GetCommonPrefixLength(string a, string b)
        {
            var len = Math.Min(a.Length, b.Length);
            var i = 0;
            for (; i < len; i++)
            {
                if (a[i] != b[i]) break;
            }
            return i;
        }
    }
}
