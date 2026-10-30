using SpcStudio.Model;

namespace SpcStudio.Export;

// How many #instruments entries and #samples slots AddmusicK's driver can play in one song, and how many a song's export takes.
public static class DriverTableLimits
{
    public const int LastCustomInstrument = 127;
    public const int MaxSampleSlots = 128;
    public const int MaxInstrumentEntries = LastCustomInstrument - MmlExporter.FirstCustomInstrument + 1;

    // How many #instruments entries the song's export writes.
    public static int InstrumentEntryCount(Song song)
    {
        return MmlExporter.UsedInstruments(song).Count;
    }

    // How many #samples slots the song's export takes: its sample group's and then its own files'.
    public static int SampleSlotCount(Song song, int groupSampleCount)
    {
        return groupSampleCount + SampleExport.UsedSamples(song).Count;
    }
}
