using System.IO;
using System.Net.Http;

namespace SpcStudio;

// Puts a release's files in place of the running app's own, keeping the files it replaces aside, and clears those away once the app has started again.
public static class UpdateInstall
{
    private const string StagingFolderName = ".update";
    private const string ReplacedFilesFolderName = ".old-version";
    private const string DownloadFileName = "download";
    private const string UnpackedFolderName = "unpacked";
    private const string AllFiles = "*";
    private const int DownloadBufferBytes = 81920;

    // Whether the files a past update replaced are still there, which they are from the first launch after an update until they are deleted.
    public static bool HasJustUpdated
    {
        get
        {
            return Directory.Exists(RunningAppReplacedFilesFolder);
        }
    }

    // The folder the running app's past update moved the files it replaced into.
    private static string RunningAppReplacedFilesFolder
    {
        get
        {
            return Path.Combine(AppPaths.AppFolder, ReplacedFilesFolderName);
        }
    }

    // Downloads a release into the app's folder, unpacks it and moves its files in place of the app's own, reporting the share of the download done;
    // puts the app's own files back where a step after the download fails, and throws.
    public static void Install(AvailableUpdate update, string appFolder, Action<double> reportDone, CancellationToken cancel)
    {
        string stagingFolder = Path.Combine(appFolder, StagingFolderName);
        string replacedFilesFolder = Path.Combine(appFolder, ReplacedFilesFolderName);
        DeleteFolderWhereThere(stagingFolder);
        DeleteFolderWhereThere(replacedFilesFolder);
        CreateStagingFolder(stagingFolder, appFolder);

        try
        {
            string archivePath = Path.Combine(stagingFolder, DownloadFileName);
            string unpackedFolder = Path.Combine(stagingFolder, UnpackedFolderName);
            Download(update.DownloadUri, archivePath, reportDone, cancel);
            ReleaseArchive.Extract(archivePath, unpackedFolder);
            cancel.ThrowIfCancellationRequested();
            ReplaceFiles(Directory.GetDirectories(unpackedFolder).Single(), appFolder, replacedFilesFolder);
        }
        finally
        {
            DeleteFolderWhereThere(stagingFolder);
        }
    }

    // Deletes the files a past update replaced, the app running from the new ones, letting a failure pass.
    public static void DeleteReplacedFiles()
    {
        try
        {
            DeleteFolderWhereThere(RunningAppReplacedFilesFolder);
        }
        catch (Exception)
        {
        }
    }

    // Makes the folder a release is downloaded into, throwing where the app's folder cannot be written into.
    private static void CreateStagingFolder(string stagingFolder, string appFolder)
    {
        try
        {
            Directory.CreateDirectory(stagingFolder);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw UpdateFailedException.FolderNotWritable(appFolder);
        }
    }

    // Writes a download to a file, reporting the share of it done where its size is known, and throws a failed download as one.
    private static void Download(Uri address, string path, Action<double> reportDone, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            using var response = UpdateCheck.Http.Send(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            long totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var source = response.Content.ReadAsStream(cancel);
            using var target = File.Create(path);
            var buffer = new byte[DownloadBufferBytes];
            long doneBytes = 0;
            int readBytes;
            while ((readBytes = source.Read(buffer)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                target.Write(buffer, 0, readBytes);
                doneBytes += readBytes;
                reportDone(ProgressDialog.DoneFraction(doneBytes, totalBytes));
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            throw UpdateFailedException.DownloadFailed(ex);
        }
    }

    // Moves every file of a release into the app's folder, each file it replaces into the replaced files' folder, and moves every one back where a move fails, then throws.
    private static void ReplaceFiles(string releaseFolder, string appFolder, string replacedFilesFolder)
    {
        var replacedPaths = new List<string>();
        var addedPaths = new List<string>();
        try
        {
            foreach (string releaseFile in Directory.GetFiles(releaseFolder, AllFiles, SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(releaseFolder, releaseFile);
                string appFile = Path.Combine(appFolder, relativePath);
                if (File.Exists(appFile))
                {
                    MoveIntoFolder(appFile, Path.Combine(replacedFilesFolder, relativePath));
                    replacedPaths.Add(relativePath);
                }

                MoveIntoFolder(releaseFile, appFile);
                addedPaths.Add(relativePath);
            }
        }
        catch (Exception)
        {
            PutBack(appFolder, replacedFilesFolder, addedPaths, replacedPaths);
            throw;
        }
    }

    // Deletes the files an install moved in and moves the ones it replaced back where they were.
    private static void PutBack(string appFolder, string replacedFilesFolder, List<string> addedPaths, List<string> replacedPaths)
    {
        foreach (string relativePath in addedPaths)
        {
            File.Delete(Path.Combine(appFolder, relativePath));
        }

        foreach (string relativePath in replacedPaths)
        {
            File.Move(Path.Combine(replacedFilesFolder, relativePath), Path.Combine(appFolder, relativePath));
        }
    }

    // Moves a file to a path, making the folder it goes into where that is not there yet.
    private static void MoveIntoFolder(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Move(sourcePath, targetPath);
    }

    // Deletes a folder and everything in it where it is there.
    private static void DeleteFolderWhereThere(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        Directory.Delete(folder, recursive: true);
    }
}
