using SpcStudio.Model;

namespace SpcStudio.Controls;

// The snap arithmetic a block is placed, moved and resized by along the time axis - a note on the roll and a unit on a lane alike.
public static class SnapSpan
{
    // A block stretched from its resize start to the cursor's tick, ending at the end of the cursor's snap step and dragging the start left with a
    // cursor taken before it, no shorter than one snap step or the block's own length.
    public static (int Start, int Length) ResizedToTick(Song song, int tick, ref int resizeStart, int ownLength)
    {
        int shortest = Math.Min(song.SnapTicks, ownLength);

        if (tick < resizeStart)
        {
            resizeStart = tick < 0 ? 0 : song.SnapDown(tick);
        }

        int end = song.SnapDown(tick) + song.SnapTicks;
        return (resizeStart, Math.Max(end - resizeStart, shortest));
    }

    // A length changed by a drag's delta, held to one snap step or to its own length where that is shorter.
    public static int Lengthened(Song song, int ownLength, int delta)
    {
        return Math.Max(ownLength + delta, Math.Min(song.SnapTicks, ownLength));
    }

    // How far a selection laid down again under the cursor moves: its anchor to the cursor's snap step, and no member before the start of the song.
    public static int OffsetToCursor(Song song, int tick, int anchorStart, int earliest)
    {
        return Math.Max(song.SnapDown(tick) - anchorStart, -earliest);
    }
}
