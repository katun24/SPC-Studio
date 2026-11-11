using Avalonia.Controls;
using SpcStudio.Controls;
using SpcStudio.Import;
using SpcStudio.Web;

namespace SpcStudio;

// A song taken from SMWCentral by its number or its page's address - the box asking for it, and its download and unpacking into the install - which Import From SMWCentral
// and the song list's Add From SMWCentral share.
public static class SmwcSong
{
    public const int StageCount = DownloadStageCount + MmlOrZip.ZipStageCount;
    private const int DownloadStageCount = 1;
    private const string SongDownloadedStage = "song downloaded";

    // The box asking for a song under a title, its confirming button saying given words, which can use a song number or a song page's address.
    public static TextPrompt Prompt(string title, string confirmText)
    {
        return new TextPrompt(title, QuestionsAndNotices.EnterSmwcSong, FieldChars.WebAddress, confirmText, text => SmwcSongReference.TryReadNumber(text, out _));
    }

    // The number of the song typed into a box over a window, or null where the box was closed.
    public static async Task<int?> AskForNumber(Window owner, TextPrompt prompt)
    {
        if (await TextPromptDialog.Show(owner, prompt, null) is not { } typedSong || !SmwcSongReference.TryReadNumber(typedSong, out int songNumber))
        {
            return null;
        }

        return songNumber;
    }

    // Downloads a song into a zip and unpacks it into the install, asking in a box over a window before overwriting samples and failing as an unpacking once the
    // download is done, and answers the .txt it put there; the zip is deleted either way.
    public static string MmlPathOf(int songNumber, string? amkRoot, Window owner, EqualStepsProgress steps, Action<FailureTitle> setFailureTitle, CancellationToken cancel)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), songNumber + SongZip.Extension);
        try
        {
            SmwcSongDownload.ToZip(new SmwcApi(AppConnection.Http), songNumber, zipPath, cancel);
            steps.Report(SongDownloadedStage, DownloadStageCount, DownloadStageCount);
            setFailureTitle(WindowTitles.Failures.FailedToExtractSong);
            return MmlOrZip.MmlPathOf(zipPath, amkRoot, owner, steps.Report, cancel);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }
}
