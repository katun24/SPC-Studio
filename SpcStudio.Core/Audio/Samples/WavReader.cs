using System.IO;
using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// A .wav read as mono points at the file's own rate and at the depth the BRR encoder takes, or refused with what about the file stops it.
public static class WavReader
{
    private const int FormatFloat = 3;
    private const int FormatExtensible = 0xFFFE;
    private const int RiffHeaderSize = 12;
    private const int ChunkHeaderSize = 8;
    private const int ExtensibleFormatChunkSize = 26;
    private const int ChannelsOffset = 2;
    private const int RateOffset = 4;
    private const int BitsOffset = 14;
    private const int SubFormatOffset = 24;
    private const int PointBits = 16;
    private const int UnsignedEightBitCenter = 128;
    private const int MaxChannels = 2;

    // A whole .wav as one mono point per frame, its channels averaged and its depth converted to the points' own, refused where it cannot be read.
    public static short[] ReadMonoPoints(byte[] fileBytes, string name = BrrSample.DefaultName)
    {
        var (format, channels, bits, _, dataAt, dataSize) = ReadFormatAndDataChunk(fileBytes, name);
        int bytesPerPoint = bits / ByteUnits.BitsPerByte;
        int frameSize = bytesPerPoint * channels;
        int frames = Math.Min(dataSize, fileBytes.Length - dataAt) / frameSize;
        if (frames == 0)
        {
            throw new InvalidDataException($"'{name}' contains no audio.");
        }

        var points = new short[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            int at = dataAt + frame * frameSize;
            double sum = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                sum += ReadChannelPoint(fileBytes, at + channel * bytesPerPoint, format, bits);
            }

            points[frame] = DspPoint.Rounded(sum / channels);
        }

        return points;
    }

    // The rate in hertz a .wav was written at, refused where the file cannot be read.
    public static int ReadRateHz(byte[] fileBytes, string name = BrrSample.DefaultName)
    {
        return ReadFormatAndDataChunk(fileBytes, name).Rate;
    }

    // The format chunk's fields and where the audio chunk is, found by walking the chunk list, or a refusal saying which of them the file failed on.
    private static (int Format, int Channels, int Bits, int Rate, int DataAt, int DataSize) ReadFormatAndDataChunk(byte[] fileBytes, string name)
    {
        if (fileBytes.Length < RiffHeaderSize || ChunkTagAt(fileBytes, 0) != WavFormat.RiffChunkName || ChunkTagAt(fileBytes, ChunkHeaderSize) != WavFormat.WaveFormName)
        {
            throw new InvalidDataException($"'{name}' is not a WAV file.");
        }

        int format = 0;
        int channels = 0;
        int bits = 0;
        int rate = 0;
        int dataAt = -1;
        int dataSize = 0;
        bool hasFormat = false;
        int at = RiffHeaderSize;
        while (at + ChunkHeaderSize <= fileBytes.Length)
        {
            string tag = ChunkTagAt(fileBytes, at);
            int size = Int32At(fileBytes, at + 4);
            int body = at + ChunkHeaderSize;
            if (size < 0 || body + size < body)
            {
                break;
            }

            if (tag == WavFormat.FormatChunkName && size >= WavFormat.FormatChunkBytes && body + size <= fileBytes.Length)
            {
                format = UInt16At(fileBytes, body);
                channels = UInt16At(fileBytes, body + ChannelsOffset);
                rate = Int32At(fileBytes, body + RateOffset);
                bits = UInt16At(fileBytes, body + BitsOffset);
                hasFormat = true;
                if (format == FormatExtensible && size >= ExtensibleFormatChunkSize)
                {
                    format = UInt16At(fileBytes, body + SubFormatOffset);
                }
            }
            else if (tag == WavFormat.DataChunkName)
            {
                dataAt = body;
                dataSize = size;
            }

            at = body + size + (size % 2);
        }

        RefuseUnreadable(name, hasFormat, dataAt >= 0, format, channels, rate, bits);
        return (format, channels, bits, rate, dataAt, dataSize);
    }

    // Refuses a file with no format or audio chunk, or whose format, channel count, rate or depth cannot be imported.
    private static void RefuseUnreadable(string name, bool hasFormat, bool hasData, int format, int channels, int rate, int bits)
    {
        if (!hasFormat)
        {
            throw new InvalidDataException($"'{name}' has no format chunk.");
        }

        if (!hasData)
        {
            throw new InvalidDataException($"'{name}' has no audio chunk.");
        }

        if (format != WavFormat.PcmFormatTag && format != FormatFloat)
        {
            throw new InvalidDataException($"'{name}' is compressed, and only uncompressed WAV files can be imported.");
        }

        if (channels < 1 || channels > MaxChannels)
        {
            throw new InvalidDataException($"'{name}' has {channels} channels, and only mono and stereo can be imported.");
        }

        if (rate <= 0)
        {
            throw new InvalidDataException($"'{name}' names a sample rate of {rate}.");
        }

        bool isDepthReadable = format == FormatFloat ? bits == 32 : bits is 8 or 16 or 24 or 32;
        if (!isDepthReadable)
        {
            throw new InvalidDataException($"'{name}' is {bits}-bit, which cannot be imported.");
        }
    }

    // One point of one channel on the points' own scale, not yet rounded or clamped.
    private static double ReadChannelPoint(byte[] fileBytes, int at, int format, int bits)
    {
        if (format == FormatFloat)
        {
            return BitConverter.ToSingle(fileBytes, at) * short.MaxValue;
        }

        double scale = Math.Pow(2, PointBits - bits);
        return bits switch
        {
            8 => (fileBytes[at] - UnsignedEightBitCenter) * scale,
            16 => (short)UInt16At(fileBytes, at),
            24 => (fileBytes[at] | (fileBytes[at + 1] << 8) | ((sbyte)fileBytes[at + 2] << 16)) * scale,
            _ => Int32At(fileBytes, at) * scale,
        };
    }

    // The four-character tag a chunk starts with.
    private static string ChunkTagAt(byte[] fileBytes, int at)
    {
        return Encoding.ASCII.GetString(fileBytes, at, 4);
    }

    // A little-endian unsigned 16-bit field.
    private static int UInt16At(byte[] fileBytes, int at)
    {
        return fileBytes[at] | (fileBytes[at + 1] << 8);
    }

    // A little-endian signed 32-bit field.
    private static int Int32At(byte[] fileBytes, int at)
    {
        return fileBytes[at] | (fileBytes[at + 1] << 8) | (fileBytes[at + 2] << 16) | (fileBytes[at + 3] << 24);
    }
}
