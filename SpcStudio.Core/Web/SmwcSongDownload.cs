namespace SpcStudio.Web;

// Downloads one song from SMWCentral by its number.
public static class SmwcSongDownload
{
    // Downloads the song under a number into a zip, refusing a number that is no song's and throwing a failed download as one.
    public static void ToZip(SmwcApi api, int songNumber, string zipPath, CancellationToken cancel)
    {
        try
        {
            if (api.FileDetails(songNumber, cancel) is not { IsSong: true } song)
            {
                throw SmwcException.NoSong(songNumber);
            }

            api.Download(song.DownloadUrl, zipPath, cancel);
        }
        catch (Exception ex) when (HttpDownload.IsFailedDownload(ex, cancel))
        {
            throw SmwcException.DownloadFailed(ex);
        }
    }
}
