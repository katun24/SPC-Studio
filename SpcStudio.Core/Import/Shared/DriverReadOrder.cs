namespace SpcStudio.Import;

// The order the driver reads commands in across its channels.
internal static class DriverReadOrder
{
    // The key that sorts commands into that order: by tick, then the highest channel first, then as the channel's stream has them.
    public static (int Tick, int NegatedChannel, int Position) Key(int tick, int channel, int position)
    {
        return (tick, -channel, position);
    }
}
