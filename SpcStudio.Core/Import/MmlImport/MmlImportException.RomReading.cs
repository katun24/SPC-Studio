namespace SpcStudio.Import;

// The refusals of reading one song out of a ROM's bytes, which have no line to name.
public sealed partial class MmlImportException
{
    // A pointer leading out of the song's own data.
    public static MmlImportException PointerLeavesSong(int address)
    {
        return new($"A pointer to ${address:X4} leaves the song's own data.", 0);
    }

    // A phrase with no channels in it.
    public static MmlImportException PhraseWithoutChannels(int phrase)
    {
        return new($"Phrase {phrase} has no channels in it.", 0);
    }

    // A song with no phrases in it.
    public static MmlImportException SongWithoutPhrases()
    {
        return new("The song has no phrases in it.", 0);
    }

    // A song looping back to an address that is not one of its phrase pointers.
    public static MmlImportException LoopTargetNotPhrase(int targetAddress)
    {
        return new($"The song loops back to ${targetAddress:X4}, which is not one of its own phrase pointers.", 0);
    }

    // A song header with no end.
    public static MmlImportException SongHeaderUnterminated()
    {
        return new("The song header has no end in it.", 0);
    }

    // An instrument table that is not a whole number of entries.
    public static MmlImportException InstrumentTableUneven(int tableBytes)
    {
        return new($"The {tableBytes} bytes between the song header and its first phrase are not a whole number of instrument entries.", 0);
    }

    // An instrument playing a sample past the ones the song loads.
    public static MmlImportException InstrumentSamplePastSongSamples(int instrumentNumber, int sample, int songSampleCount)
    {
        return new($"Instrument @{instrumentNumber} plays sample {sample}, which is past the {songSampleCount} this song loads.", 0);
    }

    // A command this does not model, with the channel, counted from one, that uses it.
    public static MmlImportException CommandNotModeled(int channelNumber, int command, int address)
    {
        return new($"Channel {channelNumber} uses the command ${command:X2} at ${address:X4}, which is not modeled.", 0);
    }

    // A subcommand of a command that this does not model.
    public static MmlImportException SubcommandNotModeled(int command, int subcommand)
    {
        return new($"The '${command:X2} ${subcommand:X2}' subcommand is not modeled.", 0);
    }

    // An $F6 write to a DSP register this does not model.
    public static MmlImportException DspWriteNotModeled(int register)
    {
        return new($"A '$F6' write to DSP register ${register:X2} is not modeled.", 0);
    }

    // An $F1 filter index past the driver's FIR tables, in a ROM whose driver could not be read.
    public static MmlImportException EchoFilterPastPresetsUnreadDriver(int filterIndex)
    {
        return new($"'$F1' with echo filter ${filterIndex:X2} reads past the driver's two FIR tables, and this ROM's own driver could not be read to say what it finds there.", 0);
    }

    // A remote code call inside a remote code block.
    public static MmlImportException RemoteCallInsideRemoteBlock()
    {
        return new("A remote code call inside a remote code block is not read.", 0);
    }

    // A track that never reaches its end, with the channel counted from one.
    public static MmlImportException TrackNeverEnds(int channelNumber)
    {
        return new($"Channel {channelNumber} never reaches the end of its track, so the song loops inside itself forever.", 0);
    }

    // A track running outside the song, with the channel counted from one.
    public static MmlImportException TrackLeavesSong(int channelNumber, int address)
    {
        return new($"Channel {channelNumber} runs to ${address:X4}, which is outside the song.", 0);
    }

    // A byte the driver's dispatch table has no entry for, with the channel counted from one.
    public static MmlImportException ByteWithoutDispatch(int channelNumber, int trackByte, int address)
    {
        return new($"Channel {channelNumber} reaches ${trackByte:X2} at ${address:X4}, which the driver's dispatch table has no entry for.", 0);
    }

    // A subroutine call inside a subroutine.
    public static MmlImportException CallInsideCall()
    {
        return new("A subroutine call inside a subroutine, which the driver's one return slot per channel cannot hold.", 0);
    }

    // A percussion note past the driver's drums, with the channel counted from one.
    public static MmlImportException PercussionPastDrums(int channelNumber, int noteByte)
    {
        return new($"Channel {channelNumber} plays percussion note ${noteByte:X2}, which is past the driver's nine drums.", 0);
    }

    // A read-ahead reaching the end of a subloop that is not being played, with the channel counted from one.
    public static MmlImportException ReadAheadIntoUnplayedSubloop(int channelNumber, int address)
    {
        return new($"Channel {channelNumber} reads ahead into the end of a subloop at ${address:X4} that is not being played.", 0);
    }

    // A track ending on a loop with nothing in it, which hangs the driver under the read-ahead hot patch, with the channel counted from one.
    public static MmlImportException TrackEndsOnEmptyLoop(int channelNumber)
    {
        return new(
            $"Channel {channelNumber} ends on a loop with no note, rest or tie in it, which hangs the driver under a '$FA $7F' or '$FA $FE' hot patch that makes it look inside loops.", 0);
    }
}
