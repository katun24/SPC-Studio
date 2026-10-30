using System.IO;
using System.Runtime.InteropServices;

namespace SpcStudio.Model;

// Deletes a file or a folder the user asked to delete: into the Recycle Bin on Windows, and for good elsewhere.
public static class DiskDelete
{
    private const uint DeleteOperation = 3;
    private const ushort SilentFlag = 0x0004;
    private const ushort NoConfirmationFlag = 0x0010;
    private const ushort AllowUndoFlag = 0x0040;
    private const ushort NoErrorUiFlag = 0x0400;
    private const char PathListEnd = '\0';

    // Whether a delete goes into the Recycle Bin rather than deleting for good.
    public static bool IsRecycled
    {
        get
        {
            return OperatingSystem.IsWindows();
        }
    }

    // Deletes a file, or a folder with everything in it, refusing where the system cannot.
    public static void Delete(string path)
    {
        if (IsRecycled)
        {
            Recycle(path);
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        File.Delete(path);
    }

    // Puts a file or a folder into the Recycle Bin without the shell asking or reporting anything itself.
    private static void Recycle(string path)
    {
        var shellDeleteOperation = new ShellFileOperation
        {
            Function = DeleteOperation,
            FromPaths = Path.GetFullPath(path) + PathListEnd,
            Flags = (ushort)(SilentFlag | NoConfirmationFlag | AllowUndoFlag | NoErrorUiFlag),
        };

        if (SHFileOperationW(ref shellDeleteOperation) != 0 || shellDeleteOperation.IsAnyOperationAborted)
        {
            throw new IOException($"{Path.GetFileName(path)} could not be deleted.");
        }
    }

    // Runs a copy, move, rename or delete through the Windows shell.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShellFileOperation operation);

    // What the shell's file operation is handed: the operation, the paths it works on, how quietly, and whether it was stopped.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr OwnerWindow;
        public uint Function;
        public string FromPaths;
        public string? ToPaths;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsAnyOperationAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }
}
