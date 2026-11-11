using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds what the key-on remote slot holds between the key-on blocks' runs: the legato's block that
// keys held notes on, the envelope reset a channel's release runs arm once, or both.
public static partial class MmlExporter
{
    private const string LegatoHoldArmPlaceholder = "(!0,-1)";
    private const string EnvelopeResetArmPlaceholder = "(!0,-1,1)";
    private const string EnvelopeResetAndLegatoHoldArmPlaceholder = "(!0,-1,2)";
    private const string KeyOnClearCommand = "(!!-1)";
    private const int ArmAndClearCount = 2;

    private static readonly string[] KeyOnSlotArmPlaceholders = [LegatoHoldArmPlaceholder, EnvelopeResetArmPlaceholder, EnvelopeResetAndLegatoHoldArmPlaceholder];
    private static readonly HoldCommandList NoKeyOnSlotArms = new(false, []);

    // The commands of the block a stand-in arms the slot with between runs, or null where the token is no such stand-in.
    private static string[]? KeyOnSlotBlockCommandsOf(string token)
    {
        return token switch
        {
            LegatoHoldArmPlaceholder => LegatoHoldBlockCommands(),
            EnvelopeResetArmPlaceholder => [EnvResetName.Write()],
            EnvelopeResetAndLegatoHoldArmPlaceholder => [EnvResetName.Write(), .. LegatoHoldBlockCommands()],
            _ => null,
        };
    }

    // The stand-in for the arm of the block between runs whose commands are a text, or null where no such block holds them.
    private static string? KeyOnSlotPlaceholderFor(string blockText)
    {
        return KeyOnSlotArmPlaceholders.FirstOrDefault(placeholder => string.Join(' ', KeyOnSlotBlockCommandsOf(placeholder)!) == blockText);
    }

    // The command that arms the slot between runs with the legato's block, the envelope reset or both, or the one that clears it where it holds neither.
    private static string KeyOnSlotCommandFor(bool isLegatoHoldArmed, bool isEnvelopeResetArmed)
    {
        if (isEnvelopeResetArmed)
        {
            return isLegatoHoldArmed ? EnvelopeResetAndLegatoHoldArmPlaceholder : EnvelopeResetArmPlaceholder;
        }

        return isLegatoHoldArmed ? LegatoHoldArmPlaceholder : KeyOnClearCommand;
    }

    // The slot's commands between runs on a channel: one wherever the legato's block or the envelope reset is armed or cleared, writing what the slot
    // holds after every command at that tick.
    private static IEnumerable<TimedCommand> KeyOnSlotCommands(HoldCommandList legatoHoldArms, HoldCommandList envelopeResetArms)
    {
        return KeyOnSlotTicks(legatoHoldArms, envelopeResetArms)
            .Distinct()
            .Order()
            .Select(tick => new TimedCommand(tick, KeyOnSlotOrder, KeyOnSlotCommandFor(legatoHoldArms.IsArmedAt(tick), envelopeResetArms.IsArmedAt(tick))));
    }

    // The slot's command between runs written again just after the loop marker, where what it holds as the loop starts differs from what it holds
    // behind the restatements in front of it - what the song's end leaves, or nothing where they clear both slots - and nothing at the marker's tick
    // says it.
    private static IEnumerable<TimedCommand> KeyOnSlotRestatement(
        HoldCommandList legatoHoldArms, HoldCommandList envelopeResetArms, int loopTick, int lastTick, IReadOnlyList<TimedCommand> restatementsInFront)
    {
        string atLoop = KeyOnSlotCommandFor(legatoHoldArms.IsArmedAt(loopTick), envelopeResetArms.IsArmedAt(loopTick));
        string inFront = restatementsInFront.Any(command => HoldOffName.IsNameOf(command.Text))
            ? KeyOnClearCommand
            : KeyOnSlotCommandFor(legatoHoldArms.IsArmedAt(lastTick), envelopeResetArms.IsArmedAt(lastTick));

        if (IsRestatementNeeded(KeyOnSlotTicks(legatoHoldArms, envelopeResetArms).Contains(loopTick), atLoop, inFront))
        {
            yield return new TimedCommand(loopTick, KeyOnSlotOrder, atLoop);
        }
    }

    // Every tick the legato's block or the envelope reset is armed or cleared at.
    private static IEnumerable<int> KeyOnSlotTicks(HoldCommandList legatoHoldArms, HoldCommandList envelopeResetArms)
    {
        return legatoHoldArms.ArmAndClearTicks.Concat(envelopeResetArms.ArmAndClearTicks).Select(command => command.Tick);
    }

    // A channel's commands with the envelope reset its release runs arm in place: its own resets taken out where a note keys on under the armed one.
    private static IEnumerable<TimedCommand> CommandsUnderEnvelopeReset(IEnumerable<TimedCommand> commands, Song song, int channel, HoldCommandList envelopeResetArms)
    {
        var resetNoteStarts = PlayedNotes(song, channel).Select(note => note.StartTick).Where(envelopeResetArms.IsArmedAt).ToHashSet();
        return commands.Where(command => command.SortOrder != EnvelopeRestoreOrder || !resetNoteStarts.Contains(command.Tick));
    }

    // Where a channel arms and clears the envelope reset: over each stretch of notes keying on where a reset changes nothing, kept where the resets it
    // takes out save more than its arm, its clear and its arms again behind every clear of both slots inside it cost, and nowhere on a channel with no
    // release run.
    private static HoldCommandList EnvelopeResetArms(Song song, int channel, HeldNoteShape shape, IReadOnlyList<ReleaseRun> releaseRuns, IReadOnlyList<ArpeggioRun> arpeggioRuns)
    {
        if (releaseRuns.Count == 0)
        {
            return NoKeyOnSlotArms;
        }

        var notes = PlayedNotes(song, channel).ToList();
        var envelope = EnvelopeMap.For(song, channel);
        var restoreTicks = envelope.RestoreTicks().ToHashSet();
        List<int> clearTicksOfBothSlots =
        [
            .. HoldToggles.For(song, channel, shape).ArmAndClearTicks.Where(command => !command.Arms).Select(command => command.Tick),
            .. releaseRuns.Select(run => run.ClearTick),
        ];
        int resetBytes = WrittenTokens.CompiledBytesOf(EnvResetName.Write());

        var stretches = new List<(int ArmTick, int ClearTick)>();
        bool doesFirstNoteStartStretch = false;
        bool doesLastNoteEndStretch = false;
        for (int firstNote = 0; firstNote < notes.Count;)
        {
            if (!DoesResetChangeNothingAt(song, envelope, arpeggioRuns, notes[firstNote]))
            {
                firstNote++;
                continue;
            }

            int lastNote = firstNote;
            while (lastNote + 1 < notes.Count && DoesResetChangeNothingAt(song, envelope, arpeggioRuns, notes[lastNote + 1]))
            {
                lastNote++;
            }

            var stretch = (ArmTick: notes[firstNote].StartTick, ClearTick: notes[lastNote].EndTick());
            int resetsTakenOut = notes[firstNote..(lastNote + 1)].Count(note => restoreTicks.Contains(note.StartTick));
            int armsAgain = HoldCommandList.ClearTicksInside(clearTicksOfBothSlots, stretch.ArmTick, stretch.ClearTick).Count();
            if (resetsTakenOut * resetBytes > (ArmAndClearCount + armsAgain) * SongDataSize.RemoteCallBytes)
            {
                stretches.Add(stretch);
                doesFirstNoteStartStretch |= firstNote == 0;
                doesLastNoteEndStretch = lastNote == notes.Count - 1;
            }

            firstNote = lastNote + 1;
        }

        if (song.IsLooping && LoopMarkerTick(song) == NoLoopMarker && doesLastNoteEndStretch && !doesFirstNoteStartStretch)
        {
            stretches.RemoveAt(stretches.Count - 1);
        }

        return HoldCommandList.ForStretches(stretches, clearTicksOfBothSlots);
    }

    // Whether the envelope reset changes nothing where a note keys on: the note keys on under its instrument's own envelope, on an instrument that is
    // neither noise nor percussion, outside every arpeggio run.
    private static bool DoesResetChangeNothingAt(Song song, EnvelopeMap envelope, IReadOnlyList<ArpeggioRun> arpeggioRuns, Note note)
    {
        var instrument = song.InstrumentOf(note)!;
        return !instrument.IsNoise
               && !instrument.IsPercussion
               && envelope.StateAt(note.StartTick) == envelope.InstrumentEnvelopeAt(note.StartTick)
               && !ArpeggioRuns.IsInAnyRun(arpeggioRuns, note);
    }
}
