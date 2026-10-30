namespace SpcStudio.Model;

// One channel's legato, derived from its notes: where its $F4 $01 toggles fall and what state they leave the channel's bit in the driver's $0161 in.
public static class LegatoToggles
{
    // The ticks the channel writes a $F4 $01 at after its header, in order, a tick repeated where the gap needs two, and none at the loop marker.
    public static IEnumerable<int> ToggleTicks(Song song, int channel, int markerTick, HeldNoteShape shape)
    {
        foreach (int toggleTick in For(song, channel, shape).ToggleTicks)
        {
            if (toggleTick < song.EndTick && toggleTick != markerTick)
            {
                yield return toggleTick;
            }
        }
    }

    // Whether the note at an index hands over to the one after it: its own flag, and the next note starting where it ends.
    public static bool DoesBindNext(IReadOnlyList<Note> notes, int noteIndex)
    {
        return notes[noteIndex].IsLegato && noteIndex + 1 < notes.Count && notes[noteIndex + 1].StartTick == notes[noteIndex].EndTick();
    }

    // Whether a note of a stretch played over and over hands over to the one after it - the note panel's loop - the last to the first again, which starts directly after it.
    public static bool DoesBindNextInLoop(IReadOnlyList<Note> stretch, int noteIndex)
    {
        if (noteIndex + 1 < stretch.Count)
        {
            return DoesBindNext(stretch, noteIndex);
        }

        var lastNote = stretch[noteIndex];
        return DoesBindNext([lastNote, stretch[0] with { StartTick = lastNote.EndTick() }], 0);
    }

    // Whether the note before one of a stretch played over and over hands over to it, the first being handed over to by the last.
    public static bool IsBoundFromPreviousInLoop(IReadOnlyList<Note> stretch, int noteIndex)
    {
        return DoesBindNextInLoop(stretch, (noteIndex + stretch.Count - 1) % stretch.Count);
    }

    // Steps the driver's two bits a note at a time, writing the fewest toggles that leave every boundary as its note asked for, a note written whole
    // under the legato keying on through the key-on block wherever the bits would bind it.
    public static LegatoToggleList For(Song song, int channel, HeldNoteShape shape)
    {
        var notes = song.Notes(channel);
        var toggleTicks = new List<int>();
        if (notes.Count == 0)
        {
            return new(false, toggleTicks, new HoldCommandList(false, []));
        }

        var notesKeyingOnThroughBlock = new List<int>();
        var boundNotes = new List<int>();

        // Whether the header toggles $0161, this channel's bit, which is what the first note needs.
        bool doesHeaderToggle = DoesBindNext(notes, 0) || HoldToggles.IsWrittenAsLegato(song, notes, 0, shape);

        bool isBitSet = doesHeaderToggle;

        // Whether $0162 says this voice has a note going.
        bool isNoteGoing = false;
        for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            if (noteIndex > 0 && notes[noteIndex].StartTick > notes[noteIndex - 1].EndTick())
            {
                isNoteGoing = false;
            }

            bool isWrittenAsLegato = HoldToggles.IsWrittenAsLegato(song, notes, noteIndex, shape);
            bool isBoundFromPrevious = noteIndex > 0 && DoesBindNext(notes, noteIndex - 1);
            if (isBoundFromPrevious)
            {
                boundNotes.Add(noteIndex);
            }
            else
            {
                bool isBitNeeded = DoesBindNext(notes, noteIndex) || isWrittenAsLegato;
                if (isBitSet != isBitNeeded)
                {
                    toggleTicks.Add(notes[noteIndex].StartTick);
                    isBitSet = isBitNeeded;
                    isNoteGoing = false;
                }
                else if (isBitSet && isNoteGoing && isWrittenAsLegato)
                {
                    notesKeyingOnThroughBlock.Add(noteIndex);
                }
                else if (isBitSet && isNoteGoing)
                {
                    toggleTicks.Add(notes[noteIndex].StartTick);
                    toggleTicks.Add(notes[noteIndex].StartTick);
                    isNoteGoing = false;
                }
            }

            isNoteGoing = true;
        }

        return new(doesHeaderToggle, toggleTicks, KeyOnArms(notes, notesKeyingOnThroughBlock, boundNotes, HoldToggles.For(song, channel, shape)));
    }

    // The key-on block's arms and clears: armed in front of the first note of each stretch keying on through it and cleared behind the last, a bound
    // note ending a stretch, and armed again behind every clear of the key-off block inside one, which clears both slots.
    private static HoldCommandList KeyOnArms(IReadOnlyList<Note> notes, List<int> notesKeyingOnThroughBlock, List<int> boundNotes, HoldCommandList holdCommands)
    {
        var armAndClearTicks = new List<(int Tick, bool Arms)>();

        for (int stretchStart = 0; stretchStart < notesKeyingOnThroughBlock.Count;)
        {
            int stretchEnd = stretchStart;
            while (stretchEnd + 1 < notesKeyingOnThroughBlock.Count
                   && !boundNotes.Any(bound => bound > notesKeyingOnThroughBlock[stretchEnd] && bound < notesKeyingOnThroughBlock[stretchEnd + 1]))
            {
                stretchEnd++;
            }

            int armTick = notes[notesKeyingOnThroughBlock[stretchStart]].StartTick;
            int clearTick = notes[notesKeyingOnThroughBlock[stretchEnd]].EndTick();

            armAndClearTicks.Add((armTick, true));
            foreach (var (holdClearTick, _) in holdCommands.ArmAndClearTicks.Where(command => !command.Arms && command.Tick > armTick && command.Tick < clearTick))
            {
                armAndClearTicks.Add((holdClearTick, true));
            }

            armAndClearTicks.Add((clearTick, false));
            stretchStart = stretchEnd + 1;
        }

        return new(false, armAndClearTicks);
    }
}
