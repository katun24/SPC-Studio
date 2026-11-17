namespace SpcStudio.Audio;

// What reading a .wav and writing one share of its layout: the chunk names, the uncompressed format's tag and the plain format chunk's size.
public static class WavFormat
{
    public const string RiffChunkName = "RIFF";
    public const string WaveFormName = "WAVE";
    public const string FormatChunkName = "fmt ";
    public const string DataChunkName = "data";
    public const int PcmFormatTag = 1;
    public const int FormatChunkBytes = 16;
}
