namespace SpcStudio.Audio;

// Something an output device can pull interleaved stereo audio out of.
public interface IAudioSource
{
    public const int ChannelsPerFrame = 2;

    // Fills a run of the buffer - counted in samples, not in frames - and answers how many it wrote.
    public int Read(float[] buffer, int offset, int count);
}
