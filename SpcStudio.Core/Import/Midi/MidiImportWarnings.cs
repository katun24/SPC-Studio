namespace SpcStudio.Import;

// The text of every warning a MIDI import can give, one method each, with a parameter for each number the text mentions.
internal static class MidiImportWarnings
{
    // Notes left out because every channel the song can hold was already sounding.
    public static string NotesDropped(int droppedCount, int maxChannelCount)
    {
        return $"{droppedCount} notes were dropped because more than {maxChannelCount} notes sounded at once.";
    }

    // Notes moved by whole octaves onto the roll's rows.
    public static string NotesMovedByOctaves(int movedCount)
    {
        return $"{movedCount} notes outside C1-B7 were moved by whole octaves.";
    }
}
