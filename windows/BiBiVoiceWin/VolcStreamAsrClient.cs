using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BiBiVoiceWin;

/// <summary>
/// 火山引擎 WebSocket 流式 ASR 客户端（bigmodel_async 二进制协议）。
/// 说明：
/// - 先发送“完整请求”（Full Client Request），再分包发送 PCM16 音频。
/// - 服务端返回 JSON 结果；这里只取最终结果文本。
/// </summary>
public sealed class VolcStreamAsrClient
{
    private const int ProtocolVersion = 0x1;
    private const int HeaderSizeUnits = 0x1; // 4 bytes

    // Message types
    private const int MsgTypeFullClientReq = 0x1;
    private const int MsgTypeAudioOnlyClientReq = 0x2;
    private const int MsgTypeFullServerResp = 0x9;
    private const int MsgTypeErrorServer = 0xF;

    // Serialization
    private const int SerializeNone = 0x0;
    private const int SerializeJson = 0x1;

    // Compression
    private const int CompressGzip = 0x1;

    // Flags
    private const int FlagAudioLast = 0x2; // 客户端音频最后一包
    private const int FlagServerFinalMask = 0x3; // 服务端最终结果标志

    private const int DefaultChunkMillis = 200;

    private readonly VolcConfig _cfg;
    private readonly TimeSpan _receiveTimeout;

    public VolcStreamAsrClient(VolcConfig cfg, TimeSpan? receiveTimeout = null)
    {
        _cfg = cfg;
        _receiveTimeout = receiveTimeout ?? TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// 发送 PCM16（小端、单声道）并返回最终识别文本。
    /// </summary>
    public async Task<string> TranscribeAsync(byte[] pcm16, int sampleRate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Endpoint))
        {
            throw new InvalidOperationException("Volc.Endpoint 为空");
        }
        if (!_cfg.Endpoint.StartsWith("ws", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Volc.Endpoint 必须是 wss://... 的流式地址");
        }
        if (string.IsNullOrWhiteSpace(_cfg.AppKey) || string.IsNullOrWhiteSpace(_cfg.AccessKey))
        {
            throw new InvalidOperationException("请先在 config.json 中填写 Volc.AppKey（App ID）与 Volc.AccessKey（Access Token）");
        }
        if (string.IsNullOrWhiteSpace(_cfg.ResourceId))
        {
            throw new InvalidOperationException("Volc.ResourceId 为空");
        }

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        ws.Options.SetRequestHeader("X-Api-App-Key", _cfg.AppKey);
        ws.Options.SetRequestHeader("X-Api-Access-Key", _cfg.AccessKey);
        ws.Options.SetRequestHeader("X-Api-Resource-Id", _cfg.ResourceId);
        ws.Options.SetRequestHeader("X-Api-Connect-Id", Guid.NewGuid().ToString());

        await ws.ConnectAsync(new Uri(_cfg.Endpoint), ct).ConfigureAwait(false);

        // 1) 发送“完整请求”
        var fullJson = BuildFullClientRequestJson(_cfg.AppKey, sampleRate, _cfg.EnableDdc);
        var fullPayload = Gzip(Encoding.UTF8.GetBytes(fullJson));
        await SendFrameAsync(
            ws,
            messageType: MsgTypeFullClientReq,
            flags: 0,
            serialization: SerializeJson,
            compression: CompressGzip,
            payload: fullPayload,
            ct
        ).ConfigureAwait(false);

        // 2) 分包发送音频（最后一包带标记）
        await SendAudioFramesAsync(ws, pcm16, sampleRate, ct).ConfigureAwait(false);

        // 3) 等待最终结果
        var text = await ReceiveFinalTextAsync(ws, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("火山 ASR 返回空结果");
        }

        return text.Trim();
    }

    private static async Task SendAudioFramesAsync(ClientWebSocket ws, byte[] pcm16, int sampleRate, CancellationToken ct)
    {
        var rate = sampleRate <= 0 ? 16000 : sampleRate;
        // 约定：输入为 PCM16 单声道（16bit = 2 bytes），按 200ms 一包切分
        var bytesPerSecond = rate * 2;
        var chunkBytes = Math.Max(1, bytesPerSecond * DefaultChunkMillis / 1000);

        if (pcm16.Length == 0)
        {
            await SendFrameAsync(
                ws,
                messageType: MsgTypeAudioOnlyClientReq,
                flags: FlagAudioLast,
                serialization: SerializeNone,
                compression: CompressGzip,
                payload: Gzip(Array.Empty<byte>()),
                ct
            ).ConfigureAwait(false);
            return;
        }

        var offset = 0;
        while (offset < pcm16.Length)
        {
            var size = Math.Min(chunkBytes, pcm16.Length - offset);
            var isLast = offset + size >= pcm16.Length;
            var chunk = new byte[size];
            Buffer.BlockCopy(pcm16, offset, chunk, 0, size);

            await SendFrameAsync(
                ws,
                messageType: MsgTypeAudioOnlyClientReq,
                flags: isLast ? FlagAudioLast : 0,
                serialization: SerializeNone,
                compression: CompressGzip,
                payload: Gzip(chunk),
                ct
            ).ConfigureAwait(false);

            offset += size;
        }
    }

    private async Task<string> ReceiveFinalTextAsync(ClientWebSocket ws, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_receiveTimeout);

        string? finalText = null;
        while (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
        {
            var msg = await ReceiveMessageAsync(ws, timeoutCts.Token).ConfigureAwait(false);
            if (msg is null) break;

            if (!TryParseServerMessage(msg, out var text, out var isFinal, out var error))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException(error);
            }

            if (isFinal)
            {
                finalText = text ?? "";
                break;
            }
        }

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "final", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // 忽略关闭失败，避免影响最终结果返回
        }

        return finalText ?? "";
    }

    private static async Task<byte[]?> ReceiveMessageAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            if (result.Count > 0)
            {
                ms.Write(buffer, 0, result.Count);
            }
            if (result.EndOfMessage)
            {
                break;
            }
        }
        return ms.ToArray();
    }

    private static bool TryParseServerMessage(byte[] data, out string? text, out bool isFinal, out string? error)
    {
        text = null;
        isFinal = false;
        error = null;
        if (data.Length < 8) return false;

        var b0 = data[0] & 0xFF;
        var b1 = data[1] & 0xFF;
        var b2 = data[2] & 0xFF;

        var headerSizeBytes = (b0 & 0x0F) * 4;
        var msgType = (b1 >> 4) & 0x0F;
        var flags = b1 & 0x0F;
        var serialization = (b2 >> 4) & 0x0F;
        var compression = b2 & 0x0F;

        var offset = headerSizeBytes;

        if (msgType == MsgTypeFullServerResp)
        {
            // full server response: header + sequence(4) + payloadSize(4) + payload(JSON)
            if (data.Length < offset + 4) return false;
            offset += 4; // sequence
            if (data.Length < offset + 4) return false;
            var payloadSize = ReadUInt32BE(data, offset);
            offset += 4;
            if (data.Length < offset + payloadSize) return false;

            var payload = new byte[payloadSize];
            Buffer.BlockCopy(data, offset, payload, 0, payloadSize);
            if (compression == CompressGzip)
            {
                payload = Gunzip(payload);
            }
            if (serialization == SerializeJson)
            {
                var json = Encoding.UTF8.GetString(payload);
                text = ParseTextFromJson(json);
                isFinal = (flags & FlagServerFinalMask) == FlagServerFinalMask;
                return true;
            }
        }
        else if (msgType == MsgTypeErrorServer)
        {
            if (data.Length < offset + 8) return false;
            var code = ReadUInt32BE(data, offset);
            var size = ReadUInt32BE(data, offset + 4);
            var start = offset + 8;
            var end = Math.Min(data.Length, start + size);
            var msg = start < end ? Encoding.UTF8.GetString(data, start, end - start) : "";
            var lower = msg.ToLowerInvariant();
            if (code == 45000000 && (lower.Contains("decode ws request failed") || lower.Contains("unable to decode")))
            {
                return false; // 忽略该类非致命错误
            }
            error = $"ASR Error {code}: {msg}";
            return true;
        }

        return false;
    }

    private static string ParseTextFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return "";
            if (!result.TryGetProperty("text", out var textEl)) return "";
            return textEl.GetString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 构建“完整请求”JSON（尽量保持纯函数，便于排查与复用）。
    /// </summary>
    public static string BuildFullClientRequestJson(string uid, int sampleRate, bool enableDdc)
    {
        var rate = sampleRate <= 0 ? 16000 : sampleRate;
        var root = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                // 重要：uid 建议用 AppKey，便于服务端统计与排查
                ["uid"] = uid
            },
            ["audio"] = new Dictionary<string, object?>
            {
                ["format"] = "pcm",
                ["rate"] = rate,
                ["bits"] = 16,
                ["channel"] = 1
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

    private static async Task SendFrameAsync(
        ClientWebSocket ws,
        int messageType,
        int flags,
        int serialization,
        int compression,
        byte[] payload,
        CancellationToken ct)
    {
        var frame = BuildClientFrame(messageType, flags, serialization, compression, payload);
        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 组装二进制协议帧：
    /// header(4B) + payloadSize(4B, BE) + payload。
    /// header 的 4 个字节分别包含：协议版本/头长度、消息类型/标志、序列化/压缩标记、保留位。
    /// </summary>
    private static ArraySegment<byte> BuildClientFrame(
        int messageType,
        int flags,
        int serialization,
        int compression,
        byte[] payload)
    {
        var header = new byte[4];
        header[0] = (byte)(((ProtocolVersion & 0x0F) << 4) | (HeaderSizeUnits & 0x0F));
        header[1] = (byte)(((messageType & 0x0F) << 4) | (flags & 0x0F));
        header[2] = (byte)(((serialization & 0x0F) << 4) | (compression & 0x0F));
        header[3] = 0;

        var size = new byte[4];
        size[0] = (byte)((payload.Length >> 24) & 0xFF);
        size[1] = (byte)((payload.Length >> 16) & 0xFF);
        size[2] = (byte)((payload.Length >> 8) & 0xFF);
        size[3] = (byte)(payload.Length & 0xFF);

        var frame = new byte[header.Length + size.Length + payload.Length];
        Buffer.BlockCopy(header, 0, frame, 0, header.Length);
        Buffer.BlockCopy(size, 0, frame, header.Length, size.Length);
        Buffer.BlockCopy(payload, 0, frame, header.Length + size.Length, payload.Length);
        return new ArraySegment<byte>(frame);
    }

    private static int ReadUInt32BE(byte[] arr, int offset)
    {
        return (arr[offset] << 24) | (arr[offset + 1] << 16) | (arr[offset + 2] << 8) | arr[offset + 3];
    }

    private static byte[] Gzip(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(data);
        }
        return ms.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            // 兜底：解压失败则按原始数据处理，避免直接中断识别流程
            return data;
        }
    }
}
