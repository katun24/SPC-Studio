using System.IO;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Writing 16-bit PCM .wav files at the DSP's own rate: the mixer's levels as points, and the canonical header with the points behind it.
public static class WavWriter
{
    public const string DialogFilter = DialogFilters.WavAudio + DialogFilters.Separator + DialogFilters.AllFiles;
    public const int SampleRate = (int)DspPoint.SampleRate;
    private const int HeaderBytesAfterRiffSize = 36;

    // A mixer level as a 16-bit point, full scale being the largest positive point.
    public static short ToPoint(float level)
    {
        return DspPoint.Rounded(level * short.MaxValue);
    }

    // Writes the canonical PCM header and the first points given behind it, little-endian, in frames of the channel count given.
    public static void Write16Bit(string path, short[] points, int pointCount, int channelsPerFrame)
    {
        int dataBytes = pointCount * DspPoint.BytesPerPoint;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        WriteChunkName(writer, WavFormat.RiffChunkName);
        writer.Write(HeaderBytesAfterRiffSize + dataBytes);
        WriteChunkName(writer, WavFormat.WaveFormName);

        WriteChunkName(writer, WavFormat.FormatChunkName);
        writer.Write(WavFormat.FormatChunkBytes);
        writer.Write((short)WavFormat.PcmFormatTag);
        writer.Write((short)channelsPerFrame);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channelsPerFrame * DspPoint.BytesPerPoint);
        writer.Write((short)(channelsPerFrame * DspPoint.BytesPerPoint));
        writer.Write((short)(DspPoint.BytesPerPoint * ByteUnits.BitsPerByte));

        WriteChunkName(writer, WavFormat.DataChunkName);
        writer.Write(dataBytes);

        writer.Write(DspPoint.ToLittleEndianBytes(points.AsSpan(0, pointCount)));
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
