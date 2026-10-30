using System.IO;

namespace SpcStudio.Model;

// One change to the instruments folder that can be undone: an instrument file or a folder moved or renamed from one path to another; a folder made where the path it comes from is null, or a copy of
// the copied entry made there where the change has one; and an empty folder, or that copy, taken away where the path it goes to is null.
public sealed record InstrumentLibraryChange(string? FromPath, string? ToPath, InstrumentLibraryEntry? CopiedEntry = null)
{
    private const string UndoDuplicationLeadIn = "Failed to undo duplication.";
    private const string DoesNotExistAnymore = "does not exist anymore.";
    private const string CouldNotBeDeleted = "could not be deleted.";

    // The change that puts this one back.
    public InstrumentLibraryChange Reversed
    {
        get
        {
            return new InstrumentLibraryChange(ToPath, FromPath, CopiedEntry);
        }
    }

    // Makes the change on disk, refusing it where something already stands at the path it leads to or nothing stands at the one it comes from.
    public void Apply()
    {
        if (FromPath is not null && ToPath is not null)
        {
            Move(FromPath, ToPath);
            return;
        }

        if (ToPath is not null)
        {
            MakeEntry(ToPath);
            return;
        }

        if (FromPath is not null)
        {
            TakeEntryAway(FromPath);
        }
    }

    // Makes the copy of the copied entry where the change has one, and a new folder otherwise.
    private void MakeEntry(string path)
    {
        if (CopiedEntry is { } copiedEntry)
        {
            Copy(copiedEntry, path);
            return;
        }

        CreateFolder(path);
    }

    // Takes the copy of the copied entry away where the change has one, and an empty folder otherwise.
    private void TakeEntryAway(string path)
    {
        if (CopiedEntry is not null)
        {
            DeleteCopy(path);
            return;
        }

        RemoveEmptyFolder(path);
    }

    // Makes a new folder, refusing a path something already stands at.
    private static void CreateFolder(string path)
    {
        if (Path.Exists(path))
        {
            throw new IOException(NameRefusals.TakenInFolder(NamedThing.Folder, InstrumentLibrary.NameOf(path, isFolder: true)));
        }

        Directory.CreateDirectory(path);
    }

    // Copies an instrument file, or a folder with everything in it, refusing an entry that has gone and a path something already stands at.
    private static void Copy(InstrumentLibraryEntry entry, string toPath)
    {
        var thing = InstrumentLibrary.ThingOf(entry.IsFolder);
        if (!Path.Exists(entry.Path))
        {
            throw new IOException(Refusal($"Failed to duplicate {NameRefusals.NounOf(thing)}.", entry.Path, DoesNotExistAnymore));
        }

        if (Path.Exists(toPath))
        {
            throw new IOException(NameRefusals.FolderAlreadyContains(thing, InstrumentLibrary.NameOf(toPath, entry.IsFolder)));
        }

        if (!entry.IsFolder)
        {
            File.Copy(entry.Path, toPath);
            return;
        }

        CopyFolder(entry.Path, toPath);
    }

    // Copies a folder and everything in it to a new path.
    private static void CopyFolder(string fromPath, string toPath)
    {
        Directory.CreateDirectory(toPath);
        foreach (string filePath in Directory.GetFiles(fromPath))
        {
            File.Copy(filePath, InstrumentLibrary.MovedPath(filePath, toPath));
        }

        foreach (string subfolderPath in Directory.GetDirectories(fromPath))
        {
            CopyFolder(subfolderPath, InstrumentLibrary.MovedPath(subfolderPath, toPath));
        }
    }

    // Deletes a copy the way the user's own delete does, refusing one that has gone or that the system will not delete.
    private static void DeleteCopy(string path)
    {
        if (!Path.Exists(path))
        {
            throw new IOException(Refusal(UndoDuplicationLeadIn, path, DoesNotExistAnymore));
        }

        try
        {
            DiskDelete.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(Refusal(UndoDuplicationLeadIn, path, CouldNotBeDeleted));
        }
    }

    // Takes an empty folder away, refusing one that holds anything.
    private static void RemoveEmptyFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new IOException($"The folder {Path.GetFileName(path)} is no longer there.");
        }

        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new IOException($"The folder {Path.GetFileName(path)} is not empty.");
        }

        Directory.Delete(path);
    }

    // Moves a file or a folder, a change of letter case alone being a rename of the same entry.
    private static void Move(string fromPath, string toPath)
    {
        bool isFolder = Directory.Exists(fromPath);
        if (!isFolder && !File.Exists(fromPath))
        {
            throw new IOException($"{Path.GetFileName(fromPath)} is no longer there.");
        }

        bool isOnlyCaseChanged = string.Equals(fromPath, toPath, StringComparison.OrdinalIgnoreCase);
        if (!isOnlyCaseChanged && Path.Exists(toPath))
        {
            throw new IOException(NameRefusals.TakenInFolder(InstrumentLibrary.ThingOf(isFolder), InstrumentLibrary.NameOf(toPath, isFolder)));
        }

        if (!isFolder)
        {
            File.Move(fromPath, toPath);
            return;
        }

        if (!isOnlyCaseChanged)
        {
            Directory.Move(fromPath, toPath);
            return;
        }

        // A free name the folder passes through on its way to the new letter case.
        string passingPath = toPath + Guid.NewGuid().ToString("N");
        Directory.Move(fromPath, passingPath);
        Directory.Move(passingPath, toPath);
    }

    // A refusal of a lead-in sentence, then the entry's file or folder name in quotes and what is wrong with it.
    private static string Refusal(string leadIn, string path, string fault)
    {
        return $"{leadIn} '{Path.GetFileName(path)}' {fault}";
    }
}
