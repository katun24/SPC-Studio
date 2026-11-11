using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SpcStudio.Web;

// SMWCentral's public API, asked one request at a time as its rate limit allows: the music section's listing, one file's details and a file's download.
public sealed class SmwcApi
{
    private const string ApiAddress = "https://www.smwcentral.net/ajax.php";
    private const string ListingPageQuery = "?a=getsectionlist&s=" + SmwcFile.MusicSection + "&n=";
    private const string FileDetailsQuery = "?a=getfile&v=2&id=";
    private const string RemainingHeader = "X-RateLimit-Remaining";
    private const string NoneRemaining = "0";
    private const int PauseWhenNoneRemainingMilliseconds = 1000;
    private const int RetryWithoutRetryAfterSeconds = 10;

    private static readonly JsonSerializerOptions ApiJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly HttpClient _client;
    private bool _isNoneRemaining;

    // An API client asking over a connection of the caller's, which names what is asking.
    public SmwcApi(HttpClient client)
    {
        _client = client;
    }

    // One page of the music section's listing of accepted files, the newest first.
    public SmwcListingPage ListingPage(int page)
    {
        using var response = Send(ApiAddress + ListingPageQuery + page, CancellationToken.None);
        return JsonSerializer.Deserialize<SmwcListingPage>(response.Content.ReadAsStream(), ApiJson)
            ?? throw new InvalidDataException($"SMWCentral answered page {page} of the listing with nothing.");
    }

    // One file's details, or null where SMWCentral has no file with that number.
    public SmwcFile? FileDetails(int id, CancellationToken cancel)
    {
        try
        {
            using var response = Send(ApiAddress + FileDetailsQuery + id, cancel);
            return JsonSerializer.Deserialize<SmwcFile>(response.Content.ReadAsStream(cancel), ApiJson);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // Downloads a file to a path, over whatever is there.
    public void Download(string url, string path, CancellationToken cancel)
    {
        using var response = Send(url, cancel);
        HttpDownload.WriteToFile(response, path, null, cancel);
    }

    // Asks for a URL, first pausing where the last answer left no request to spare and asking again as long as one is refused for too many,
    // refusing any other failure.
    private HttpResponseMessage Send(string url, CancellationToken cancel)
    {
        while (true)
        {
            if (_isNoneRemaining)
            {
                Pause(TimeSpan.FromMilliseconds(PauseWhenNoneRemainingMilliseconds), cancel);
            }

            var response = _client.Send(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, cancel);
            _isNoneRemaining = response.Headers.TryGetValues(RemainingHeader, out var remaining) && remaining.FirstOrDefault() == NoneRemaining;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(RetryWithoutRetryAfterSeconds);
                response.Dispose();
                Pause(wait, cancel);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"SMWCentral answered {(int)status} {status} to {url}.", null, status);
            }

            return response;
        }
    }

    // Waits for a time, or until the wait is cancelled, which it throws.
    private static void Pause(TimeSpan wait, CancellationToken cancel)
    {
        cancel.WaitHandle.WaitOne(wait);
        cancel.ThrowIfCancellationRequested();
    }
}
