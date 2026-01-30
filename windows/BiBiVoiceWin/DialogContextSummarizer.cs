using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BiBiVoiceWin;

/// <summary>
/// 使用 LLM 对“已有摘要 + 新增文本”进行压缩更新。
/// 说明：对话上下文只存“领域摘要”，避免把错误词条长期固化。
/// </summary>
internal sealed class DialogContextSummarizer
{
    private readonly DialogContextConfig _cfg;
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private bool _loggedMissingKey;
    private bool _loggedMissingEndpoint;

    public DialogContextSummarizer(DialogContextConfig cfg)
    {
        _cfg = cfg;
    }

    public async Task<string?> SummarizeAsync(string? previousSummary, string finalText, CancellationToken ct)
    {
        if (!_cfg.Enabled) return null;
        if (string.IsNullOrWhiteSpace(_cfg.LlmEndpoint) || string.IsNullOrWhiteSpace(_cfg.LlmModel))
        {
            if (!_loggedMissingEndpoint)
            {
                _loggedMissingEndpoint = true;
                AppLogger.Status("上下文", "LLM 配置缺失（Endpoint/Model），已跳过");
            }
            return null;
        }
        if (string.IsNullOrWhiteSpace(_cfg.LlmApiKey))
        {
            if (!_loggedMissingKey)
            {
                _loggedMissingKey = true;
                AppLogger.Status("上下文", "LLM ApiKey 为空，已跳过");
            }
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

        var payload = new
        {
            model = _cfg.LlmModel,
            temperature = _cfg.LlmTemperature,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, _cfg.LlmEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.LlmApiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Status("上下文", $"LLM 请求失败: {ex.Message}");
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            AppLogger.Status("上下文", $"LLM 返回错误 {((int)resp.StatusCode)}");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                {
                    return content.GetString()?.Trim();
                }
                if (choice.TryGetProperty("text", out var text))
                {
                    return text.GetString()?.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Status("上下文", $"LLM 响应解析失败: {ex.Message}");
        }

        return null;
    }
}
