using System.Text;
using System.Text.Json;

namespace BiBiVoiceWin;

/// <summary>
/// 火山引擎 recognize/flash 非流式识别：
/// - 对齐 Android 端 VolcFileAsrEngine 的请求结构与 Header
/// - 输入：WAV（PCM16/mono/16k 推荐）
/// - 输出：result.text
/// </summary>
public sealed class VolcFlashAsrClient
{
    private readonly VolcConfig _cfg;
    private readonly HttpClient _http;

    public VolcFlashAsrClient(VolcConfig cfg, HttpClient? http = null)
    {
        _cfg = cfg;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Endpoint))
        {
            throw new InvalidOperationException("Volc.Endpoint 为空");
        }
        if (string.IsNullOrWhiteSpace(_cfg.AppKey) || string.IsNullOrWhiteSpace(_cfg.AccessKey))
        {
            throw new InvalidOperationException("请先在 config.json 中填写 Volc.AppKey 与 Volc.AccessKey");
        }
        if (string.IsNullOrWhiteSpace(_cfg.ResourceId))
        {
            throw new InvalidOperationException("Volc.ResourceId 为空");
        }

        var b64 = Convert.ToBase64String(wavBytes);
        var reqBody = BuildRequestJson(b64, _cfg.AppKey, _cfg.EnableDdc);

        using var req = new HttpRequestMessage(HttpMethod.Post, _cfg.Endpoint);
        req.Headers.Add("X-Api-App-Key", _cfg.AppKey);
        req.Headers.Add("X-Api-Access-Key", _cfg.AccessKey);
        req.Headers.Add("X-Api-Resource-Id", _cfg.ResourceId);
        req.Headers.Add("X-Api-Request-Id", Guid.NewGuid().ToString());
        req.Headers.Add("X-Api-Sequence", "-1");
        req.Content = new StringContent(reqBody, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        var respText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var volcMsg = TryGetHeader(resp, "X-Api-Message");
            var detail = string.IsNullOrWhiteSpace(volcMsg) ? resp.ReasonPhrase : volcMsg;
            throw new InvalidOperationException($"火山 ASR 请求失败：HTTP {(int)resp.StatusCode} {detail}".Trim());
        }

        var text = ParseResultText(respText);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("火山 ASR 返回空结果");
        }

        return text.Trim();
    }

    private static string? TryGetHeader(HttpResponseMessage resp, string name)
    {
        if (resp.Headers.TryGetValues(name, out var vs)) return vs.FirstOrDefault();
        if (resp.Content.Headers.TryGetValues(name, out var cvs)) return cvs.FirstOrDefault();
        return null;
    }

    /// <summary>
    /// 构建请求 JSON（尽量保持“纯函数”，便于单测与跨端对照）。
    /// </summary>
    public static string BuildRequestJson(string base64AudioWav, string uid, bool enableDdc)
    {
        var root = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                ["uid"] = uid
            },
            ["audio"] = new Dictionary<string, object?>
            {
                ["data"] = base64AudioWav
            },
            ["request"] = new Dictionary<string, object?>
            {
                ["model_name"] = "bigmodel",
                ["enable_itn"] = true,
                ["enable_punc"] = true,
                ["enable_ddc"] = enableDdc
            }
        };

        return JsonSerializer.Serialize(root);
    }

    private static string ParseResultText(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return "";
            if (!result.TryGetProperty("text", out var textEl)) return "";
            return textEl.GetString() ?? "";
        }
        catch
        {
            return "";
        }
    }
}

