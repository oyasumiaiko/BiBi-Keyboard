namespace BiBiVoiceWin;

/// <summary>
/// 使用 LLM 对最终识别文本做后处理，输出修正后的整段文本。
/// </summary>
internal sealed class InputProofreader
{
    private readonly ProofreadConfig _cfg;
    private readonly LlmChatClient? _client;
    private const string UserInputPrefix = "待处理文本:\n";
    private const string DefaultPostProcessPrompt = """
# 角色

你是一个顶级的 ASR（自动语音识别）后处理专家。

# 任务

用户会向你发送一段由 ASR 系统转录的原始文本。你的任务是将其处理一遍。

# 规则

1.  **去除无关填充词**: 彻底删除所有无意义的语气词、犹豫词和口头禅。
    - **示例**: "嗯"、"啊"、"呃"、"那个"、"然后"、"就是说"等。
2.  **合并重复与修正口误**: 当说话者重复单词、短语或进行自我纠正时，整合这些内容，只保留其最终的、最清晰的意图。
    - **重复示例**: 将"我想...我想去..."修正为"我想去..."。
    - **口误修正示例**: 将"我们明天去上海，哦不对，去苏州开会"修正为"我们明天去苏州开会"。
3.  **修正识别错误**: 根据上下文语境，纠正明显不符合逻辑的同音、近音词汇。
    - **同音词示例**: 将"请大家准时参加明天的『会意』"修正为"请大家准时参加明天的『会议』"
4.  **保持语义完整性**: 确保修正后的文本忠实于说话者的原始意图，不要进行主观臆断或添加额外信息。保留用户语气，无需进行书面化等风格化处理。
5.  输入格式提示：用户输入会以“待处理文本:”开头，该标签仅用于标记，请忽略标签本身，只处理其后的正文。

# 输出要求

- 只输出修正后的最终文本
- 不要输出任何解释、前后缀、引号或 Markdown 格式
- 如果输入文本已经足够规范，直接原样输出

# 示例

**输入**: "嗯...那个...我想确认一下，我们明天，我们明天的那个会意，啊不对，会议，时间是不是...是不是上午九点？"
**输出**: 我想确认一下，我们明天的会议时间是不是上午九点？
""";

    public InputProofreader(ProofreadConfig cfg, ApiProfile? profile)
    {
        _cfg = cfg;
        if (profile is not null)
        {
            _client = new LlmChatClient(profile, "后处理");
        }
    }

    public async Task<string?> ProofreadAsync(string? existingText, string finalText, CancellationToken ct)
    {
        if (!_cfg.Enabled) return null;
        if (_client is null)
        {
            AppLogger.Status("后处理", "API 配置缺失，已跳过");
            return null;
        }

        var input = Normalize(finalText);
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = TrimByMaxChars(input, _cfg.SourceMaxChars);

        AppLogger.Status("后处理发送", $"待处理({input.Length}): {input}");

        var systemPrompt = DefaultPostProcessPrompt;
        var userPrompt = UserInputPrefix + input;

        var result = await _client.SendAsync(systemPrompt, userPrompt, ct).ConfigureAwait(false);
        if (result is null)
        {
            AppLogger.Status("后处理返回", "结果为空");
            return null;
        }
        var normalized = Normalize(result);
        AppLogger.Status("后处理返回", $"结果({normalized?.Length ?? 0}): {normalized}");
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

}
