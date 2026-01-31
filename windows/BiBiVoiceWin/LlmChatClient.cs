using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BiBiVoiceWin;

/// <summary>
/// 轻量 LLM Chat 调用封装：统一处理鉴权、reasoning 参数与响应解析。
/// </summary>
internal sealed class LlmChatClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly ILlmConfig _cfg;
    private readonly string _logPrefix;
    private bool _loggedMissingKey;
    private bool _loggedMissingEndpoint;

    public LlmChatClient(ILlmConfig cfg, string logPrefix)
    {
        _cfg = cfg;
        _logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "LLM" : logPrefix;
    }

    public async Task<string?> SendAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_cfg.LlmEndpoint) || string.IsNullOrWhiteSpace(_cfg.LlmModel))
        {
            if (!_loggedMissingEndpoint)
            {
                _loggedMissingEndpoint = true;
                AppLogger.Status(_logPrefix, "LLM 配置缺失（Endpoint/Model），已跳过");
            }
            return null;
        }
        if (string.IsNullOrWhiteSpace(_cfg.LlmApiKey))
        {
            if (!_loggedMissingKey)
            {
                _loggedMissingKey = true;
                AppLogger.Status(_logPrefix, "LLM ApiKey 为空，已跳过");
            }
            return null;
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = _cfg.LlmModel,
            ["temperature"] = _cfg.LlmTemperature,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var reasoning = BuildReasoningConfig();
        if (reasoning is not null)
        {
            payload["reasoning"] = reasoning;
        }

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        LogRequest(payloadJson);

        var req = new HttpRequestMessage(HttpMethod.Post, _cfg.LlmEndpoint)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.LlmApiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Status(_logPrefix, $"LLM 请求失败: {ex.Message}");
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        LogResponse(resp, body);
        if (!resp.IsSuccessStatusCode)
        {
            AppLogger.Status(_logPrefix, $"LLM 返回错误 {((int)resp.StatusCode)}");
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
            AppLogger.Status(_logPrefix, $"LLM 响应解析失败: {ex.Message}");
        }

        return null;
    }

    private void LogRequest(string payloadJson)
    {
        var apiKey = _cfg.LlmLogIncludeSecrets ? _cfg.LlmApiKey : MaskSecret(_cfg.LlmApiKey);
        AppLogger.Status($"{_logPrefix}请求", $"Endpoint: {_cfg.LlmEndpoint} Model: {_cfg.LlmModel} ApiKey: {apiKey}");
        AppLogger.Status($"{_logPrefix}请求", $"Payload: {payloadJson}");
    }

    private void LogResponse(HttpResponseMessage resp, string body)
    {
        var status = $"{(int)resp.StatusCode} {resp.ReasonPhrase}".Trim();
        AppLogger.Status($"{_logPrefix}响应", $"Status: {status}");
        if (!string.IsNullOrWhiteSpace(body))
        {
            AppLogger.Status($"{_logPrefix}响应", $"Body: {body}");
        }
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Length <= 4) return new string('*', value.Length);
        return value[..2] + new string('*', value.Length - 4) + value[^2..];
    }

    private object? BuildReasoningConfig()
    {
        var effort = _cfg.LlmReasoningEffort?.Trim();
        if (string.IsNullOrWhiteSpace(effort)) return null;

        // OpenRouter 统一 reasoning 参数：尽量保留“思考量”，但不返回推理内容。
        return new
        {
            effort = effort,
            exclude = true
        };
    }
}
