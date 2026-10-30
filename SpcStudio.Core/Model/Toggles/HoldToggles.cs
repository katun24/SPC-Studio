namespace SpcStudio.Model;

// One channel's key-off remote blocks, derived from its notes: where its $FC arm and clear fall and whether the slot is armed at a tick.
public static class HoldToggles
{
    // Whether a held note is written a tick short with a one-tick rest of its own behind it, which a held note under the key-off block followed by a note or the song's end is.
    public static bool IsWrittenShort(Song song, int channel, Note note, HeldNoteShape shape)
    {
        if (!note.IsHeld || note.LengthTicks <= 1)
        {
            return false;
        }

        var notes = WrittenNotes(song, channel);
        int noteIndex = notes.FindIndex(written => written.StartTick == note.StartTick);
        return noteIndex >= 0 && !DoesRestFollow(song, notes, noteIndex) && !IsWrittenAsLegato(song, notes, noteIndex, shape);
    }

    // Whether the note at an index is written whole under the legato rather than under the key-off block: a held note longer than a tick, played, running straight into a note inside the song, bound
    // to neither neighbor nor straight after a bound note, and clear of the loop's start, where the shape asks for it.
    public static bool IsWrittenAsLegato(Song song, IReadOnlyList<Note> notes, int noteIndex, HeldNoteShape shape)
    {
        var note = notes[noteIndex];
        if (shape != HeldNoteShape.Legato || !note.IsHeld || note.LengthTicks <= 1 || !song.IsNotePlayable(note) || note.EndTick() >= song.EndTick)
        {
            return false;
        }

        if (DoesRestFollow(song, notes, noteIndex))
        {
            return false;
        }

        if (song.IsLooping && note.StartTick <= song.LoopTick && song.LoopTick <= note.EndTick())
        {
            return false;
        }

        bool isBoundFromPrevious = noteIndex > 0 && LegatoToggles.DoesBindNext(notes, noteIndex - 1);
        bool isRightAfterBoundNote = noteIndex > 1 && LegatoToggles.DoesBindNext(notes, noteIndex - 2) && notes[noteIndex - 1].EndTick() == note.StartTick;
        return !isBoundFromPrevious && !isRightAfterBoundNote && !LegatoToggles.DoesBindNext(notes, noteIndex);
    }

    // Whether any note in the song is held under the key-off block.
    public static bool IsKeyOffBlockUsed(Song song, HeldNoteShape shape)
    {
        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            var notes = song.Notes(channel);
            for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
            {
                if (notes[noteIndex].IsHeld && notes[noteIndex].LengthTicks > 1 && !IsWrittenAsLegato(song, notes, noteIndex, shape))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Steps the slot a note at a time, writing the fewest commands that leave every note keying off as its own flag asks and every rest after a held note keyed off where it starts: the header arming
    // it where the channel's first note is held and starts the song, and the arms and clears after it in tick order, a note written under the legato asking for no arm.
    public static HoldCommandList For(Song song, int channel, HeldNoteShape shape)
    {
        var notes = WrittenNotes(song, channel);
        var armAndClearTicks = new List<(int Tick, bool Arms)>();
        bool doesHeaderArm = false;
        bool isArmed = false;
        for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            var note = notes[noteIndex];

            bool isHeldUnderKeyOffBlock = note.IsHeld && !IsWrittenAsLegato(song, notes, noteIndex, shape);
            if (note.LengthTicks > 1 && isHeldUnderKeyOffBlock != isArmed)
            {
                isArmed = isHeldUnderKeyOffBlock;
                if (note.StartTick == 0)
                {
                    doesHeaderArm = isArmed;
                }
                else
                {
                    armAndClearTicks.Add((note.StartTick, isArmed));
                }
            }

            if (isArmed && DoesRestFollow(song, notes, noteIndex))
            {
                isArmed = false;
                armAndClearTicks.Add((note.EndTick(), false));
            }
        }

        return new(doesHeaderArm, armAndClearTicks);
    }

    // A channel's notes as the export writes them: none from the song's end on, and one crossing it cut off there.
    private static List<Note> WrittenNotes(Song song, int channel)
    {
        return
        [
            .. song.Notes(channel)
                .Where(note => note.StartTick < song.EndTick)
                .Select(note => note.EndTick() <= song.EndTick ? note : note with { LengthTicks = song.EndTick - note.StartTick }),
        ];
    }

    // Whether a rest follows the note at an index: a gap before the next note, or for the last one, before the song's end.
    private static bool DoesRestFollow(Song song, IReadOnlyList<Note> notes, int noteIndex)
    {
        if (noteIndex + 1 < notes.Count)
        {
            return notes[noteIndex + 1].StartTick > notes[noteIndex].EndTick();
        }

        return notes[noteIndex].EndTick() < song.EndTick;
    }
}
