namespace SpcStudio.Model;

// What every lane a note can stand over on three rungs shares, whatever the lane holds: finding the note as the song holds it, and the notes that override, in order.
public static class NoteRungs
{
    // The note as the song holds it now, found by its channel and start tick, or the one handed in where the song has no such note.
    public static Note LiveNote(Song song, int channel, Note note)
    {
        foreach (var heldNote in song.Notes(channel))
        {
            if (heldNote.StartTick == note.StartTick)
            {
                return heldNote;
            }
        }

        return note;
    }

    // The notes of a channel whose rung resolves to a value, in start order and never overlapping, each with what it resolved to.
    public static List<(Note Note, T Value)> OverridingNotes<T>(Song song, int channel, IReadOnlyList<Note>? notes, Func<Note, T?> resolve)
        where T : struct
    {
        return [.. OverridingNotes(song, channel, notes, resolve, value => value.HasValue).Select(overriding => (overriding.Note, overriding.Value!.Value))];
    }

    // The notes of a channel whose resolved value overrides the lane, in start order and never overlapping, each with what it resolved to.
    public static List<(Note Note, T Value)> OverridingNotes<T>(Song song, int channel, IReadOnlyList<Note>? notes, Func<Note, T> resolve, Func<T, bool> doesOverride)
    {
        var overridingNotes = new List<(Note Note, T Value)>();
        foreach (var note in notes ?? song.Notes(channel))
        {
            var value = resolve(note);
            if (doesOverride(value))
            {
                overridingNotes.Add((note, value));
            }
        }

        return OrderedWithoutOverlap(overridingNotes);
    }

    // Moves a lane's next-divider index past a note, handing on each divider before the note's start and skipping each one inside it, and answers
    // the index of the first divider at or after the note's end.
    public static int LaneDividerIndexPastNote<TDivider>(IReadOnlyList<TDivider> laneDividers, int nextLaneDividerIndex, Note note, Action<TDivider> passLaneDivider)
        where TDivider : ILaneChange
    {
        while (nextLaneDividerIndex < laneDividers.Count && laneDividers[nextLaneDividerIndex].Tick < note.StartTick)
        {
            passLaneDivider(laneDividers[nextLaneDividerIndex++]);
        }

        while (nextLaneDividerIndex < laneDividers.Count && laneDividers[nextLaneDividerIndex].Tick < note.EndTick())
        {
            nextLaneDividerIndex++;
        }

        return nextLaneDividerIndex;
    }

    // The notes sorted by start, with each one starting inside the note before it dropped.
    private static List<(Note Note, T Value)> OrderedWithoutOverlap<T>(List<(Note Note, T Value)> overridingNotes)
    {
        overridingNotes.Sort((first, second) => first.Note.StartTick.CompareTo(second.Note.StartTick));
        var keptNotes = new List<(Note, T)>(overridingNotes.Count);
        int lastKeptEndTick = int.MinValue;
        foreach (var overridingNote in overridingNotes)
        {
            if (overridingNote.Note.StartTick < lastKeptEndTick)
            {
                continue;
            }

            keptNotes.Add(overridingNote);
            lastKeptEndTick = overridingNote.Note.EndTick();
        }

        return keptNotes;
    }
}
