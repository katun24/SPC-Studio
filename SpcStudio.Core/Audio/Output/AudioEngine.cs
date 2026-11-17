namespace SpcStudio.Audio;

// Owns the output device and the mixer that feeds it.
public sealed class AudioEngine : IDisposable
{
    private IAudioOutput? _output;

    // Opens the mixer at the device's own rate and starts the device pulling from it.
    public AudioEngine(IAudioOutput output)
    {
        _output = output;
        SampleRate = output.SampleRate;
        Mixer = new BrrMixer(SampleRate);

        output.Start(Mixer);
    }

    public BrrMixer Mixer { get; }
    public int SampleRate { get; }

    // Stops the device and lets it go.
    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
    }
}
