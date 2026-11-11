using Avalonia.Controls;
using Avalonia.Threading;
using SpcStudio.Export;
using SpcStudio.Import;

namespace SpcStudio;

// A song picked as a .txt or as a zip of one with its samples, which is how Import MML and the song list's Add Song both take a shared song's zip.
public static class MmlOrZip
{
    public const int ZipStageCount = 1;
    private const string ExtractingLine = "Extracting song...";
    private const string FilesUnpackedStage = "files unpacked";

    // How many stages finding a picked file's .txt reports: the unpacking for a zip, and none for a .txt.
    public static int StageCountOf(string pickedPath)
    {
        return SongZip.IsZip(pickedPath) ? ZipStageCount : 0;
    }

    // The .txt a picked file is: the file itself, or for a zip the .txt it unpacks into the install, asking in a box over a window before overwriting samples.
    public static string MmlPathOf(string pickedPath, string? amkRoot, Window owner, Action<string, int, int>? progress, CancellationToken cancel)
    {
        if (!SongZip.IsZip(pickedPath))
        {
            return pickedPath;
        }

        return Unpack(pickedPath, amkRoot, owner, progress, cancel);
    }

    // The .txt a picked file is, found behind a progress box over a window where it is a zip to unpack, and at once with no box otherwise.
    public static async Task<ProgressDialog.JobRun<string>> MmlPathBehindProgressForZip(Window owner, string title, string pickedPath, string? amkRoot)
    {
        if (SongZip.IsZip(pickedPath))
        {
            return await ProgressDialog.RunUnderOneLine(owner, title, ExtractingLine,
                (reportDoneFraction, cancel) => MmlPathOf(pickedPath, amkRoot, owner, new EqualStepsProgress(StageCountOf(pickedPath), 0, reportDoneFraction).Report, cancel),
                ex => ex.Message);
        }

        return new ProgressDialog.JobRun<string>(MmlPathOf(pickedPath, amkRoot, owner, null, CancellationToken.None), null, false);
    }

    // The questions a job copying a song into the install asks in boxes over a window before overwriting the song or its samples already there with other bytes.
    public static InstallOverwriteConsent OverwriteQuestionsOver(Window owner)
    {
        return new InstallOverwriteConsent(
            songName => IsYesFromJob(owner, QuestionsAndNotices.OverwriteMml(songName), WindowTitles.Notices.OverwriteMml),
            samplePaths => IsYesFromJob(owner, QuestionsAndNotices.OverwriteImportedSamples(samplePaths), WindowTitles.Notices.OverwriteSamples));
    }

    // Unpacks a zip into the install, asking in a box over a window which song to unpack where it holds more than one, counting the files unpacked, and
    // answers the .txt it put there or found there already.
    private static string Unpack(string zipPath, string? amkRoot, Window owner, Action<string, int, int>? progress, CancellationToken cancel)
    {
        return SongZip.Extract(zipPath, amkRoot, songNames => AnswerFromJob(() => MessageDialog.Choose(owner, QuestionsAndNotices.ChooseMmlFile, WindowTitles.Notices.ChooseMmlFile, songNames)),
            OverwriteQuestionsOver(owner), (done, total) => progress?.Invoke(FilesUnpackedStage, done, total), cancel);
    }

    // Asks a yes-or-no question in a box over a window from a job off the interface thread, waiting for the answer.
    private static bool IsYesFromJob(Window owner, string question, NoticeTitle title)
    {
        return AnswerFromJob(() => MessageDialog.IsAnsweredYes(owner, question, title));
    }

    // Shows a box from a job off the interface thread, waiting for its answer.
    private static T AnswerFromJob<T>(Func<Task<T>> showBox)
    {
        return Dispatcher.UIThread.InvokeAsync(showBox).GetAwaiter().GetResult();
    }
}
