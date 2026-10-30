namespace SpcStudio.Model;

// One note's own pan: the pan lane as a lane a note can stand over, and the merge in the lane's own spelling.
public static class NotePan
{
    // The pan as a lane a note can stand over, a state carrying the surround pair beside the byte.
    public static readonly NoteFadeLane<PanState> Lane = new()
    {
        LaneBase = (song, channel) => new PanState(song.BasePan(channel), song.BaseSurround(channel)),
        LaneDividers = (song, channel) => [.. song.PanChanges(channel).Select(LaneDividerOf)],
        MergedStateAt = (song, channel, tick) => new PanState(
            ChangeMap.ForChannelPan(song, channel).InForceAt(tick),
            SnesSurround.FromPacked(ChangeMap.ForChannelSurround(song, channel).InForceAt(tick))),
        LaneReader = (song, channel) =>
        {
            var panMap = ChangeMap.ForChannelPanOwn(song, channel);
            var surroundMap = ChangeMap.ForChannelSurroundOwn(song, channel);
            return tick => new PanState(panMap.InForceAt(tick), SnesSurround.FromPacked(surroundMap.InForceAt(tick)));
        },
        ValueOf = state => state.Value,
        WithValue = (state, value) => state with { Value = value },
        ValueRange = SnesVolume.PanIndexRange,
        SourceOf = note => note.PanSource,
        OwnChainOf = note => note.Pans,
        SetNoteChain = (song, channel, startTick, source, pans) => song.SetNotePan(channel, startTick, source, pans),
        DefaultState = PanState.Default,
        InstrumentRows = instrument => instrument.PanRows,
        SetInstrumentRows = (instrument, rows) => instrument.PanRows = rows,
    };

    // One channel's pan as the lane and its notes together say it, in the lane's own spelling, each divider carrying its target's surround pair.
    public static (PanState Base, IReadOnlyList<PanChange> Changes) Merged(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var (mergedBase, mergedDividers) = Lane.Merged(song, channel, notes);
        return (mergedBase, [.. mergedDividers.Select(divider => new PanChange(divider.Tick, divider.Value.Value, divider.Fades, divider.FadeTo.Value, divider.FadeTo.Surround))]);
    }

    // A lane divider in the terms the merge works in, its surround pair on both of its ends.
    private static LaneDivider<PanState> LaneDividerOf(PanChange change)
    {
        return new(change.Tick, new PanState(change.Value, change.Surround), change.Fades, new PanState(change.FadeTo, change.Surround));
    }
}
