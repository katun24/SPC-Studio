using Avalonia.Controls;
using SpcStudio.Import;

namespace SpcStudio;

// A song picked as a .txt or as a zip of one with its samples, which is how Import MML and the song list's Add Song both take a shared song's zip.
public static class MmlOrZip
{
    private const string ExtractingLine = "Extracting song...";
    private const string FilesUnpackedStage = "files unpacked";

    // How many stages finding a picked file's .txt reports: the unpacking for a zip, and none for a .txt.
    public static int StageCountOf(string pickedPath)
    {
        return SongZip.IsZip(pickedPath) ? 1 : 0;
    }

    // The .txt a picked file is: the file itself, or for a zip the .txt it unpacks into the install.
    public static string MmlPathOf(string pickedPath, string? amkRoot, Action<string, int, int>? progress, CancellationToken cancel)
    {
        if (!SongZip.IsZip(pickedPath))
        {
            return pickedPath;
        }

        return Unpack(pickedPath, amkRoot, progress, cancel);
    }

    // The .txt a picked file is, found behind a progress box over a window where it is a zip to unpack, and at once with no box otherwise.
    public static async Task<ProgressDialog.JobRun<string>> MmlPathBehindProgressForZip(Window owner, string title, string pickedPath, string? amkRoot)
    {
        if (SongZip.IsZip(pickedPath))
        {
            return await ProgressDialog.RunUnderOneLine(owner, title, ExtractingLine,
                (reportDoneFraction, cancel) => MmlPathOf(pickedPath, amkRoot, new EqualStepsProgress(StageCountOf(pickedPath), 0, reportDoneFraction).Report, cancel), ex => ex.Message);
        }

        return new ProgressDialog.JobRun<string>(MmlPathOf(pickedPath, amkRoot, null, CancellationToken.None), null, false);
    }

    // Unpacks a zip into the install, counting the files unpacked, and answers the .txt it put there.
    private static string Unpack(string zipPath, string? amkRoot, Action<string, int, int>? progress, CancellationToken cancel)
    {
        return SongZip.Extract(zipPath, amkRoot, (done, total) => progress?.Invoke(FilesUnpackedStage, done, total), cancel);
    }
}
