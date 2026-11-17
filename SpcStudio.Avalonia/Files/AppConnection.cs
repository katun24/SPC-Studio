using System.Net.Http;
using System.Net.Http.Headers;

namespace SpcStudio;

// The app's one web connection, which the update check, the update's download and the SMWCentral import share.
public static class AppConnection
{
    // The connection, naming the app and its version on every request.
    public static readonly HttpClient Http = NewHttpClient();

    // A connection that sends the app's name and version with every request.
    private static HttpClient NewHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UpdateCheck.RepoName, AppVersion.Text));
        return client;
    }
}
