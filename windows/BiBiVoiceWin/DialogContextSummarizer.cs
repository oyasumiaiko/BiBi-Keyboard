namespace BiBiVoiceWin;

/// <summary>
/// 使用 LLM 对“已有摘要 + 新增文本”进行压缩更新。
/// 说明：对话上下文只存“领域摘要”，避免把错误词条长期固化。
/// </summary>
internal sealed class DialogContextSummarizer
{
    private readonly DialogContextConfig _cfg;
    private readonly LlmChatClient? _client;

    public DialogContextSummarizer(DialogContextConfig cfg, ApiProfile? profile)
    {
        _cfg = cfg;
        if (profile is not null)
        {
            _client = new LlmChatClient(profile, "上下文");
        }
    }

    public async Task<string?> SummarizeAsync(string? previousSummary, string finalText, CancellationToken ct)
    {
        if (!_cfg.Enabled) return null;
        if (_client is null)
        {
            AppLogger.Status("上下文", "API 配置缺失，已跳过");
            return null;
        }

        var input = finalText.Trim();
        if (input.Length <= 0) return null;
        if (_cfg.SourceMaxChars > 0 && input.Length > _cfg.SourceMaxChars)
        {
            input = input.Substring(0, _cfg.SourceMaxChars);
        }

        var existing = string.IsNullOrWhiteSpace(previousSummary) ? "无" : previousSummary!.Trim();
        var maxChars = Math.Max(50, _cfg.MaxSummaryChars);

        var systemPrompt = "你是语音输入的上下文摘要器。你的目标是生成简短、稳健的领域摘要，用于提高后续识别准确率。";
        var userPrompt = $"""
已有摘要：{existing}
新增文本：{input}
要求：
1) 输出更新后的摘要，不超过 {maxChars} 字。
2) 只保留确定性高的领域/主题信息。
3) 专有名词仅在出现明显且重复时保留，否则用“药品名/术语”等泛化表述。
4) 不要添加推测内容，不要换行。
""";

        return await _client.SendAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
    }
}
