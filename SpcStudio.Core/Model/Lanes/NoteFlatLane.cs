namespace SpcStudio.Model;

// One flat lane a note can stand over on three rungs, a value one number per region that never fades: the note's own value, its instrument's or the
// lane's, and the merge that lays the notes' values over the lane.
public sealed class NoteFlatLane : ILaneRungs
{
    private readonly Func<Song, int, int> _laneBase;
    private readonly Func<Song, int, IEnumerable<LaneValueChange>> _laneDividers;
    private readonly Func<Note, LaneSource> _sourceOf;
    private readonly Func<Note, int> _ownValueOf;
    private readonly Func<Instrument?, int> _instrumentValueOf;
    private readonly Action<Song, int, int, LaneSource, int> _setNoteValue;

    // A lane over its own base and dividers, which rung a note stands on, a note's own value, an instrument's value, and the writer of a note's rung and value by channel and start tick.
    public NoteFlatLane(
        Func<Song, int, int> laneBase,
        Func<Song, int, IEnumerable<LaneValueChange>> laneDividers,
        Func<Note, LaneSource> sourceOf,
        Func<Note, int> ownValueOf,
        Func<Instrument?, int> instrumentValueOf,
        Action<Song, int, int, LaneSource, int> setNoteValue)
    {
        _laneBase = laneBase;
        _laneDividers = laneDividers;
        _sourceOf = sourceOf;
        _ownValueOf = ownValueOf;
        _instrumentValueOf = instrumentValueOf;
        _setNoteValue = setNoteValue;
    }

    // What the note plays by its own rung - its own value, its instrument's, or null where it overrides nothing.
    public int? ResolvedValue(Song song, Note note)
    {
        return _sourceOf(note) switch
        {
            LaneSource.Note => _ownValueOf(note),
            LaneSource.Instrument => _instrumentValueOf(song.InstrumentOf(note)),
            _ => null,
        };
    }

    // The value an instrument's rung holds, and the lane's default without an instrument.
    public int InstrumentValue(Instrument? instrument)
    {
        return _instrumentValueOf(instrument);
    }

    // What the lane alone is at on a channel at a tick, no note laid over it.
    public int LaneValueAt(Song song, int channel, int tick)
    {
        var dividers = _laneDividers(song, channel).ToList();
        int dividersAtOrBeforeTick = SortedSearch.CountLeading(dividers, divider => divider.Tick <= tick);
        return dividersAtOrBeforeTick == 0 ? _laneBase(song, channel) : dividers[dividersAtOrBeforeTick - 1].Value;
    }

    // The one value the note plays across the whole of itself, or null where the lane beneath it changes inside it.
    public int? ValueHeldThroughout(Song song, int channel, Note note)
    {
        if (ResolvedValue(song, note) is { } resolvedValue)
        {
            return resolvedValue;
        }

        bool doesLaneChangeInside = _laneDividers(song, channel).Any(divider => divider.Tick > note.StartTick && divider.Tick < note.EndTick());
        return doesLaneChangeInside ? null : LaneValueAt(song, channel, note.StartTick);
    }

    // What the note plays: its rung's value, or the lane's where it overrides nothing.
    public int ValuePlayedBy(Song song, int channel, Note note)
    {
        var liveNote = NoteRungs.LiveNote(song, channel, note);
        return ResolvedValue(song, liveNote) ?? LaneValueAt(song, channel, liveNote.StartTick);
    }

    // Which rung the note as the song holds it stands on.
    public LaneSource LiveSourceOf(Song song, int channel, Note note)
    {
        return _sourceOf(NoteRungs.LiveNote(song, channel, note));
    }

    // Gives the note a value of its own, which puts it on its own rung.
    public void ApplyOverride(Song song, int channel, Note note, int value)
    {
        _setNoteValue(song, channel, note.StartTick, LaneSource.Note, value);
    }

    // Moves a note to a rung without changing what it plays: onto its own it takes the value it was already playing, and off it its own value goes back to zero.
    public void SetSource(Song song, int channel, Note note, LaneSource source)
    {
        var liveNote = NoteRungs.LiveNote(song, channel, note);
        int ownValue = source == LaneSource.Note ? ValuePlayedBy(song, channel, liveNote) : 0;
        _setNoteValue(song, channel, liveNote.StartTick, source, ownValue);
    }

    // One channel's lane as the lane and its notes together say it: the lane's own base and dividers, with each overriding note's value laid over its own span.
    public (int Base, IReadOnlyList<LaneValueChange> Changes) Merged(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        int laneBase = _laneBase(song, channel);
        var laneDividers = _laneDividers(song, channel).ToList();
        var overridingNotes = NoteRungs.OverridingNotes(song, channel, notes, note => ResolvedValue(song, note));
        if (overridingNotes.Count == 0)
        {
            return (laneBase, laneDividers);
        }

        var mergedDividers = new List<LaneValueChange>();
        int mergedBase = laneBase;
        int nextLaneDividerIndex = 0;

        // Writes a divider a note says, replacing anything already on its tick and writing nothing where the value is the one already in force.
        void PlaceNoteDivider(int tick, int value)
        {
            if (tick <= 0)
            {
                mergedBase = value;
                return;
            }

            if (mergedDividers.Count > 0 && mergedDividers[^1].Tick == tick)
            {
                mergedDividers.RemoveAt(mergedDividers.Count - 1);
            }

            int valueInForce = mergedDividers.Count > 0 ? mergedDividers[^1].Value : mergedBase;
            if (value != valueInForce)
            {
                mergedDividers.Add(new(tick, value));
            }
        }

        foreach (var (note, value) in overridingNotes)
        {
            nextLaneDividerIndex = NoteRungs.LaneDividerIndexPastNote(laneDividers, nextLaneDividerIndex, note, mergedDividers.Add);
            PlaceNoteDivider(note.StartTick, value);

            bool isLaneDividerOnNoteEnd = nextLaneDividerIndex < laneDividers.Count && laneDividers[nextLaneDividerIndex].Tick == note.EndTick();
            if (isLaneDividerOnNoteEnd)
            {
                continue;
            }

            PlaceNoteDivider(note.EndTick(), LaneValueAt(song, channel, note.EndTick()));
        }

        mergedDividers.AddRange(laneDividers.Skip(nextLaneDividerIndex));

        return (mergedBase, mergedDividers);
    }
}
