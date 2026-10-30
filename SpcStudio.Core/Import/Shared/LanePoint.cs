using System.Collections.Immutable;

namespace SpcStudio.Import;

// One value an imported lane was played at: its tick, the value of each of its sides, and a key that a fade may not run across.
public readonly record struct LanePoint<TKey>(int Tick, ImmutableArray<int> Values, TKey Key);
