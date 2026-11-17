using Avalonia.Controls;

namespace SpcStudio;

// The File menu's Recent Files: the list it shows and the files reopened from it.
public partial class MainWindow
{
    private readonly RecentFiles _recentFiles = new();

    // Shows the recent files under Recent Files now and whenever the list changes.
    private void WireRecentFiles()
    {
        _recentFiles.ListChanged += ShowRecentFiles;
        ShowRecentFiles();
    }

    // Lists the recent files under Recent Files, each by its full path, the item greyed out where there are none.
    private void ShowRecentFiles()
    {
        RecentFilesItem.Items.Clear();
        foreach (var recentFile in _recentFiles.FilesNewestFirst)
        {
            var fileItem = new MenuItem { Header = recentFile.Path };
            fileItem.Click += async (_, _) => await ReopenRecentFile(recentFile);
            RecentFilesItem.Items.Add(fileItem);
        }

        RecentFilesItem.IsEnabled = RecentFilesItem.Items.Count > 0;
    }

    // Opens, imports or transcribes a listed file as its own File menu item does, and takes it off the list where it turns out to be gone.
    private async Task ReopenRecentFile(RecentFile recentFile)
    {
        if (await ReplaceSongWithKnownFile(recentFile.Kind, recentFile.Path) && !File.Exists(recentFile.Path))
        {
            _recentFiles.Forget(recentFile.Path);
        }
    }
}
