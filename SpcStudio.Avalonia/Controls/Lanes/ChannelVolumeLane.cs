using SpcStudio.Model;

namespace SpcStudio.Controls;

// The lane holding one channel's volume, MML v, with its $E8 fades, drawn as regions with the notes' own volumes over them.
public sealed class ChannelVolumeLane : NoteFadeLaneControl
{
    // Draws the lane in the volume lanes' two colors.
    public ChannelVolumeLane() : base(LaneCellBrushes.Volume)
    {
    }

    // The channel's own volume dividers.
    protected override IEnumerable<LaneValueChange> Changes
    {
        get
        {
            return Song is null ? [] : Song.ChannelVolumeChanges(Channel).Select(LaneValueChange.Of);
        }
    }

    // The channel's base volume.
    public override int BaseValue
    {
        get
        {
            return Song?.BaseChannelVolume(Channel) ?? DefaultValue;
        }
    }

    public override int DefaultValue
    {
        get
        {
            return SnesVolume.DefaultChannelVolume;
        }
    }

    public override ValueRange ValueRange
    {
        get
        {
            return SnesVolume.ChannelVolumeRange;
        }
    }

    // The channel's own volume regions, its notes' own volumes left out.
    protected override IEnumerable<LaneRegion> OwnRegions(Song song)
    {
        return LaneRegions.ChannelVolumeOwn(song, Channel);
    }

    // The regions the channel plays, its dividers merged with the given notes' own volumes.
    protected override IReadOnlyList<LaneRegion> MergedRegions(IReadOnlyList<Note>? notes)
    {
        return Song is null ? [] : LaneRegions.ChannelVolume(Song, Channel, notes);
    }

    // The stretches the given notes' own volumes decide.
    protected override IReadOnlyList<(int Start, int End)> OverrideSpans(IReadOnlyList<Note>? notes)
    {
        return Song is null ? [] : NoteVolume.Lane.OverriddenSpans(Song, Channel, notes);
    }

    // The volume a change at a tick sets, or null where none sits there.
    public override int? ChangeValueAt(int tick)
    {
        return Song?.ChannelVolumeChangeAt(Channel, tick)?.Value;
    }
}
