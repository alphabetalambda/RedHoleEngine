namespace RedHoleEngine.Audio;

/// <summary>
/// Reads uncompressed PCM WAV bytes. Extracted from the OpenAL backend so clip
/// parsing can be tested without opening an audio device. Messages and failure
/// results match the previous in-backend loader.
/// </summary>
public static class WavPcmReader
{
    public static (byte[]? data, AudioFormat format) Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"OpenAL: File not found: {path}");
            return (null, default);
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        // RIFF header
        var riff = new string(reader.ReadChars(4));
        if (riff != "RIFF")
        {
            Console.WriteLine($"OpenAL: Invalid WAV file (no RIFF header): {path}");
            return (null, default);
        }

        reader.ReadInt32(); // File size

        var wave = new string(reader.ReadChars(4));
        if (wave != "WAVE")
        {
            Console.WriteLine($"OpenAL: Invalid WAV file (no WAVE header): {path}");
            return (null, default);
        }

        // Find fmt chunk
        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? audioData = null;

        while (stream.Position < stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                reader.ReadInt16(); // 1 = PCM
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // Byte rate
                reader.ReadInt16(); // Block align
                bitsPerSample = reader.ReadInt16();

                // Skip any extra format bytes
                if (chunkSize > 16)
                    reader.ReadBytes(chunkSize - 16);
            }
            else if (chunkId == "data")
            {
                audioData = reader.ReadBytes(chunkSize);
            }
            else
            {
                // Skip unknown chunk
                reader.ReadBytes(chunkSize);
            }

            // Align to word boundary
            if (chunkSize % 2 == 1 && stream.Position < stream.Length)
                reader.ReadByte();
        }

        if (audioData == null)
        {
            Console.WriteLine($"OpenAL: No audio data found in: {path}");
            return (null, default);
        }

        int bytesPerFrame = channels * (bitsPerSample / 8);
        if (bytesPerFrame <= 0)
        {
            // A data chunk with no valid fmt used to divide by zero. Callers
            // (OpenALBackend.LoadClip) already treat a failed parse as "no clip".
            Console.WriteLine($"OpenAL: Invalid WAV format in: {path}");
            return (null, default);
        }

        var format = new AudioFormat
        {
            Channels = channels,
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample,
            TotalSamples = audioData.Length / bytesPerFrame
        };

        return (audioData, format);
    }
}
