namespace BiBiVoiceWin;

/// <summary>
/// 使用 LLM 对“新增语音文本”做上下文校对，输出只包含修正后的新增文本。
/// </summary>
internal sealed class InputProofreader
{
    private readonly ProofreadConfig _cfg;
    private readonly LlmChatClient _client;

    public InputProofreader(ProofreadConfig cfg)
    {
        _cfg = cfg;
        _client = new LlmChatClient(cfg, "校对");
    }

    public async Task<string?> ProofreadAsync(string? existingText, string finalText, CancellationToken ct)
    {
        if (!_cfg.Enabled) return null;

        var input = Normalize(finalText);
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = TrimByMaxChars(input, _cfg.SourceMaxChars);

        var context = Normalize(existingText);
        context = string.IsNullOrWhiteSpace(context) ? "无" : TrimTailByMaxChars(context, _cfg.SourceMaxChars);

        AppLogger.Status("校对发送", $"上下文({context.Length}): {context}");
        AppLogger.Status("校对发送", $"新增({input.Length}): {input}");

        var systemPrompt = "你是语音输入的校对助手，目标是在不改写语义的情况下纠正听写错误。只输出修正后的新增文本。";
        var userPrompt = $"""
已有输入（上下文，可能未校对）：{context}
新增语音文本：{input}
要求：
1) 只输出“新增语音文本”的修正结果，不要输出解释。
2) 不改写已有输入，只作为语境参考。
3) 专有名词不确定时保持原样或使用更中性的表述。
        4) 不要换行。
""";

        var result = await _client.SendAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
        if (result is null)
        {
            AppLogger.Status("校对返回", "结果为空");
            return null;
        }
        var normalized = Normalize(result);
        AppLogger.Status("校对返回", $"结果({normalized?.Length ?? 0}): {normalized}");
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (string.Equals(normalized, input, StringComparison.Ordinal)) return null;

        return normalized;
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string TrimByMaxChars(string text, int maxChars)
    {
        if (maxChars <= 0 || text.Length <= maxChars) return text;
        return text.Substring(0, maxChars);
    }

    private static string TrimTailByMaxChars(string text, int maxChars)
    {
        if (maxChars <= 0 || text.Length <= maxChars) return text;
        // 校对更依赖“最近输入上下文”，优先取尾部。
        return text.Substring(text.Length - maxChars, maxChars);
    }
}
