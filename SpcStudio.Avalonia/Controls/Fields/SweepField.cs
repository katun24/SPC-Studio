using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// One field of a sweep's row: its label, how its value is read and written, and the range it may hold given the chain it is in and the room that chain has.
public sealed record SweepField<T>(string Label, Func<T, int> ValueOf, Func<T, int, T> WithValue, Func<ImmutableArray<T>, int, int, ValueRange> RangeIn)
    where T : struct, INoteSweep<T>
{
    // Whether the field holds a tick count rather than a byte.
    public bool IsTickCount { get; init; }

    // Whether the chain leaves the field of the stretch at an index nothing to hold, which disables it; null for a field that is always in use.
    public Func<ImmutableArray<T>, int, bool>? IsLocked { get; init; }
}
