namespace SpcStudio.Model;

// What one file or folder name may hold: a song's sample path, which the export writes as #path and puts its .brr files in, and an instrument's name, which is its file's name.
public static class DiskNameRules
{
    private const string ForbiddenCharacters = "<>:\"/\\|?*";

    // Whether a character may appear in a file or folder name at all.
    public static bool IsAllowed(char character)
    {
        return character >= ' ' && !ForbiddenCharacters.Contains(character);
    }

    // The name as it is stored and written: the text with its outer spaces taken off.
    public static string Normalize(string? name)
    {
        return name?.Trim() ?? "";
    }

    // The text with every character a file or folder name cannot carry taken out, then its outer spaces.
    public static string WithoutForbiddenCharacters(string? text)
    {
        return Normalize(new string((text ?? "").Where(IsAllowed).ToArray()));
    }

    // Whether a name can stand on disk as it is: one name, made only of characters a file or folder can carry, and not ending in the dot Windows would throw away.
    public static bool IsValid(string? name)
    {
        string normalizedName = Normalize(name);
        return normalizedName.Length > 0 && !EndsInDroppedDot(normalizedName) && normalizedName.All(IsAllowed);
    }

    // Whether a name ends in the dot Windows throws away.
    public static bool EndsInDroppedDot(string name)
    {
        return name.EndsWith('.');
    }
}
