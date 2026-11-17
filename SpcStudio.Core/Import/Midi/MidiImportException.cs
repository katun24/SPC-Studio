namespace SpcStudio.Import;

// A MIDI file that cannot be imported, each made with the message that says why.
public sealed class MidiImportException : Exception
{
    private MidiImportException(string message) : base(message)
    {
    }

    // A file that is not a Standard MIDI File the import can read.
    public static MidiImportException NotValidMidi(string fileName)
    {
        return new MidiImportException($"{fileName} is not a valid MIDI file.");
    }

    // No AddmusicK folder set, or no piano sample in its default sample group.
    public static MidiImportException NoDefaultSample()
    {
        return new MidiImportException("No default sample can be found in the AddmusicK folder.");
    }
}
