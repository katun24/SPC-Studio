using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SpcStudio.Model;

// The JSON both of the app's own files are written in, a song and a single instrument, and the three refusals they share on the way back in.
internal static class SavedFileJson
{
    // Names in camel case, indented, a null field left out and an enum written by its name.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // The same, with every field left out as well where it holds what reading the file back fills a missing field in with.
    private static readonly JsonSerializerOptions DefaultsLeftOutOptions = new(SerializerOptions)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { LeaveOutValuesReadBackAnyway } },
    };

    // Writes a file's contents.
    public static void Write<TContents>(string path, TContents contents)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(contents, SerializerOptions));
    }

    // Writes a file's contents, leaving out every field that holds what reading it back would give anyway.
    public static void WriteLeavingOutDefaults<TContents>(string path, TContents contents)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(contents, DefaultsLeftOutOptions));
    }

    // A chain's entries as a file writes them, or null where the chain is empty, which leaves it out of the file.
    public static List<TEntry>? EntriesOrNull<TRow, TEntry>(IReadOnlyCollection<TRow> rows, Func<TRow, TEntry> entryOf)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        return [.. rows.Select(entryOf)];
    }

    // Reads a file's contents back, refusing a file that is not of this kind, is empty or was written in another version - but for the version before
    // the current one where an upgrade is given, which brings such a file up to the current one before it is read.
    public static TContents Read<TContents>(string path, string fileKind, int currentVersion, Action<JsonObject>? upgradePreviousVersion = null)
        where TContents : class, IVersionedContents
    {
        string quotedName = QuotedFileName(path);
        TContents? contents;
        try
        {
            var fileNode = JsonNode.Parse(File.ReadAllText(path));
            if (upgradePreviousVersion is not null && fileNode is JsonObject fileObject && VersionOf(fileObject) == currentVersion - 1)
            {
                upgradePreviousVersion(fileObject);
                fileObject[PropertyName(nameof(IVersionedContents.Version))] = currentVersion;
            }

            contents = fileNode.Deserialize<TContents>(SerializerOptions);
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"{quotedName} is not an SPC Studio {fileKind} file.");
        }

        if (contents is null)
        {
            throw new InvalidDataException($"{quotedName} is empty.");
        }

        if (contents.Version != currentVersion)
        {
            throw new InvalidDataException($"{quotedName} is version {contents.Version}; this build reads version {currentVersion}.");
        }

        return contents;
    }

    // A part of a file read as the type it is written as, or null where there is none.
    public static T? FromNode<T>(JsonNode? node)
    {
        return node.Deserialize<T>(SerializerOptions);
    }

    // A value as a part of a file would hold it.
    public static JsonNode? ToNode<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, SerializerOptions);
    }

    // The name a property is written under.
    public static string PropertyName(string propertyName)
    {
        return SerializerOptions.PropertyNamingPolicy!.ConvertName(propertyName);
    }

    // The version a file's contents say they were written in, or null where they say none.
    private static int? VersionOf(JsonObject fileObject)
    {
        return fileObject[PropertyName(nameof(IVersionedContents.Version))] is JsonValue version && version.TryGetValue(out int versionNumber) ? versionNumber : null;
    }

    // Reads a file back, any refusal of its contents becoming the one saying it is not a valid file of its kind, whatever is wrong with it.
    public static TRead ReadRefusingAsNotValid<TRead>(string path, string fileKind, Func<string, TRead> read)
    {
        try
        {
            return read(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException($"{QuotedFileName(path)} is not a valid {fileKind} file.");
        }
    }

    // A saved file's name as a refusal names it: the name alone, in quotes.
    public static string QuotedFileName(string path)
    {
        return $"'{Path.GetFileName(path)}'";
    }

    // Has each property of an object written only where it holds something other than null and other than what a newly made object of its type holds, which is
    // what reading the file back gives a property the file leaves out.
    private static void LeaveOutValuesReadBackAnyway(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.CreateObject is null)
        {
            return;
        }

        object newObject = typeInfo.CreateObject();
        foreach (var property in typeInfo.Properties)
        {
            if (property.Get is not { } readValue)
            {
                continue;
            }

            object? newValue = readValue(newObject);
            property.ShouldSerialize = (_, value) => value is not null && !Equals(value, newValue);
        }
    }
}
