using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SpcStudio;

// Asks GitHub for the public repo's latest release, and answers it where it is newer than the running app and carries a download for this system.
public static class UpdateCheck
{
    private const string RepoName = "SPC-Studio";
    private const string LatestReleaseAddress = "https://api.github.com/repos/katun24/" + RepoName + "/releases/latest";
    private const string GitHubJsonType = "application/vnd.github+json";
    private const char VersionTagPrefix = 'v';
    private const string TagProperty = "tag_name";
    private const string AssetsProperty = "assets";
    private const string AssetNameProperty = "name";
    private const string AssetAddressProperty = "browser_download_url";

    // The one connection the check and the download share, naming the app and its version on every request.
    public static readonly HttpClient Http = NewHttpClient();

    // Asks GitHub for a newer release, answering it, no release where there is none, or a failed check where GitHub cannot be asked or answers nothing readable.
    public static async Task<Answer> AskForNewerRelease()
    {
        try
        {
            return new Answer(await NewerRelease(), HasFailed: false);
        }
        catch (Exception)
        {
            return new Answer(null, HasFailed: true);
        }
    }

    // The latest release where it is newer than the running app and has a download for this system, or null where it does not; throws where GitHub cannot be asked.
    private static async Task<AvailableUpdate?> NewerRelease()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseAddress);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubJsonType));
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var latestVersion = Version.Parse(release.RootElement.GetProperty(TagProperty).GetString()!.TrimStart(VersionTagPrefix));
        if (latestVersion <= Version.Parse(AppVersion.Text))
        {
            return null;
        }

        return DownloadForThisSystem(release.RootElement) is { } downloadUri ? new AvailableUpdate(latestVersion, downloadUri) : null;
    }

    // Where the release's download for this system is, or null where it carries none.
    private static Uri? DownloadForThisSystem(JsonElement release)
    {
        foreach (var asset in release.GetProperty(AssetsProperty).EnumerateArray())
        {
            if (asset.GetProperty(AssetNameProperty).GetString() is { } name && name.EndsWith(ReleaseArchive.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return new Uri(asset.GetProperty(AssetAddressProperty).GetString()!);
            }
        }

        return null;
    }

    // A connection that sends the app's name and version with every request.
    private static HttpClient NewHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(RepoName, AppVersion.Text));
        return client;
    }

    // What asking GitHub came to: the newer release where there is one, and whether the asking failed.
    public readonly record struct Answer(AvailableUpdate? Update, bool HasFailed);
}
