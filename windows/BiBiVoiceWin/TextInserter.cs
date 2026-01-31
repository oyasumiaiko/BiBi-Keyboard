using System.Threading;

namespace BiBiVoiceWin;

public enum InsertMode
{
    SendInput = 1
}

public static class InsertModeParser
{
    public static InsertMode ParseOrDefault(string? text, InsertMode defaultMode = InsertMode.SendInput)
    {
        if (string.IsNullOrWhiteSpace(text)) return defaultMode;
        return text.Trim().ToLowerInvariant() switch
        {
            "sendinput" => InsertMode.SendInput,
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

    public Task InsertAsync(IntPtr targetWindow, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        var finalText = _appendSpace ? (text + " ") : text;

        // 尽量把目标窗口拉回前台，保证 SendInput 能落到正确位置。
        Win32.TrySetForegroundWindow(targetWindow);

        if (!Win32.SendUnicodeText(finalText))
        {
            AppLogger.Status("插入", $"SendInput 失败 (err={Win32.LastSendInputError})");
        }
        return Task.CompletedTask;
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
        private bool _finalSpaceAppended;
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
            if (!isFinal)
            {
                _finalSpaceAppended = false;
            }
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
                    await InsertTextAsync(append, ct).ConfigureAwait(true);
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
            if (_finalSpaceAppended) return;
            await InsertTextAsync(" ", ct).ConfigureAwait(true);
            _finalSpaceAppended = true;
        }

        private Task InsertTextAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(text)) return Task.CompletedTask;

            // 流式时使用 SendInput。
            if (Win32.SendUnicodeText(text)) return Task.CompletedTask;
            LogInsert($"SendInput 失败（长度={text.Length}，err={Win32.LastSendInputError}）");
            return Task.CompletedTask;
        }

        public async Task ApplyCorrectionAsync(string correctedText, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(correctedText)) return;
            var normalized = correctedText.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return;

            await _gate.WaitAsync(ct).ConfigureAwait(true);
            try
            {
                if (normalized == _lastText) return;

                var currentWindow = Win32.GetForegroundWindow();
                if (currentWindow != IntPtr.Zero && currentWindow != _targetWindow)
                {
                    // 用户切换了输入目标：避免在新窗口回退删除旧内容
                    _targetWindow = currentWindow;
                    _lastText = "";
                }

                Win32.TrySetForegroundWindow(_targetWindow);

                // 先回退删除已插入内容（含尾随空格）
                var backspaceCount = _lastText.Length + (_finalSpaceAppended ? 1 : 0);
                if (backspaceCount > 0)
                {
                    Win32.SendBackspace(backspaceCount);
                }

                await InsertTextAsync(normalized, ct).ConfigureAwait(true);
                _lastText = normalized;
                _finalSpaceAppended = false;
                await AppendSpaceIfNeededAsync(ct).ConfigureAwait(true);
                _finalized = true;
            }
            finally
            {
                _gate.Release();
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
