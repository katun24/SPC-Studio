using System.IO;
using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Import;

// AddmusicK's sample groups file: the named groups a #samples block can pull in, each a list of .brr paths under the install's samples folder.
public static class SampleGroups
{
    public const string FileName = "Addmusic_sample groups.txt";
    public const string DefaultGroup = "default";
    private const char GroupNameMark = '#';
    private const string WindowsNewline = "\r\n";
    private const string UnixNewline = "\n";

    // The path of the groups file in an install.
    public static string FilePath(string root)
    {
        return Path.Combine(root, FileName);
    }

    // The text of the groups file beside the samples, or null where there is no such file.
    public static string? ReadFileText(string root)
    {
        string path = FilePath(root);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    // A group's name as a #samples block and the groups file write it.
    public static string DirectiveOf(string groupName)
    {
        return $"{GroupNameMark}{groupName}";
    }

    // The sentence saying a group is not one the install's groups file defines.
    public static string NotInInstallSentence(string groupName)
    {
        return $"The sample group {DirectiveOf(groupName)} is not in AddmusicK's {FileName}.";
    }

    // The paths of each group the file beside the samples defines, or null where there is no such file.
    public static IReadOnlyDictionary<string, IReadOnlyList<string>>? ReadPathsByGroup(string root)
    {
        return ReadFileText(root) is { } text ? ParsePathsByGroup(text) : null;
    }

    // The groups the file beside the samples defines, in its own order, or null where there is no such file or it cannot be read.
    public static IReadOnlyList<SampleGroup>? ReadGroups(string root)
    {
        return ReadableFiles.OrNull(() => ReadFileText(root) is { } text ? ParseGroups(text) : null);
    }

    // The paths of each group a groups file's text defines.
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParsePathsByGroup(string text)
    {
        var pathsByGroup = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var group in ParseGroups(text))
        {
            pathsByGroup[group.Name] = [.. group.Entries.Select(entry => entry.Path)];
        }

        return pathsByGroup;
    }

    // The groups a groups file's text defines, in its own order, or none where AddmusicK would stop on the text.
    public static IReadOnlyList<SampleGroup> ParseGroups(string text)
    {
        return TryParseGroups(text) ?? [];
    }

    // The groups a shared song's own groups file defines once its comments are taken out, or null where it is not such a file or defines no group.
    public static IReadOnlyList<SampleGroup>? GroupsOfSharedFile(string text)
    {
        return TryParseGroups(MmlPreprocessor.RemoveComments(text)) is { Count: > 0 } groups ? groups : null;
    }

    // The text that adds groups to the end of a groups file holding the given text, in the file's own line endings.
    public static string TextToAppend(string fileText, IEnumerable<SampleGroup> groups)
    {
        string fileNewline = fileText.Contains(WindowsNewline) ? WindowsNewline : UnixNewline;
        var appended = new StringBuilder();
        if (fileText.Length > 0 && !fileText.EndsWith(UnixNewline))
        {
            appended.Append(fileNewline);
        }

        foreach (var group in groups)
        {
            appended.Append(fileNewline).Append(DirectiveOf(group.Name)).Append(fileNewline).Append('{').Append(fileNewline);
            foreach (var entry in group.Entries)
            {
                appended.Append("\t\"").Append(entry.Path).Append('"');
                if (entry.IsImportant)
                {
                    appended.Append('!');
                }

                appended.Append(fileNewline);
            }

            appended.Append('}').Append(fileNewline);
        }

        return appended.ToString();
    }

    // The groups a groups file's text defines, in its own order, each entry marked where a '!' follows it and a name defined twice kept as its first,
    // or null where the text holds anything AddmusicK's own reader of the file stops on.
    private static List<SampleGroup>? TryParseGroups(string text)
    {
        var groups = new List<SampleGroup>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int position = 0;
        while (true)
        {
            if (IsAtEndPastWhitespace(text, ref position))
            {
                return groups;
            }

            if (text[position] != GroupNameMark)
            {
                return null;
            }

            int nameStart = ++position;
            position = AmkWhitespace.NextWhitespace(text, nameStart);
            string name = text[nameStart..position];
            if (IsAtEndPastWhitespace(text, ref position))
            {
                return groups;
            }

            if (text[position] != '{')
            {
                return null;
            }

            position++;
            if (TryParseEntries(text, ref position) is not { } entries)
            {
                return null;
            }

            if (name.Length > 0 && names.Add(name))
            {
                groups.Add(new SampleGroup(name, entries));
            }
        }
    }

    // Moves a position past whitespace and says whether that reaches the text's end.
    private static bool IsAtEndPastWhitespace(string text, ref int position)
    {
        position = AmkWhitespace.NextNonWhitespace(text, position);
        return position >= text.Length;
    }

    // The quoted entries of one group from just inside its opening brace, leaving the position past its closing one, or null where the group holds anything AddmusicK stops on.
    private static List<SampleGroupEntry>? TryParseEntries(string text, ref int position)
    {
        var entries = new List<SampleGroupEntry>();
        while (true)
        {
            if (IsAtEndPastWhitespace(text, ref position))
            {
                return entries;
            }

            char character = text[position++];
            if (character == '}')
            {
                return entries;
            }

            if (character == '!')
            {
                if (entries.Count == 0)
                {
                    return null;
                }

                entries[^1] = entries[^1] with { IsImportant = true };
                continue;
            }

            if (character != '"')
            {
                return null;
            }

            int pathStart = position;
            while (position < text.Length && text[position] != '"')
            {
                position++;
            }

            if (position >= text.Length)
            {
                return entries;
            }

            entries.Add(new SampleGroupEntry(text[pathStart..position++], false));
        }
    }
}
