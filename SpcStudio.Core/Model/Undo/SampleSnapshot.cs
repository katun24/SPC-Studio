using SpcStudio.Audio;

namespace SpcStudio.Model;

// The song's samples captured whole, with the view of the sample editor they were edited through.
public sealed class SampleSnapshot : IUndoState<SampleSnapshot>
{
    private readonly Sample[] _samples;

    private SampleSnapshot(Sample[] samples, SampleView view)
    {
        _samples = samples;
        View = view;
    }

    public SampleView View { get; }

    // Copies the song's samples into a snapshot, seen through a view.
    public static SampleSnapshot Capture(Song song, SampleView view)
    {
        return new([.. song.Samples], view);
    }

    // Puts these samples back into the live song.
    public void Restore(Song song)
    {
        song.SetSamples(_samples);
    }

    // The same samples seen through another view.
    public SampleSnapshot SeenThrough(SampleView view)
    {
        return new(_samples, view);
    }

    // Whether the two hold the same samples, the view aside.
    public bool HoldsSameStateAs(SampleSnapshot other)
    {
        return _samples.SequenceEqual(other._samples, ReferenceEqualityComparer.Instance);
    }
}
