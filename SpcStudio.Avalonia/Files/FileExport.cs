using Avalonia.Controls;

namespace SpcStudio;

// Writing one file to a path the user picks and names in a save picker.
internal static class FileExport
{
    // Asks where to write one file, offering its own name, and writes it there, saying so only where it could not be written.
    public static async Task WriteOne(Window owner, FileExportKind kind, ExportedFile file)
    {
        string? path = await FilePickers.SaveFile(owner, kind.PickerTitle, kind.Filter, file.FileName, kind.Extension, kind.PickerId, kind.StartFolder);
        if (path is null)
        {
            return;
        }

        await MessageDialog.TryOrSayFailure(owner, () => file.WriteTo(path), kind.FailureTitle);
    }
}
