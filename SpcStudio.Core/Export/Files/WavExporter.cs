using System.Buffers.Binary;
using System.IO;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Renders a song to a 16-bit stereo .wav at the DSP's own rate, through a mixer of its own fed the same events playback is.
public static class WavExporter
{
    public const string DialogFilter = DialogFilters.WavAudio + DialogFilters.Separator + DialogFilters.AllFiles;
    public const int SampleRate = (int)DspPoint.SampleRate;
    private const double TailSeconds = 3.0;
    private const int ChunkFrames = 4096;
    private const int BytesPerPoint = sizeof(short);
    private const int HeaderBytesAfterRiffSize = 36;

    // Renders the song once from tick 0 to its end, reporting seconds rendered out of seconds to render, and writes the file only once all of it is rendered, so a cancelled render writes nothing.
    public static void Write(Song song, string path, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        var (points, frameCount) = Render(song, progress, cancel);

        cancel.ThrowIfCancellationRequested();

        Write16BitStereoWav(path, points, frameCount * IAudioSource.ChannelsPerFrame);
    }

    // The song's interleaved points with the tail's trailing silence taken off, and how many frames are left.
    private static (short[] Points, int FrameCount) Render(Song song, Action<int, int>? progress, CancellationToken cancel)
    {
        var (events, songFrames) = SequenceBuilder.BuildIgnoringMuting(song, song.PlaybackEndTick(0));

        var mixer = new BrrMixer(SampleRate);
        mixer.StartSequence(events, songFrames);

        long tailFrames = (long)(TailSeconds * SampleRate);
        int renderFrames = (int)Math.Min(songFrames + tailFrames, int.MaxValue / IAudioSource.ChannelsPerFrame);
        var points = new short[renderFrames * IAudioSource.ChannelsPerFrame];
        var chunkLevels = new float[ChunkFrames * IAudioSource.ChannelsPerFrame];

        int totalSeconds = Math.Max(1, renderFrames / SampleRate);
        progress?.Invoke(0, totalSeconds);

        for (int frame = 0; frame < renderFrames; frame += ChunkFrames)
        {
            cancel.ThrowIfCancellationRequested();

            int chunkFrames = Math.Min(ChunkFrames, renderFrames - frame);
            mixer.Read(chunkLevels, 0, chunkFrames * IAudioSource.ChannelsPerFrame);

            for (int index = 0; index < chunkFrames * IAudioSource.ChannelsPerFrame; index++)
            {
                points[frame * IAudioSource.ChannelsPerFrame + index] = ToPoint(chunkLevels[index]);
            }

            progress?.Invoke(Math.Min((frame + chunkFrames) / SampleRate, totalSeconds), totalSeconds);
        }

        int frameCount = renderFrames;
        while (frameCount > songFrames && IsSilentFrame(points, frameCount - 1))
        {
            frameCount--;
        }

        return (points, frameCount);
    }

    // Whether both channels of a frame are exactly zero.
    private static bool IsSilentFrame(short[] points, int frame)
    {
        for (int channel = 0; channel < IAudioSource.ChannelsPerFrame; channel++)
        {
            if (points[frame * IAudioSource.ChannelsPerFrame + channel] != 0)
            {
                return false;
            }
        }

        return true;
    }

    // A mixer level as a 16-bit point, full scale being the largest positive point.
    private static short ToPoint(float level)
    {
        return DspPoint.Rounded(level * short.MaxValue);
    }

    // Writes the canonical PCM header and the points behind it, little-endian.
    private static void Write16BitStereoWav(string path, short[] points, int pointCount)
    {
        int dataBytes = pointCount * BytesPerPoint;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        WriteChunkName(writer, WavFormat.RiffChunkName);
        writer.Write(HeaderBytesAfterRiffSize + dataBytes);
        WriteChunkName(writer, WavFormat.WaveFormName);

        WriteChunkName(writer, WavFormat.FormatChunkName);
        writer.Write(WavFormat.FormatChunkBytes);
        writer.Write((short)WavFormat.PcmFormatTag);
        writer.Write((short)IAudioSource.ChannelsPerFrame);
        writer.Write(SampleRate);
        writer.Write(SampleRate * IAudioSource.ChannelsPerFrame * BytesPerPoint);
        writer.Write((short)(IAudioSource.ChannelsPerFrame * BytesPerPoint));
        writer.Write((short)(BytesPerPoint * ByteUnits.BitsPerByte));

        WriteChunkName(writer, WavFormat.DataChunkName);
        writer.Write(dataBytes);

        var dataChunk = new byte[dataBytes];
        for (int index = 0; index < pointCount; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(dataChunk.AsSpan(index * BytesPerPoint), points[index]);
        }

        writer.Write(dataChunk);
    }

    // Writes a RIFF chunk name as its four ASCII bytes.
    private static void WriteChunkName(BinaryWriter writer, string chunkName)
    {
        foreach (char character in chunkName)
        {
            writer.Write((byte)character);
        }
    }
}
