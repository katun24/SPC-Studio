using System.IO;
using System.Net.Http;

namespace SpcStudio.Web;

// Writes what a web request answered to a file, and tells a download that failed from one the user stopped.
public static class HttpDownload
{
    private const int BufferBytes = 81920;

    // Writes an answer's body to a file, over whatever is there, reporting the bytes written so far and the whole size, which is zero where the answer does not give it.
    public static void WriteToFile(HttpResponseMessage response, string path, Action<long, long>? reportBytes, CancellationToken cancel)
    {
        long totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var source = response.Content.ReadAsStream(cancel);
        using var target = File.Create(path);
        var buffer = new byte[BufferBytes];
        long doneBytes = 0;
        int readBytes;
        while ((readBytes = source.Read(buffer)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            target.Write(buffer, 0, readBytes);
            doneBytes += readBytes;
            reportBytes?.Invoke(doneBytes, totalBytes);
        }
    }

    // Whether an exception is a download that failed rather than one the user stopped, a timeout being a cancel nobody asked for.
    public static bool IsFailedDownload(Exception exception, CancellationToken cancel)
    {
        return exception is HttpRequestException or IOException || exception is OperationCanceledException && !cancel.IsCancellationRequested;
    }
}
