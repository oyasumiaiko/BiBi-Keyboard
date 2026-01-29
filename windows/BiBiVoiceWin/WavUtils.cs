namespace BiBiVoiceWin;

internal static class WavUtils
{
    /// <summary>
    /// 将 PCM16（小端）音频封装成 WAV。
    /// 这段逻辑直接对齐 Android 端 BaseFileAsrEngine.pcmToWav()，便于跨端对照与复用。
    /// </summary>
    public static byte[] Pcm16ToWav(ReadOnlySpan<byte> pcm, int sampleRate, short channels)
    {
        const short bitsPerSample = 16;
        const int headerSize = 44;

        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var dataSize = pcm.Length;
        var totalDataLen = dataSize + 36;

        var outBytes = new byte[headerSize + dataSize];
        using var ms = new MemoryStream(outBytes);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(totalDataLen);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16); // PCM
        bw.Write((short)1); // AudioFormat=1(PCM)
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * bitsPerSample / 8)); // block align
        bw.Write(bitsPerSample);
        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);
        bw.Write(pcm);

        return outBytes;
    }
}

