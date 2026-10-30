using SpcStudio.Model;

namespace SpcStudio.Import;

// The text of every warning an MML import or a ROM extraction can give, one method each, with a parameter for each name or number the text mentions.
internal static class ImportWarnings
{
    // A song holding more notes than the app works with comfortably.
    public static string ManyNotes(int noteCount)
    {
        return $"The song has {noteCount} notes, so the app may be slow to work with it.";
    }

    // A channel whose `$FA $02` transpose differs between its loop point and the song's end, heard by a note after the loop point.
    public static string TransposeAcrossLoop(int channel)
    {
        return $"Channel #{channel} ends with a different '$FA $02' transpose than at its loop point, which cannot be mapped.";
    }

    // The `$E4` global transpose differing between the loop point and the song's end, heard by a note after the loop point.
    public static string GlobalTransposeAcrossLoop()
    {
        return "The song ends with a different '$E4' global transpose than at its loop point, which cannot be mapped.";
    }

    // Channels whose own loop points stand at different ticks.
    public static string LoopPointsDisagree()
    {
        return "The song has channels that loop from different points, which cannot be mapped.";
    }

    // Only some of the written channels having a loop point, all at one tick past the start.
    public static string LoopPointsMissing()
    {
        return "The song has channels without a loop point beside channels with one, which cannot be mapped.";
    }

    // Notes, or parts of notes, the ROM plays at no pitch, left out.
    public static string NotesOrPartsAtNoPitch()
    {
        return "The song has notes, or parts of notes, that the ROM plays at no pitch, so they are left out.";
    }

    // One note the ROM plays at no pitch, left out.
    public static string NoteAtNoPitch()
    {
        return "The song has a note that the ROM plays at no pitch, so it is left out.";
    }

    // Several notes the ROM plays at no pitch, left out.
    public static string NotesAtNoPitch()
    {
        return "The song has notes that the ROM plays at no pitch, so they are left out.";
    }

    // A kind of dropped command whose dropping is warned about.
    public static string DroppedCommand(IgnoredCommand command)
    {
        return command switch
        {
            IgnoredCommand.HotPatchPreset => "The song uses a '$FA $7F' hot patch preset, which is not supported.",
            IgnoredCommand.ZeroLengthPitchBend => "The song has a '$DD' pitch bend of 0 ticks, which is dropped.",
            IgnoredCommand.PitchModulation => "The song uses pitch modulation, which is not supported.",
            IgnoredCommand.MasterVolume => "The song changes the master volume, which is not supported.",
            IgnoredCommand.HotPatchBits => "The song sets '$FA $FE' hot patch bits, which are not supported.",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    // Vibrato fades dropped for being a note's second.
    public static string SecondVibratoFades()
    {
        return "The song has a note with multiple vibrato fades, which cannot be mapped.";
    }

    // Noise commands dropped for landing inside a note a tremolo runs across.
    public static string NoiseInsideTremoloNotes()
    {
        return "The song has noise commands inside a note with a tremolo, which are dropped.";
    }

    // Ties under a `q` of their own landing inside a note a vibrato, a tremolo or a pitch bend runs across.
    public static string TieGatesInsideNotes()
    {
        return "The song has ties with their own 'q' inside a vibrato or tremolo or pitch bend, which play at the note's own 'q'.";
    }

    // Tuning changes cutting a note a vibrato runs across, the vibrato starting again on the second half.
    public static string VibratosRestarted()
    {
        return "The song has tuning changes inside a vibrato, which make the vibrato start over.";
    }

    // Tuning changes cutting a note a pitch bend runs across, the bend carrying on from the nearest semitone.
    public static string BendsRestarted()
    {
        return "The song has tuning changes inside a pitch bend, which make the bend start over from the nearest semitone.";
    }

    // Fades a later fade armed while they were still running, held as a fade to where they had got to.
    public static string RearmedFades(string laneName)
    {
        return $"The song has {IndefiniteArticle.Before(laneName)} fade that starts before the previous one ends, which is mapped approximately.";
    }

    // Sloping fades a plain set was written inside, ended at the set.
    public static string OverwrittenFades(string laneName)
    {
        return $"The song has {IndefiniteArticle.Before(laneName)} set inside a fade, which ends that fade early.";
    }

    // Sets written inside a sloping fade, held as a fade from the set value at about the fade's own step.
    public static string SetsInsideFades(string laneName)
    {
        return $"The song has {IndefiniteArticle.Before(laneName)} set inside a running fade, which is mapped approximately.";
    }

    // Fades a set inside them would carry past the end of the value's range, stopped there.
    public static string FadesStoppedAtRangeEnd(string laneName)
    {
        return $"The song has {IndefiniteArticle.Before(laneName)} set inside a fade that pushes the {laneName} past its range, which cannot be mapped.";
    }

    // Voices sent into the echo before the song's first echo unit, which the import drops.
    public static string EchoHeardBeforeUnit()
    {
        return "The song uses the echo before it sets it up, which cannot be mapped.";
    }

    // A channel's arpeggio taking its timing from another channel.
    public static string ArpeggioTimingFromOtherChannel(int channel)
    {
        return $"Channel #{channel} has an arpeggio timed by another channel, which cannot be mapped.";
    }

    // A channel reaching the song's end with an arpeggio still running.
    public static string ArpeggioRunningAtSongEnd(int channel)
    {
        return $"Channel #{channel} ends with an arpeggio still running, which cannot be mapped.";
    }

    // A channel ending the song with its remote code set otherwise than at the loop point.
    public static string RemoteCodeChangedAcrossLoop(int channel)
    {
        return $"Channel #{channel} ends with different remote code than at its loop point, which cannot be mapped.";
    }
}
