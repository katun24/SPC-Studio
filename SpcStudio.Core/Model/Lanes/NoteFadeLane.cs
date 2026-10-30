using System.Collections.Immutable;

namespace SpcStudio.Model;

// One fading lane a note can stand over on three rungs: the note's own rows, its instrument's rows, and the merge that lays whichever rows the rung
// names over the lane's own base and dividers. The channel volume and the pan are its two instances.
public sealed class NoteFadeLane<TState> : INoteChain<TState>, ILaneRungs where TState : struct
{
    // The lane's own base on a channel, which is what tick 0 holds.
    public required Func<Song, int, TState> LaneBase { get; init; }

    // The lane's own dividers on a channel, in tick order.
    public required Func<Song, int, IReadOnlyList<LaneDivider<TState>>> LaneDividers { get; init; }

    // The state in force on a channel at a tick, the notes laid over the lane.
    public required Func<Song, int, int, TState> MergedStateAt { get; init; }

    // A reader of the lane alone on a channel, built once and then asked per tick.
    public required Func<Song, int, Func<int, TState>> LaneReader { get; init; }

    // The byte a state carries, which is what a ramp interpolates.
    public required Func<TState, int> ValueOf { get; init; }

    // A state at another byte, with whatever else it carries left as it was.
    public required Func<TState, int, TState> WithValue { get; init; }

    // The bytes the lane's command takes.
    public required ValueRange ValueRange { get; init; }

    // Which rung a note stands on.
    public required Func<Note, LaneSource> SourceOf { get; init; }

    // A note's own rows, which are read only on its own rung.
    public required Func<Note, ImmutableArray<NoteChainRegion<TState>>> OwnChainOf { get; init; }

    // Writes a note's rung and own rows, by channel and start tick.
    public required Action<Song, int, int, LaneSource, ImmutableArray<NoteChainRegion<TState>>> SetNoteChain { get; init; }

    // The state a note plays where there is no instrument to take one from.
    public required TState DefaultState { get; init; }

    // An instrument's rows, the first at a note's start and the last running to each note's end.
    public required Func<Instrument, ImmutableArray<NoteChainRegion<TState>>> InstrumentRows { get; init; }

    // Writes an instrument's rows.
    public required Action<Instrument, ImmutableArray<NoteChainRegion<TState>>> SetInstrumentRows { get; init; }

    // The state in force on a channel at a tick, overrides included - which is what sounds there.
    public TState StateAt(Song song, int channel, int tick)
    {
        return MergedStateAt(song, channel, tick);
    }

    // The state the lane alone is at on a channel at a tick, with no note's override laid over it.
    public TState LaneStateAt(Song song, int channel, int tick)
    {
        return LaneReader(song, channel)(tick);
    }

    // A row at another state, its ramp dropped where it would end on the byte it now sets out from.
    public NoteChainRegion<TState> WithState(NoteChainRegion<TState> row, TState state)
    {
        return row with { State = state, FadeTo = row.FadeTo == ValueOf(state) ? null : row.FadeTo };
    }

    // A row ramping to a byte, or holding flat where that byte is the one it sets out from.
    public NoteChainRegion<TState> WithFadeTo(NoteChainRegion<TState> row, int fadeTo)
    {
        return row with { FadeTo = fadeTo == ValueOf(row.State) ? null : fadeTo };
    }

    // A row with the byte it sets and the one it ramps to held to the lane's range.
    public NoteChainRegion<TState> ClampedToRange(NoteChainRegion<TState> row)
    {
        var valueClampedRow = WithState(row, WithValue(row.State, ValueRange.Clamp(ValueOf(row.State))));
        return WithFadeTo(valueClampedRow, ValueRange.Clamp(EndValueOf(valueClampedRow)));
    }

    // The byte a row ends at: where it ramps to, or the one it holds.
    public int EndValueOf(NoteChainRegion<TState> row)
    {
        return row.FadeTo ?? ValueOf(row.State);
    }

    // The byte a row running a number of ticks has reached a number of ticks in, as the driver's ramp reaches it.
    public int ValueReached(NoteChainRegion<TState> row, int lengthTicks, int elapsedTicks)
    {
        if (row.FadeTo is not int fadeTo)
        {
            return ValueOf(row.State);
        }

        return SnesFade.HeldAt(ValueOf(row.State)).FadingTo(fadeTo, lengthTicks).Advanced(elapsedTicks).Value;
    }

    // The byte on a row's straight line a number of ticks into the number it runs.
    public int ValueOnLine(NoteChainRegion<TState> row, int lengthTicks, int elapsedTicks)
    {
        return SnesFade.ValueOnLine(ValueOf(row.State), EndValueOf(row), elapsedTicks, lengthTicks);
    }

    // Rows rebased onto a note of a length trimmed at its head: the row in force at the new start becomes the first, at the byte it has reached there and
    // ramping on to its own target, and every row after it keeps its tick.
    public ImmutableArray<NoteChainRegion<TState>> TrimHead(ImmutableArray<NoteChainRegion<TState>> rows, int trimmedTicks, int noteLengthTicks)
    {
        return OffsetRows.TrimHead(rows, trimmedTicks, inForceIndex =>
        {
            var inForce = rows[inForceIndex];
            int valueReachedAtNewStart = ValueReached(inForce, OffsetRows.LengthTicksAt(rows, inForceIndex, noteLengthTicks), trimmedTicks - inForce.OffsetTicks);
            return WithState(inForce, WithValue(inForce.State, valueReachedAtNewStart));
        });
    }

    // Each of a channel's notes' stretch of the lane alone as flat rows counted from the note's start - the state in force where it starts, then each
    // step to another state inside it - or null for a note the lane ramps anywhere across.
    public List<ImmutableArray<NoteChainRegion<TState>>?> LaneStepsAcross(Song song, int channel, IReadOnlyList<Note> notes)
    {
        var dividers = LaneDividers(song, channel);
        var laneBase = LaneBase(song, channel);
        return [.. notes.Select(note => LaneStepsAcross(dividers, laneBase, note))];
    }

    // One note's stretch of a lane's own base and dividers as flat rows counted from its start, or null where the lane ramps anywhere across it.
    private static ImmutableArray<NoteChainRegion<TState>>? LaneStepsAcross(IReadOnlyList<LaneDivider<TState>> dividers, TState laneBase, Note note)
    {
        // How many of the lane's dividers fall at or before the note's start.
        int dividerIndex = SortedSearch.CountLeading(dividers, divider => divider.Tick <= note.StartTick);
        if (dividerIndex > 0 && dividers[dividerIndex - 1].Fades)
        {
            return null;
        }

        var rows = ImmutableArray.CreateBuilder<NoteChainRegion<TState>>();

        // The lane's state where the note starts.
        var state = dividerIndex == 0 ? laneBase : dividers[dividerIndex - 1].Value;
        rows.Add(new NoteChainRegion<TState>(OffsetRows.FirstOffsetTicks, state));
        for (; dividerIndex < dividers.Count && dividers[dividerIndex].Tick < note.EndTick(); dividerIndex++)
        {
            var divider = dividers[dividerIndex];
            if (divider.Fades)
            {
                return null;
            }

            if (AreSameState(divider.Value, state))
            {
                continue;
            }

            rows.Add(new NoteChainRegion<TState>(divider.Tick - note.StartTick, divider.Value));
            state = divider.Value;
        }

        return rows.ToImmutable();
    }

    // The rows the note as the song holds it plays over: its own, its instrument's, or none where it overrides nothing.
    public ImmutableArray<NoteChainRegion<TState>> PlayedChain(Song song, int channel, Note note)
    {
        return ResolvedChain(song, NoteRungs.LiveNote(song, channel, note));
    }

    // Which rung the note as the song holds it names.
    public LaneSource LiveSourceOf(Song song, int channel, Note note)
    {
        return SourceOf(NoteRungs.LiveNote(song, channel, note));
    }

    // The rows the note plays over, or null where it overrides nothing.
    public ImmutableArray<NoteChainRegion<TState>>? OverrideOf(Song song, int channel, Note note)
    {
        var playedChain = PlayedChain(song, channel, note);
        return playedChain.IsEmpty ? null : playedChain;
    }

    // Puts the note on rows of its own, none taking it back to the lane.
    public void ApplyOverride(Song song, int channel, Note note, ImmutableArray<NoteChainRegion<TState>> rows)
    {
        SetNoteChain(song, channel, note.StartTick, rows.IsEmpty ? LaneSource.None : LaneSource.Note, rows);
    }

    // Takes the note back to the lane, throwing its own rows away.
    public void ClearOverride(Song song, int channel, Note note)
    {
        ApplyOverride(song, channel, note, []);
    }

    // The state the note keys on at: the first row of whichever rows are in force, and the lane's own where none are.
    public TState LeadState(Song song, int channel, Note note)
    {
        var playedChain = PlayedChain(song, channel, note);
        return playedChain.IsEmpty ? LaneStateAt(song, channel, note.StartTick) : playedChain[OffsetRows.FirstRowIndex].State;
    }

    // Puts the note's first row at a state and leaves every row behind it alone.
    public void SetLeadState(Song song, int channel, Note note, TState state)
    {
        var rows = ChainAlreadyPlaying(song, channel, note);
        ApplyOverride(song, channel, note, rows.SetItem(OffsetRows.FirstRowIndex, WithState(rows[OffsetRows.FirstRowIndex], state)));
    }

    // Moves a note to a rung without changing what it plays: onto its own it takes the rows it was already playing, and off it its own rows go.
    public void SetSource(Song song, int channel, Note note, LaneSource source)
    {
        var ownChain = source == LaneSource.Note ? ChainAlreadyPlaying(song, channel, note) : [];
        SetNoteChain(song, channel, note.StartTick, source, ownChain);
    }

    // What a note on the instrument's rung keys on: the instrument's first row, and the lane's default where there is no instrument.
    public TState LeadState(Instrument? instrument)
    {
        return InstrumentChain(instrument)[OffsetRows.FirstRowIndex].State;
    }

    // Puts an instrument's first row at a state and leaves every row behind it alone.
    public void SetLeadState(Instrument instrument, TState state)
    {
        var rows = InstrumentRows(instrument);
        SetInstrumentRows(instrument, rows.SetItem(OffsetRows.FirstRowIndex, WithState(rows[OffsetRows.FirstRowIndex], state)));
    }

    // An instrument's rows as a note of a length plays them: every row taking over before the note ends, the last running to its end.
    public ImmutableArray<NoteChainRegion<TState>> InstrumentChainForNoteLength(Instrument? instrument, int noteLengthTicks)
    {
        return OffsetRows.FitToNoteLength(InstrumentChain(instrument), noteLengthTicks);
    }

    // An instrument's rows with nothing cut off them, or the lane's default held flat where there is no instrument.
    public ImmutableArray<NoteChainRegion<TState>> InstrumentChain(Instrument? instrument)
    {
        return instrument is null ? [new NoteChainRegion<TState>(OffsetRows.FirstOffsetTicks, DefaultState)] : InstrumentRows(instrument);
    }

    // The rows a note plays by the rung it names - its own, its instrument's, or none at all.
    public ImmutableArray<NoteChainRegion<TState>> ResolvedChain(Song song, Note note)
    {
        return SourceOf(note) switch
        {
            LaneSource.Note => OwnChainOf(note),
            LaneSource.Instrument => InstrumentChainForNoteLength(song.InstrumentOf(note), note.LengthTicks),
            _ => [],
        };
    }

    // The stretches of a channel a note's own rows are in force over, which is each overriding note's whole span.
    public IReadOnlyList<(int Start, int End)> OverriddenSpans(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        return [.. OverridingNotes(song, channel, notes).Select(overriding => (overriding.Note.StartTick, overriding.Note.EndTick()))];
    }

    // One channel's lane as the lane and its notes together say it: the lane's own base and dividers, with each overriding note's rows laid over that note's own span.
    public (TState Base, IReadOnlyList<LaneDivider<TState>> Changes) Merged(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var laneDividers = LaneDividers(song, channel);
        var laneBase = LaneBase(song, channel);
        var overridingNotes = OverridingNotes(song, channel, notes);
        if (overridingNotes.Count == 0)
        {
            return (laneBase, laneDividers);
        }

        var laneStateAt = LaneReader(song, channel);
        var laneDividersAsBytes = laneDividers.Select(divider => new LaneDivider<int>(divider.Tick, ValueOf(divider.Value), divider.Fades, ValueOf(divider.FadeTo)));
        var laneRegions = LaneRegions.FromBaseAndChanges(ValueOf(laneBase), laneDividersAsBytes, song.RollLengthTicks);
        var mergedDividers = new List<LaneDivider<TState>>();
        var mergedBase = laneBase;
        int nextLaneDividerIndex = 0;

        // The lane's own region ramping across a tick, or null where the lane is flat there.
        LaneRegion? LaneRampAt(int tick)
        {
            int regionsStartingAtOrBeforeTick = SortedSearch.CountLeading(laneRegions, region => region.Start <= tick);
            if (regionsStartingAtOrBeforeTick == 0)
            {
                return null;
            }

            var region = laneRegions[regionsStartingAtOrBeforeTick - 1];
            return tick < region.End && region.Fades ? region : null;
        }

        // Adds a divider, replacing one already on its tick.
        void AddReplacingSameTick(LaneDivider<TState> divider)
        {
            if (mergedDividers.Count > 0 && mergedDividers[^1].Tick == divider.Tick)
            {
                mergedDividers[^1] = divider;
            }
            else
            {
                mergedDividers.Add(divider);
            }
        }

        // Writes a divider a note says, a flat one at tick 0 being the base and a ramp there the first region's own fade.
        void EmitNoteDivider(int tick, TState value, bool fades, TState fadeTo)
        {
            if (tick == 0)
            {
                mergedBase = fades ? PairedState(value, fadeTo) : value;
                if (!fades)
                {
                    return;
                }
            }

            AddReplacingSameTick(new LaneDivider<TState>(tick, value, fades, fadeTo));
        }

        // Re-aims a ramp written in front of a tick at the byte the lane holds there, the note's first divider ending that ramp early.
        void ReaimRampInFront(int tick)
        {
            if (mergedDividers.Count == 0 || mergedDividers[^1].Tick >= tick || !mergedDividers[^1].Fades)
            {
                return;
            }

            mergedDividers[^1] = mergedDividers[^1] with { FadeTo = PairedState(laneStateAt(tick), mergedDividers[^1].FadeTo) };
        }

        foreach (var (note, rows) in overridingNotes)
        {
            nextLaneDividerIndex = NoteRungs.LaneDividerIndexPastNote(laneDividers, nextLaneDividerIndex, note, AddReplacingSameTick);
            ReaimRampInFront(note.StartTick);
            foreach (var row in rows)
            {
                EmitNoteDivider(note.StartTick + row.OffsetTicks, row.State, row.Fades, WithValue(row.State, EndValueOf(row)));
            }

            var laneStateAtNoteEnd = laneStateAt(note.EndTick());
            if (LaneRampAt(note.EndTick()) is { } resumedRamp)
            {
                EmitNoteDivider(note.EndTick(), laneStateAtNoteEnd, true, WithValue(laneStateAtNoteEnd, resumedRamp.FadeTo));
            }
            else if (rows[^1].Fades || !AreSameState(rows[^1].State, laneStateAtNoteEnd))
            {
                EmitNoteDivider(note.EndTick(), laneStateAtNoteEnd, false, laneStateAtNoteEnd);
            }
        }

        while (nextLaneDividerIndex < laneDividers.Count)
        {
            AddReplacingSameTick(laneDividers[nextLaneDividerIndex++]);
        }

        return (mergedBase, mergedDividers);
    }

    // Whether two states are equal.
    private static bool AreSameState(TState first, TState second)
    {
        return EqualityComparer<TState>.Default.Equals(first, second);
    }

    // The byte of one state with whatever else another carries - the pan's surround pair, and nothing at all on the volume.
    private TState PairedState(TState byteSource, TState restSource)
    {
        return WithValue(restSource, ValueOf(byteSource));
    }

    // The rows a note arriving on its own rung takes: whatever it is already playing, and the lane's state held flat where it overrides nothing.
    private ImmutableArray<NoteChainRegion<TState>> ChainAlreadyPlaying(Song song, int channel, Note note)
    {
        var resolvedChain = ResolvedChain(song, NoteRungs.LiveNote(song, channel, note));
        return resolvedChain.IsEmpty ? [new NoteChainRegion<TState>(OffsetRows.FirstOffsetTicks, LaneStateAt(song, channel, note.StartTick))] : resolvedChain;
    }

    // The notes of a channel that override the lane, in start order and never overlapping.
    private List<(Note Note, ImmutableArray<NoteChainRegion<TState>> Value)> OverridingNotes(Song song, int channel, IReadOnlyList<Note>? notes)
    {
        return NoteRungs.OverridingNotes(song, channel, notes, note => ResolvedChain(song, note), chain => !chain.IsEmpty);
    }
}
