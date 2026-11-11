namespace SpcStudio.Model;

// One note's own channel volume, MML v: the channel volume lane as a lane a note can stand over, and the merge in the lane's own spelling.
public static class NoteVolume
{
    // The channel volume as a lane a note can stand over, a state being the byte alone.
    public static readonly NoteFadeLane<int> Lane = new()
    {
        LaneBase = (song, channel) => song.BaseChannelVolume(channel),
        LaneDividers = (song, channel) => song.ChannelVolumeChanges(channel),
        MergedStateAt = (song, channel, tick) => ChangeMap.ForChannelVolume(song, channel).InForceAt(tick),
        LaneReader = (song, channel) =>
        {
            var laneMap = ChangeMap.ForChannelVolumeOwn(song, channel);
            return tick => laneMap.InForceAt(tick);
        },
        ValueOf = volume => volume,
        WithValue = (_, volume) => volume,
        ValueRange = SnesVolume.ChannelVolumeRange,
        SourceOf = note => note.VolumeSource,
        OwnChainOf = note => note.Volumes,
        SetNoteChain = (song, channel, startTick, source, volumes) => song.SetNoteVolume(channel, startTick, source, volumes),
        DefaultState = SnesVolume.DefaultChannelVolume,
        InstrumentRows = instrument => instrument.VolumeRows,
        SetInstrumentRows = (instrument, rows) => instrument.VolumeRows = rows,
    };
}
