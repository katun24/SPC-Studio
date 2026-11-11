namespace SpcStudio.Model;

// The values a setting can hold, from its lowest to its highest, both included.
public readonly record struct ValueRange(int Min, int Max)
{
    // Whether a value is inside the range.
    public bool Contains(int value)
    {
        return value >= Min && value <= Max;
    }

    // A value brought inside the range, to its nearer end where it is outside.
    public int Clamp(int value)
    {
        return Math.Clamp(value, Min, Max);
    }

    // The values a thing can stand at whose reach runs from a lowest to a highest offset off it, so that all of its reach stays inside the range.
    public ValueRange HoldingReach(int lowestOffset, int highestOffset)
    {
        return new(Min - lowestOffset, Max - highestOffset);
    }
}
