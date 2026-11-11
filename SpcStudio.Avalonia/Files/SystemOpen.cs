using System.Diagnostics;
using Avalonia.Controls;

namespace SpcStudio;

// Opening a file in whatever program the system opens its kind with.
internal static class SystemOpen
{
    // Hands a file to the system's program for its kind, saying in a box over a window under a failure title where it could not be opened.
    public static async Task OpenOrSayFailure(Window owner, string path, FailureTitle failureTitle)
    {
        await MessageDialog.TryOrSayFailure(owner, () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }), failureTitle);
    }
}
