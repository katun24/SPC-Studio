namespace SpcStudio.Audio;

// The output device, behind the one interface the rest of the app knows it by.
public interface IAudioOutput : IDisposable
{
    // The rate the device runs at, known as soon as the backend exists.
    public int SampleRate { get; }

    // Starts pulling from a source, once.
    public void Start(IAudioSource source);

    // Stops pulling, which disposing does too.
    public void Stop();
}
