namespace SpcStudio.Import;

// What the driver does to a channel where a section ends and every channel jumps to its next stretch, for the text import and the ROM reader alike.
internal static class SectionEnds
{
    // What the read-ahead finds at a section's end: a tie where a note sounds across it, nothing keying that note off before the jump, and a key-off
    // where none does.
    public static ReadAheadStop ReadAheadStopAt(MmlNote? noteSoundingAcrossEnd)
    {
        return noteSoundingAcrossEnd is null ? ReadAheadStop.KeyOff : ReadAheadStop.Tie;
    }

    // A note sounding across a section's end, cut at the jump.
    public static MmlNote CutAt(MmlNote noteSoundingAcrossEnd, int sectionEndTick)
    {
        return noteSoundingAcrossEnd.ShortenedTo(sectionEndTick - noteSoundingAcrossEnd.Tick);
    }
}
