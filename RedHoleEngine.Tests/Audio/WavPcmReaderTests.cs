using RedHoleEngine.Audio;

namespace RedHoleEngine.Tests.Audio;

public class WavPcmReaderTests
{
    private static string Write(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"redhole-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] PcmWav(short channels, int sampleRate, short bits, byte[] pcm, short extraFmt = 0, byte[]? unknown = null)
    {
        int fmtSize = 16 + extraFmt;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(0); // patched later
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(fmtSize);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        int bytesPerFrame = Math.Max(1, channels * (bits / 8));
        w.Write(sampleRate * bytesPerFrame);
        w.Write((short)bytesPerFrame);
        w.Write(bits);
        if (extraFmt > 0)
            w.Write(new byte[extraFmt]);
        if (unknown != null)
        {
            w.Write("fact"u8);
            w.Write(unknown.Length);
            w.Write(unknown);
            if (unknown.Length % 2 == 1)
                w.Write((byte)0);
        }
        w.Write("data"u8);
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Seek(4, SeekOrigin.Begin);
        w.Write((int)(ms.Length - 8));
        return ms.ToArray();
    }

    [Fact]
    public void Load_Stereo16Bit_ReturnsSamplesAndFormat()
    {
        byte[] pcm = { 1, 2, 3, 4, 5, 6, 7, 8 }; // 2 frames, stereo, 16-bit
        var path = Write(PcmWav(2, 22050, 16, pcm, extraFmt: 2, unknown: new byte[] { 9 }));
        try
        {
            var (data, format) = WavPcmReader.Load(path);
            Assert.Equal(pcm, data);
            Assert.Equal(2, format.Channels);
            Assert.Equal(22050, format.SampleRate);
            Assert.Equal(16, format.BitsPerSample);
            Assert.Equal(2, format.TotalSamples);
            Assert.Equal(2f / 22050f, format.Duration, 1e-6f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var (data, _) = WavPcmReader.Load(Path.Combine(Path.GetTempPath(), "no-such-redhole.wav"));
        Assert.Null(data);
    }

    [Fact]
    public void Load_RejectsNonRiff_NonWave_AndMissingData()
    {
        var junk = Write("not a wav file!!"u8.ToArray());
        var waveMissing = Write(BuildHeaderOnly("RIFF", "NOPE"));
        var noData = Write(BuildFmtOnly());
        try
        {
            Assert.Null(WavPcmReader.Load(junk).data);
            Assert.Null(WavPcmReader.Load(waveMissing).data);
            Assert.Null(WavPcmReader.Load(noData).data);
        }
        finally
        {
            File.Delete(junk);
            File.Delete(waveMissing);
            File.Delete(noData);
        }
    }

    [Fact]
    public void Load_DataWithoutFmt_DoesNotDivideByZero()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(0);
            w.Write("WAVE"u8);
            w.Write("data"u8);
            w.Write(4);
            w.Write(new byte[] { 1, 2, 3, 4 });
        }
        var path = Write(ms.ToArray());
        try
        {
            var (data, _) = WavPcmReader.Load(path);
            Assert.Null(data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AudioHandles_EqualityAndFormatDuration()
    {
        var clip = new AudioClipHandle(3);
        Assert.True(clip.IsValid);
        Assert.Equal(clip, new AudioClipHandle(3));
        Assert.NotEqual(clip, new AudioClipHandle(4));
        Assert.True(clip == new AudioClipHandle(3));
        Assert.True(new AudioClipHandle(1) != new AudioClipHandle(2));
        Assert.Equal(clip.GetHashCode(), new AudioClipHandle(3).GetHashCode());
        Assert.False(clip.Equals("nope"));

        var voice = new VoiceHandle(8);
        Assert.True(voice.IsValid);
        Assert.Equal(voice, new VoiceHandle(8));
        Assert.False(new VoiceHandle(0).IsValid);
        Assert.True(voice != default);
        Assert.False(voice.Equals(null));

        var format = new AudioFormat { SampleRate = 1000, TotalSamples = 250, Channels = 1, BitsPerSample = 8 };
        Assert.Equal(0.25f, format.Duration, 1e-6f);
    }

    private static byte[] BuildHeaderOnly(string riff, string wave)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes(riff));
        w.Write(4);
        w.Write(System.Text.Encoding.ASCII.GetBytes(wave));
        return ms.ToArray();
    }

    private static byte[] BuildFmtOnly()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(0);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(8000);
        w.Write(8000);
        w.Write((short)1);
        w.Write((short)8);
        return ms.ToArray();
    }
}
