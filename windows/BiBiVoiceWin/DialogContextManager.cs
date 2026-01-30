using System.Diagnostics;

namespace BiBiVoiceWin;

/// <summary>
/// 对话上下文管理器：
/// - 按窗口维度维护“领域摘要”
/// - 使用 LLM 将最终识别结果压缩成摘要
/// - 在下一次识别时注入 dialog_ctx
/// </summary>
internal sealed class DialogContextManager
{
    private readonly DialogContextConfig _cfg;
    private readonly DialogContextStore _store;
    private readonly DialogContextSummarizer _summarizer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DialogContextManager(DialogContextConfig cfg)
    {
        _cfg = cfg;
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BiBiVoiceWin",
            "dialog_context.json"
        );
        _store = new DialogContextStore(path);
        _summarizer = new DialogContextSummarizer(cfg);
    }

    public string? GetDialogContext(string key)
    {
        if (!_cfg.Enabled) return null;
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _cfg.TtlMinutes));
        return _store.GetSummary(key, ttl);
    }

    public async Task UpdateFromFinalAsync(string key, string finalText, CancellationToken ct)
    {
        if (!_cfg.Enabled) return;
        if (string.IsNullOrWhiteSpace(key)) return;
        var trimmed = finalText?.Trim() ?? "";
        if (trimmed.Length < Math.Max(1, _cfg.MinUpdateChars)) return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var ttl = TimeSpan.FromMinutes(Math.Max(1, _cfg.TtlMinutes));
            var prev = _store.GetSummary(key, ttl);
            var summary = await _summarizer.SummarizeAsync(prev, trimmed, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(summary)) return;

            var normalized = NormalizeSummary(summary, _cfg.MaxSummaryChars);
            _store.SetSummary(key, normalized);
            AppLogger.Status("上下文", $"已更新（长度 {normalized.Length}）");
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string BuildWindowKey(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "unknown";

        var pid = Win32.GetWindowProcessId(hWnd);
        var processName = "";
        if (pid > 0)
        {
            try { processName = Process.GetProcessById((int)pid).ProcessName; } catch { }
        }

        var title = Win32.GetWindowTitle(hWnd);
        var cls = Win32.GetWindowClassName(hWnd);

        return $"{processName}:{pid}:{cls}:{NormalizeKeyPart(title)}";
    }

    private static string NormalizeKeyPart(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string NormalizeSummary(string text, int maxChars)
    {
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (maxChars <= 0) return normalized;
        return normalized.Length > maxChars ? normalized.Substring(0, maxChars) : normalized;
    }
}
