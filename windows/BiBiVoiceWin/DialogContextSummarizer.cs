namespace BiBiVoiceWin;

/// <summary>
/// 使用 LLM 生成“结构化上下文参数”，用于 dialog_ctx。
/// 说明：上下文只保留高置信度信息，避免把错误词条长期固化。
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
        var maxChars = Math.Max(80, _cfg.MaxSummaryChars);

        var systemPrompt = """
# 角色

你是 ASR 上下文参数生成器。你的输出会被直接传给 ASR 的 dialog_ctx，用于提升识别准确率。

# 目标

基于“已有上下文 + 新增文本”，生成一条**单行**的结构化上下文参数串。

# 规则

1) 只保留**高置信度**信息，避免猜测。
2) 对不常见专有名词，只有在**明确且重复**时才保留，否则用“药品名/术语”等泛化表述。
3) 不要加入无关闲聊或主观推测，不要换行，不要 Markdown。

# 输出格式（单行）

领域摘要:...；关键词:...；易错纠正:...；格式/约束:...

- 关键词：用逗号分隔，最多 12 个，高置信度、可直接提升识别的词。
- 易错纠正：用“错词->正词”逗号分隔，最多 8 组，仅在非常确定时给出。
- 格式/约束：如“数字单位保留/中英混输/缩写保留”等，最多 6 条。
- 如果某一段没有内容，**直接省略该段**（不要留空标签）。
""";
        var userPrompt = $"""
已有上下文：{existing}
新增文本：{input}
长度限制：最终输出不超过 {maxChars} 字。
""";

        return await _client.SendAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
    }
}
