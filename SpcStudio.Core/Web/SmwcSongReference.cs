using System.Globalization;
using System.Web;

namespace SpcStudio.Web;

// A song named as SMWCentral numbers it, typed as the number itself or as the address of its page there.
public static class SmwcSongReference
{
    private const string SiteHost = "smwcentral.net";
    private const string SiteSubdomainHost = "www." + SiteHost;
    private const string IdParameter = "id";
    private const string WebAddressSymbols = "-._~:/?#[]@!$&'()*+,;=%";

    // Whether a character can stand in a song number or in a web address.
    public static bool IsAllowedCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || WebAddressSymbols.Contains(character);
    }

    // Reads the song number out of a song number or a song page's address, answering whether the text was either.
    public static bool TryReadNumber(string? text, out int number)
    {
        string trimmed = text?.Trim() ?? "";
        if (TryReadPositive(trimmed, out number))
        {
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var address) || !IsSiteHost(address.Host))
        {
            return false;
        }

        return TryReadPositive(HttpUtility.ParseQueryString(address.Query)[IdParameter], out number);
    }

    // Whether a host is SMWCentral's, with or without its www.
    private static bool IsSiteHost(string host)
    {
        return host.Equals(SiteHost, StringComparison.OrdinalIgnoreCase) || host.Equals(SiteSubdomainHost, StringComparison.OrdinalIgnoreCase);
    }

    // Reads a whole number above zero written in digits alone.
    private static bool TryReadPositive(string? text, out int number)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }
}
