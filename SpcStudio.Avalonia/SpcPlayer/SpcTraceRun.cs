using Avalonia.Controls;
using SpcStudio.Audio;
using SpcStudio.Import;

namespace SpcStudio;

// Plays an .spc through on an emulator behind a progress box.
public static class SpcTraceRun
{
    // Traces a dump behind a progress box over a window saying one line throughout, an emulator that cannot start being the one failure it puts into words.
    public static Task<ProgressDialog.JobRun<SpcTrace>> BehindProgress(Window owner, string title, string line, SpcFile file, CancellationToken otherStop = default)
    {
        return ProgressDialog.RunUnderOneLine(
            owner,
            title,
            line,
            (reportDoneFraction, cancel) => SpcTrace.Run(file.FileBytes, new EqualStepsProgress(SpcTrace.MaxPassCount, 0, reportDoneFraction).Report, cancel),
            ex => ex is InvalidOperationException or DllNotFoundException ? ex.Message : null,
            otherStop);
    }
}
