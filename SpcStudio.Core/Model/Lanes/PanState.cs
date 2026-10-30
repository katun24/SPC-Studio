namespace SpcStudio.Model;

// The pan a channel is at over one stretch: the table index and the surround pair, the two driver bytes one $DB writes.
public readonly record struct PanState(int Value, SnesSurround Surround)
{
    // The driver's own pan, centered and with neither side inverted.
    public static readonly PanState Default = new(SnesVolume.DefaultPanIndex, SnesSurround.None);
}
