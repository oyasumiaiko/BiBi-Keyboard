using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BiBiVoiceWin;

public enum AutoStopReason
{
    Silence = 1,
    MaxDuration = 2
}

public sealed record RecorderOptions(
    int TargetSampleRate,
    int MaxRecordSeconds,
    bool AutoStopEnabled,
    int AutoStopSilenceMs,
    double AutoStopThresholdDb
);

/// <summary>
/// 录音结果：
/// - Pcm16Bytes：16-bit PCM / 单声道 / 目标采样率（用于流式识别）
/// - WavBytes：封装后的 WAV（用于文件识别或调试）
/// </summary>
public sealed record RecordedAudio(byte[] Pcm16Bytes, byte[] WavBytes, int SampleRate, TimeSpan Duration);

/// <summary>
/// 麦克风录音器：
/// - 使用 WASAPI 共享模式采集默认麦克风
/// - 结束后离线转换为：16-bit PCM + 单声道 + 指定采样率，再封装成 WAV
/// - 提供简单的“静音判停”触发（由上层决定是否真正 stop）
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private WasapiCapture? _capture;
    private MemoryStream? _rawBuffer;
    private WaveFormat? _inputFormat;
    private TaskCompletionSource<bool>? _stopTcs;
    private bool _disposed;

    private RecorderOptions? _options;
    private DateTimeOffset _startedAt;
    private bool _speechDetected;
    private DateTimeOffset _lastSpeechAt;
    private bool _autoStopFired;

    public bool IsRecording { get; private set; }

    public event Action<AutoStopReason>? AutoStopRequested;

    public void Start(RecorderOptions options)
    {
        ThrowIfDisposed();
        if (IsRecording) return;

        _options = options;
        _rawBuffer = new MemoryStream();
        _stopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _startedAt = DateTimeOffset.UtcNow;
        _speechDetected = false;
        _lastSpeechAt = _startedAt;
        _autoStopFired = false;

        // 使用默认输入设备（共享模式）。这是“最小可运行形态”下最通用的方案。
        var capture = new WasapiCapture();
        _capture = capture;
        _inputFormat = capture.WaveFormat;

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        capture.StartRecording();

        IsRecording = true;
    }

    public async Task<RecordedAudio> StopAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        if (!IsRecording) throw new InvalidOperationException("当前未在录音");
        if (_capture is null || _rawBuffer is null || _inputFormat is null || _stopTcs is null || _options is null)
        {
            throw new InvalidOperationException("录音器内部状态异常");
        }

        IsRecording = false;

        try
        {
            _capture.StopRecording();
        }
        catch
        {
            // 某些设备/驱动可能在 StopRecording 抛异常，但 RecordingStopped 仍可能被触发；
            // 为了让上层更稳妥，这里继续等待 stopTcs。
        }

        using (ct.Register(() => _stopTcs.TrySetCanceled(ct)))
        {
            await _stopTcs.Task.ConfigureAwait(false);
        }

        var raw = _rawBuffer.ToArray();
        var duration = DateTimeOffset.UtcNow - _startedAt;

        var targetRate = _options.TargetSampleRate;
        var pcm16 = AudioTranscoder.ToPcm16Mono(raw, _inputFormat, targetRate);
        var wav = WavUtils.Pcm16ToWav(pcm16, targetRate, channels: 1);

        CleanupCapture();
        return new RecordedAudio(pcm16, wav, targetRate, duration);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_rawBuffer is null || _inputFormat is null || _options is null) return;
        if (e.BytesRecorded <= 0) return;

        _rawBuffer.Write(e.Buffer, 0, e.BytesRecorded);

        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _startedAt;

        // 最大时长保护：避免误操作导致录音无限增长（最小版也尽量别“炸内存”）。
        if (!_autoStopFired && _options.MaxRecordSeconds > 0 && elapsed.TotalSeconds >= _options.MaxRecordSeconds)
        {
            _autoStopFired = true;
            AutoStopRequested?.Invoke(AutoStopReason.MaxDuration);
            return;
        }

        if (!_options.AutoStopEnabled || _autoStopFired) return;

        var db = AudioLevelMeter.EstimateDbfs(e.Buffer.AsSpan(0, e.BytesRecorded), _inputFormat);
        if (db >= _options.AutoStopThresholdDb)
        {
            _speechDetected = true;
            _lastSpeechAt = now;
            return;
        }

        // 只有“检测到过说话”后才启用静音判停，避免刚按下热键就因为环境安静而立刻停。
        if (_speechDetected && (now - _lastSpeechAt).TotalMilliseconds >= _options.AutoStopSilenceMs)
        {
            _autoStopFired = true;
            AutoStopRequested?.Invoke(AutoStopReason.Silence);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopTcs?.TrySetResult(true);
    }

    private void CleanupCapture()
    {
        if (_capture is null) return;
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _capture = null;
        _inputFormat = null;
        _stopTcs = null;
        _rawBuffer?.Dispose();
        _rawBuffer = null;
        _options = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioRecorder));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { CleanupCapture(); } catch { }
    }
}

internal static class AudioLevelMeter
{
    /// <summary>
    /// 估算当前缓冲区的 dBFS（相对满刻度分贝）。
    /// - 仅用于“静音判停”，不追求极致精度
    /// - 输入可能是 IEEE float 或 PCM
    /// </summary>
    public static double EstimateDbfs(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        if (buffer.IsEmpty) return double.NegativeInfinity;

        // 统一计算 RMS，再转 dBFS。0 视为 -inf。
        double sumSquares = 0;
        long sampleCount = 0;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var floatCount = buffer.Length / 4;
            for (var i = 0; i < floatCount; i++)
            {
                var f = BitConverter.ToSingle(buffer.Slice(i * 4, 4));
                sumSquares += f * f;
            }
            sampleCount = floatCount;
        }
        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var sample16Count = buffer.Length / 2;
            for (var i = 0; i < sample16Count; i++)
            {
                var s = BitConverter.ToInt16(buffer.Slice(i * 2, 2));
                var f = s / 32768.0;
                sumSquares += f * f;
            }
            sampleCount = sample16Count;
        }
        else
        {
            // 兜底：未知格式时不触发静音判停（直接视为一直有声音），避免误停。
            return 0;
        }

        if (sampleCount <= 0) return double.NegativeInfinity;
        var rms = Math.Sqrt(sumSquares / sampleCount);
        if (rms <= 0) return double.NegativeInfinity;
        return 20.0 * Math.Log10(rms);
    }
}

internal static class AudioTranscoder
{
    /// <summary>
    /// 将任意输入格式音频转换为：16-bit PCM / mono / 指定采样率。
    /// 说明：
    /// - 这里选择“录完再转”的方式，代码更短也更稳定，符合“最小可运行形态”。
    /// - 若后续要做流式识别/实时波形，可改成边录边转的管线。
    /// </summary>
    public static byte[] ToPcm16Mono(byte[] inputBytes, WaveFormat inputFormat, int targetSampleRate)
    {
        using var ms = new MemoryStream(inputBytes);
        using var raw = new RawSourceWaveStream(ms, inputFormat);

        // 转为 float sample 便于做：混音/重采样
        ISampleProvider sampleProvider = raw.ToSampleProvider();

        // 多声道 -> 单声道（默认取左右平均；如果是 1 声道则不处理）
        if (sampleProvider.WaveFormat.Channels == 2)
        {
            sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }
        else if (sampleProvider.WaveFormat.Channels > 2)
        {
            // 极少见：>2 声道时直接取第一个声道，避免复杂化最小版
            var mux = new MultiplexingSampleProvider(new[] { sampleProvider }, 1);
            mux.ConnectInputToOutput(0, 0);
            sampleProvider = mux;
        }

        // 重采样到 ASR 推荐采样率（Android 端默认 16k）
        if (sampleProvider.WaveFormat.SampleRate != targetSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, targetSampleRate);
        }

        // float -> PCM16（小端）
        var wave16 = new SampleToWaveProvider16(sampleProvider);
        using var outMs = new MemoryStream();
        var buf = new byte[wave16.WaveFormat.AverageBytesPerSecond]; // 约 1 秒数据
        int read;
        while ((read = wave16.Read(buf, 0, buf.Length)) > 0)
        {
            outMs.Write(buf, 0, read);
        }

        return outMs.ToArray();
    }
}
