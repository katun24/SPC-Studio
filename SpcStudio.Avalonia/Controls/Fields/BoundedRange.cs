namespace SpcStudio.Controls;

// A range of values typed into two fields, the lowest value's and the highest value's, both ends included.
public sealed class BoundedRange
{
    private readonly BoundedField _lowest;
    private readonly BoundedField _highest;

    // Ties a range to its two fields, reporting a change in either.
    public BoundedRange(BoundedField lowest, BoundedField highest)
    {
        _lowest = lowest;
        _highest = highest;
        lowest.ValueChanged += () => ValueChanged?.Invoke();
        highest.ValueChanged += () => ValueChanged?.Invoke();
    }

    // Raised as either end changes.
    public event Action? ValueChanged;

    // Whether a value lies in the range, both ends included.
    public bool Contains(int value)
    {
        return value >= _lowest.Value && value <= _highest.Value;
    }
}
