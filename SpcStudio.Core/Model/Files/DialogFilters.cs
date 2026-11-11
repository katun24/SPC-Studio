namespace SpcStudio.Model;

// The file types a file picker offers that more than one picker shares, each written as the picker's filter reads it.
public static class DialogFilters
{
    public const string Separator = "|";
    public const string AllFiles = "All files (*.*)|*.*";
    public const string BrrSample = "BRR sample (*.brr)|*.brr";
    public const string WavAudio = "WAV audio (*.wav)|*.wav";
}
