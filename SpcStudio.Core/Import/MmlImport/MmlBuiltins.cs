using SpcStudio.Model;

namespace SpcStudio.Import;

// AddmusicK's built-in instruments, @0 to @18 and the percussion rows @21 to @29, which every song can play without loading a sample of its own.
public static class MmlBuiltins
{
    public const int MaxPitchedNumber = 18;
    public const int FirstPercussionNumber = 21;
    public const int LastPercussionNumber = 29;
    public const int MaxCopiedNumber = LastPercussionNumber;
    public const int DefaultNumber = 0;
    public const int NoForcedNoteValue = -1;
    private const int UnlistedSampleNumber = 0;
    private const int NoTranspose = 0;

    // @0 to @18: each row's sample, register bytes, tuning and transpose.
    private static readonly Entry[] PitchedTable =
    [
        new(0x00, 0xFE, 0x6A, 0xB8, 0x06, 0x00, 0),
        new(0x01, 0xFA, 0x6A, 0xB8, 0x03, 0x00, 0),
        new(0x02, 0xAE, 0x2F, 0xB8, 0x04, 0x00, 5),
        new(0x03, 0xFE, 0x6A, 0xB8, 0x03, 0x00, 0),
        new(0x04, 0xA9, 0x6A, 0xB8, 0x03, 0x00, 0),
        new(0x07, 0xAE, 0x26, 0xB8, 0x07, 0x00, 0),
        new(0x08, 0xFA, 0x6A, 0xB8, 0x03, 0x00, 0),
        new(0x09, 0x9E, 0x1F, 0xB8, 0x03, 0x00, 0),
        new(0x05, 0xAE, 0x26, 0xB8, 0x1E, 0x00, 0),
        new(0x0A, 0xEE, 0x6A, 0xB8, 0x02, 0x00, -5),
        new(0x0B, 0xFE, 0x6A, 0xB8, 0x08, 0x00, 6),
        new(0x01, 0xF7, 0x6A, 0xB8, 0x03, 0x00, 0),
        new(0x10, 0x0E, 0x6A, 0x7F, 0x04, 0x00, -5),
        new(0x0C, 0xFE, 0x6A, 0xB8, 0x03, 0x00, 0),
        new(0x0D, 0xAE, 0x26, 0xB8, 0x07, 0x00, 0),
        new(0x12, 0x8E, 0xE0, 0xB8, 0x03, 0x00, 8),
        new(0x0C, 0xFE, 0x70, 0xB8, 0x03, 0x00, 0),
        new(0x11, 0xFE, 0x6A, 0xB8, 0x05, 0x00, 0),
        new(0x01, 0xE9, 0x6A, 0xB8, 0x03, 0x00, 0),
    ];

    // @21 to @29: the same six bytes, no transpose, and the note value every note under the row plays.
    private static readonly Entry[] PercussionTable =
    [
        new(0x0F, 0x0F, 0x6A, 0x7F, 0x03, 0x00, NoTranspose, 0xA8 & SnesPitch.NoteValueMask),
        new(0x06, 0x0E, 0x6A, 0x40, 0x07, 0x00, NoTranspose, 0xA4 & SnesPitch.NoteValueMask),
        new(0x06, 0x8C, 0xE0, 0x70, 0x07, 0x00, NoTranspose, 0xA1 & SnesPitch.NoteValueMask),
        new(0x0E, 0xFE, 0x6A, 0xB8, 0x07, 0x00, NoTranspose, 0xA4 & SnesPitch.NoteValueMask),
        new(0x0E, 0xFE, 0x6A, 0xB8, 0x08, 0x00, NoTranspose, 0xA4 & SnesPitch.NoteValueMask),
        new(0x0B, 0xFE, 0x6A, 0xB8, 0x02, 0x00, NoTranspose, 0x9C & SnesPitch.NoteValueMask),
        new(0x0B, 0x7E, 0x6A, 0x7F, 0x08, 0x00, NoTranspose, 0xA6 & SnesPitch.NoteValueMask),
        new(0x0B, 0x7E, 0x6A, 0x30, 0x08, 0x00, NoTranspose, 0xA6 & SnesPitch.NoteValueMask),
        new(0x0E, 0x0E, 0x6A, 0x7F, 0x03, 0x00, NoTranspose, 0xA1 & SnesPitch.NoteValueMask),
    ];

    // What the instrument a channel starts under leaves in its ADSR1, ADSR2 and GAIN at song load.
    public static EnvelopeState DefaultEnvelope
    {
        get
        {
            return EnvelopeOf(DefaultNumber);
        }
    }

    // Whether an @ selects a built-in instrument, pitched or percussion.
    public static bool IsBuiltin(int number)
    {
        return (number >= 0 && number <= MaxPitchedNumber) || IsPercussion(number);
    }

    // Whether an @ selects a percussion row rather than a pitched instrument.
    public static bool IsPercussion(int number)
    {
        return number >= FirstPercussionNumber && number <= LastPercussionNumber;
    }

    // The row a built-in @ selects.
    public static Entry EntryFor(int number)
    {
        if (IsPercussion(number))
        {
            return PercussionTable[number - FirstPercussionNumber];
        }

        return PitchedTable[number];
    }

    // The ADSR1, ADSR2 and GAIN a built-in @'s row holds.
    public static EnvelopeState EnvelopeOf(int number)
    {
        var entry = EntryFor(number);
        return new(entry.Bytes.Adsr1, entry.Bytes.Adsr2, entry.Bytes.Gain);
    }

    // The sample an @ plays, as a position in the song's own #samples block, for the whole range an #instruments entry may copy from.
    public static int SampleNumberOf(int number)
    {
        if (!IsBuiltin(number))
        {
            return UnlistedSampleNumber;
        }

        return EntryFor(number).SampleNumber;
    }

    // The semitones the compiler takes off every note played on an @, which is nothing for anything that is not a built-in.
    public static int TransposeOf(int number)
    {
        if (!IsBuiltin(number))
        {
            return NoTranspose;
        }

        return EntryFor(number).Transpose;
    }

    // One row: the sample it plays, its three envelope bytes, its tuning as dd.ee, the semitones taken off its notes, and for a percussion row the note value it forces.
    public readonly record struct Entry(int SampleNumber, InstrumentBytes Bytes, int Transpose, int ForcedNoteValue)
    {
        // A row as the table writes it, its five bytes one after another.
        public Entry(int sampleNumber, byte adsr1, byte adsr2, byte gain, byte tuningHigh, byte tuningLow, int transpose, int forcedNoteValue = NoForcedNoteValue)
            : this(sampleNumber, new InstrumentBytes(adsr1, adsr2, gain, tuningHigh, tuningLow), transpose, forcedNoteValue)
        {
        }
    }
}
