namespace SpcStudio.Import;

// The refusals of placing remote code and arpeggios, whether from a text file or from a ROM: what their expansions cannot lay out as notes.
public sealed partial class MmlImportException
{
    // A remote code event type the driver has no handler for.
    public static MmlImportException RemoteEventTypeUnread(int triggerType, int line)
    {
        return new($"A remote code event type {triggerType}, which the driver has no handler for.", line);
    }

    // A remote code block called that is not defined.
    public static MmlImportException RemoteBlockUndefined(int blockLabel, int line)
    {
        return new($"Remote code block (!{blockLabel}) is not defined.", line);
    }

    // A tie at a loop's edge whose reading remote code or an arpeggio decides.
    public static MmlImportException TieAtLoopEdgeUnderRemoteCode(int line)
    {
        return new("A '^' tie at the start or the end of a loop cannot be read under remote code or an arpeggio.", line);
    }

    // A type 1 timer counted after the loop point from a value the second pass does not start it at.
    public static MmlImportException TimerDiffersAfterLoop(int channel, int line)
    {
        return new($"Channel #{channel} counts a remote code type 1 timer after the '/' loop point from a value the second pass does not start it at.", line);
    }

    // A note under both a type 3 arm and legato, still sounding where its channel ends.
    public static MmlImportException KeyOffArmAndLegatoAtChannelEnd(int channel, int tick, int line)
    {
        return new($"Channel #{channel} has a note at tick {tick} under both a remote code type 3 and legato that is still sounding where the channel ends.", line);
    }

    // A note under a type 1 arm whose block runs past the end of the note.
    public static MmlImportException TimerBlockPastNote(int channel, int tick, int line)
    {
        return new($"Channel #{channel} has a note at tick {tick} under a remote code type 1 whose block runs past the end of the note, inside the rest that follows it.", line);
    }

    // A type 1 arm whose block runs before the channel's next note.
    public static MmlImportException TimerBlockBeforeNextNote(int channel, int tick, int line)
    {
        return new($"Channel #{channel} arms a remote code type 1 at tick {tick}, where the block runs before the channel's next note.", line);
    }

    // A note under both a remote code arm and legato.
    public static MmlImportException RemoteArmAndLegato(int channel, int tick, int triggerType, int line)
    {
        return new($"Channel #{channel} has a note at tick {tick} under both a remote code type {triggerType} and legato.", line);
    }

    // An $FB inside a remote code block.
    public static MmlImportException ArpeggioInsideRemoteBlock(int line)
    {
        return new("A '$FB' arpeggio command cannot be inside a remote code block.", line);
    }

    // A song using both an arpeggio and a remote code type the expansions cannot combine.
    public static MmlImportException ArpeggioWithRemoteType(int triggerType, int line)
    {
        return new($"A song cannot use both a '$FB' arpeggio and remote code type {triggerType}.", line);
    }

    // A song using both an arpeggio and a hot patch.
    public static MmlImportException ArpeggioWithHotPatch(int line)
    {
        return new("A song cannot use both a '$FB' arpeggio and a '$FA $7F' or '$FA $FE' hot patch, the patch being able to change what the arpeggio does.", line);
    }

    // An arpeggio running while a type 3 or 6 arm is on its channel.
    public static MmlImportException ArpeggioUnderKeyOffArm(int channel, int line)
    {
        return new(InChannel(channel, "A '$FB' arpeggio cannot run while remote code type 3 or 6 is armed on its channel."), line);
    }

    // A note under an arpeggio that carries a pitch bend.
    public static MmlImportException ArpeggioNoteWithBend(int channel, int tick, int line)
    {
        return new(InChannel(channel, $"The note at tick {tick} is under an arpeggio and carries a '$DD' pitch bend."), line);
    }

    // An arpeggio whose note list is shorter than its count.
    public static MmlImportException ArpeggioListShort(int channel, int line)
    {
        return new(InChannel(channel, "An arpeggio's note list is shorter than its own count says."), line);
    }

    // An arpeggio whose note list holds nothing but loop points.
    public static MmlImportException ArpeggioListOnlyLoopPoints(int channel, int line)
    {
        return new(InChannel(channel, "An arpeggio's note list is nothing but loop points."), line);
    }

    // An arpeggio stepping before its channel's first note.
    public static MmlImportException ArpeggioBeforeFirstNote(int channel, int line)
    {
        return new(InChannel(channel, "An arpeggio steps before the channel's first note."), line);
    }

    // A reason with the channel it was found in named in front of it.
    private static string InChannel(int channel, string reason)
    {
        return $"Channel #{channel}: {reason}";
    }
}
