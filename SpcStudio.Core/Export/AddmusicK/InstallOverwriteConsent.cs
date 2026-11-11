namespace SpcStudio.Export;

// The questions a copy into the install asks before writing over files already there with other bytes: one about the song, then one about all its samples.
public sealed class InstallOverwriteConsent
{
    public static readonly InstallOverwriteConsent AgreeingToAll = new(_ => true, _ => true);

    private readonly Func<string, bool> _maySongBeOverwritten;
    private readonly Func<IReadOnlyList<string>, bool> _maySamplesBeOverwritten;

    // The questions, each answering whether the files it names may be overwritten: the song by its file name, the samples by their paths under the samples folder.
    public InstallOverwriteConsent(Func<string, bool> maySongBeOverwritten, Func<IReadOnlyList<string>, bool> maySamplesBeOverwritten)
    {
        _maySongBeOverwritten = maySongBeOverwritten;
        _maySamplesBeOverwritten = maySamplesBeOverwritten;
    }

    // Asks about the song where it would be overwritten and then about the samples where any would be, stopping as cancelled at the first no.
    public void AskOrCancel(string? overwrittenSongName, IReadOnlyList<string> overwrittenSampleNames)
    {
        if (overwrittenSongName is not null && !_maySongBeOverwritten(overwrittenSongName))
        {
            throw new OperationCanceledException();
        }

        if (overwrittenSampleNames.Count > 0 && !_maySamplesBeOverwritten(overwrittenSampleNames))
        {
            throw new OperationCanceledException();
        }
    }
}
