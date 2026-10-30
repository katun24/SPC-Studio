using SpcStudio.Model;

namespace SpcStudio.Import;

// The refusals of building a song out of what was read, whether from a text file or from a ROM: its channels, instruments, samples, lanes, echo, envelopes and pitch bends.
public sealed partial class MmlImportException
{
    // An $F6 in one channel writing a register of another channel's voice.
    public static MmlImportException DspWriteToOtherVoice(int channel, int register, int voice, int line)
    {
        return new($"A '$F6' in channel #{channel} writes DSP register ${register:X2}, which belongs to voice {voice}.", line);
    }

    // A loop point in a song that does not loop.
    public static MmlImportException LoopPointUnderNoLoop(int line)
    {
        return new("A '/' loop point in a song with #option noloop, which plays it as an intro rather than a loop.", line);
    }

    // A bent note the transposes move out of the driver's range and back.
    public static MmlImportException BendOutOfRangeAndBack(int channel, int tick, int transpose, int globalTranspose, int line)
    {
        return new($"Channel #{channel} bends a note at tick {tick} that {TransposesMovingNoteClause(transpose, globalTranspose)} out of the driver's own C1 to B7 range and back into it.", line);
    }

    // A note a pitch envelope moves out of the driver's range.
    public static MmlImportException PitchEnvelopeOutOfRange(int channel, int tick, int extent, int noteValue, int line)
    {
        return new($"Channel #{channel} has a note at tick {tick} that a pitch envelope of {extent} semitones moves {OutOfRangeClause(noteValue)}", line);
    }

    // A note overlapping the one before it in the same channel.
    public static MmlImportException NotesOverlap(int channel, int tick, int earlierTick, int line)
    {
        return new($"Channel #{channel} has a note at tick {tick} overlapping the one at tick {earlierTick}.", line);
    }

    // An $F6 write to FLG that resets or mutes the chip.
    public static MmlImportException FlagsWriteResetsOrMutes(int tick, int line)
    {
        return new($"A '$F6' write to FLG at tick {tick} resets or mutes the chip, which is not modeled.", line);
    }

    // An $F6 write to FLG that changes the noise clock.
    public static MmlImportException FlagsWriteChangesNoiseClock(int tick, int line)
    {
        return new($"A '$F6' write to FLG at tick {tick} changes the noise clock, which is not modeled.", line);
    }

    // An $F6 write to FLG stopping the echo writing into a buffer notes have already played into.
    public static MmlImportException FlagsWriteStopsUsedEcho(int tick, int line)
    {
        return new($"A '$F6' write to FLG at tick {tick} stops the echo writing into a buffer notes have already played into, which is not modeled.", line);
    }

    // An $F6 write to FLG starting the echo writing where no echo is running.
    public static MmlImportException FlagsWriteStartsEchoWithoutEcho(int tick, int line)
    {
        return new($"A '$F6' write to FLG at tick {tick} starts the echo writing into its buffer where no echo is running, which is not modeled.", line);
    }

    // An $F6 write to FLG leaving the echo writing stopped at the song's end but not at its loop point.
    public static MmlImportException FlagsWriteStopsEchoOnlyAtEnd(int line)
    {
        return new("A '$F6' write to FLG leaves the echo writing into its buffer stopped at the song's end but not at the loop point, which is not modeled.", line);
    }

    // An $F1 filter index past the driver's FIR tables.
    public static MmlImportException EchoFilterPastPresets(int filterIndex, int line)
    {
        return new($"'$F1' with echo filter ${filterIndex:X2}; there are only {SnesEcho.PresetCount} FIR tables, and a higher index reads past them.", line);
    }

    // An echo delay outside the driver's range, with the command that wrote it.
    public static MmlImportException EchoDelayOutOfRange(string commandName, int delay, int line)
    {
        return new($"'{commandName}' with an echo delay of ${delay:X2}; the driver's own is ${SnesEcho.MinDelay:X2} to ${SnesEcho.MaxDelay:X2}.", line);
    }

    // An envelope write that leaves registers other than the ones the song goes on to play.
    internal static MmlImportException EnvelopeLeftUnplayed(
        MmlImporter.EnvelopeWrite wrote, int writeTick, int channel, int audibleTick, EnvelopeState written, EnvelopeState played, int line)
    {
        string leavesPhrase = audibleTick == writeTick ? "leaves" : $"leaves, at the tick {audibleTick} it is next heard at,";
        return new(
            $"{AsSentenceStart(NameOfEnvelopeWrite(wrote))} at tick {writeTick} in channel #{channel} {leavesPhrase} ADSR1/ADSR2/GAIN "
            + $"at ${written.Adsr1:X2}/${written.Adsr2:X2}/${written.Gain:X2}, where the song plays ${played.Adsr1:X2}/${played.Adsr2:X2}/${played.Gain:X2}.", line);
    }

    // A change of tuning a sounding noise note's own pitch bend or running vibrato would carry.
    internal static MmlImportException TuningUnderSweepOnNoise(MmlImporter.EnvelopeWrite wrote, int tick, int channel, int line)
    {
        return new(
            $"{AsSentenceStart(NameOfEnvelopeWrite(wrote))} at tick {tick} in channel #{channel} changes the tuning of a noise note sounding under a pitch bend or a "
            + "vibrato, which this model cannot hold.", line);
    }

    // A change of tuning inside a note that a tremolo runs across, where the cut that would carry it restarts the tremolo.
    internal static MmlImportException TuningUnderTremolo(MmlImporter.EnvelopeWrite wrote, int tick, int channel, int line)
    {
        return new(
            $"{AsSentenceStart(NameOfEnvelopeWrite(wrote))} at tick {tick} in channel #{channel} changes the tuning of a note that a tremolo runs across, so the note "
            + "cannot be cut there without restarting the tremolo.", line);
    }

    // A custom instrument past the end of the #instruments block.
    public static MmlImportException InstrumentPastBlock(int instrumentNumber, int blockCount, int line)
    {
        return new($"@{instrumentNumber} is past the end of the #instruments block, which holds {blockCount}.", line);
    }

    // A sample load command playing a file the #samples block does not load.
    public static MmlImportException SampleLoadFileNotLoaded(string? fileName, int line)
    {
        return new($"A sample load command plays \"{fileName}\", which the #samples block does not load.", line);
    }

    // A sample group the #samples block names that AddmusicK's groups file does not define.
    public static MmlImportException SampleGroupNotInInstall(string groupName, int line)
    {
        return new(SampleGroups.NotInInstallSentence(groupName), line);
    }

    // An instrument entry naming a sample the #samples block does not list.
    public static MmlImportException InstrumentSampleNotListed(string? fileName, int line)
    {
        return new($"Instrument \"{fileName}\" names a sample the #samples block does not list.", line);
    }

    // A sample file that could not be read.
    public static MmlImportException SampleUnreadable(string path, int line)
    {
        return new($"Sample \"{path}\" could not be read.", line);
    }

    // A sample file that is not a BRR sample.
    public static MmlImportException NotBrrSample(string path, int line)
    {
        return new($"\"{path}\" is not a valid BRR sample.", line);
    }

    // A sample position past the end of the #samples block, with what plays it.
    public static MmlImportException SamplePastTable(string playedBy, int position, int tableCount, int line)
    {
        return new($"{AsSentenceStart(playedBy)} plays sample {position}, where the #samples block loads {tableCount}.", line);
    }

    // A sample position holding a placeholder with nothing after it, with what plays it.
    public static MmlImportException SamplePlaceholderOnly(string playedBy, int position, int line)
    {
        return new($"{AsSentenceStart(playedBy)} plays sample {position}, which the #samples block loads as an empty placeholder with nothing after it.", line);
    }

    // A sample file that is in neither place AddmusicK looks for it.
    public static MmlImportException SampleNotFound(string name, string besideSongPath, string samplesFolderPath)
    {
        return new($"Sample \"{name}\" is not at {besideSongPath} or at {samplesFolderPath}.", 0);
    }

    // A switch to the other velocity table.
    public static MmlImportException VelocityTableSwitch(int tick, int line)
    {
        return new($"The song moves to the other velocity table at tick {tick}, which is not modeled.", line);
    }

    // A $DD that a fetch reaches unread.
    public static MmlImportException UnreadBend(int line)
    {
        return new("A '$DD' pitch bend is not read before the next byte is fetched, which stops the driver.", line);
    }

    // A $DD with nothing fetched in front of it.
    public static MmlImportException BendWithNothingInFront(int line)
    {
        return new("A pitch bend has no note in front of it to bend.", line);
    }

    // A $DD with a loop's edge between it and the byte in front.
    public static MmlImportException BendBehindLoop(int line)
    {
        return new("A pitch bend behind a loop is never read ahead by the driver.", line);
    }

    // A pitch bend after a tie whose voice is already keyed off.
    public static MmlImportException BendAfterKeyedOffTie(int line)
    {
        return new("A pitch bend follows a tie whose voice is already keyed off, at a place between two of the tie's bytes.", line);
    }

    // A vibrato fade written behind a sweep cut off part-way through its own fade.
    public static MmlImportException VibratoFadeAfterCutSweep(int line)
    {
        return new("A vibrato fade cannot be written where the sweep in front of it was cut off part-way through its own fade.", line);
    }

    // A song zero ticks long.
    public static MmlImportException SongZeroTicks()
    {
        return new("The song is zero ticks long.", 0);
    }

    // A loop point not before the song's end.
    public static MmlImportException LoopPointNotBeforeEnd(int loopTick, int songEndTick)
    {
        return new($"The '/' loop point is at tick {loopTick}, which is not before the song's end at {songEndTick}.", 0);
    }

    // Hot patches disagreeing on whether the driver looks inside loops.
    public static MmlImportException HotPatchesDisagreeOnReadAhead(int line)
    {
        return new("The song's '$FA $7F' and '$FA $FE' hot patches do not agree on whether the driver looks inside loops, and its loops play differently either way.", line);
    }

    // Which transposes moved a note: whichever is not zero, or both.
    private static string TransposesMovingNoteClause(int transpose, int globalTranspose)
    {
        return (transpose, globalTranspose) switch
        {
            (0, _) => $"an '$E4' global transpose of {globalTranspose} moves",
            (_, 0) => $"a '$FA $02' semitone transpose of {transpose} moves",
            _ => $"a '$FA $02' semitone transpose of {transpose} and an '$E4' global transpose of {globalTranspose} together move",
        };
    }

    // How far a note value falls outside the driver's own range.
    private static string OutOfRangeClause(int noteValue)
    {
        if (noteValue > SnesPitch.MaxPlayableNoteValue)
        {
            return $"{noteValue - SnesPitch.MaxPlayableNoteValue} above the driver's own C1 to B7 range.";
        }

        return $"{SnesPitch.MinNoteValue - noteValue} below the driver's own C1 to B7 range.";
    }

    // The command an envelope write was written as.
    private static string NameOfEnvelopeWrite(MmlImporter.EnvelopeWrite write)
    {
        return write switch
        {
            MmlImporter.EnvelopeWrite.Override => "an '$ED'",
            MmlImporter.EnvelopeWrite.Gain => "a '$FA $01'",
            MmlImporter.EnvelopeWrite.DspWrite => "a '$F6'",
            MmlImporter.EnvelopeWrite.RemoteGain => "a remote gain",
            MmlImporter.EnvelopeWrite.Restore => "a '$F4 $09'",
            MmlImporter.EnvelopeWrite.KeyOnRestore => "the key-on restore of a remote code type 5 or 6",
            MmlImporter.EnvelopeWrite.SampleLoad => "a '$F3'",
            _ => "an '@'",
        };
    }
}
