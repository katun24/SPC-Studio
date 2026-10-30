using System.Collections.Immutable;

namespace SpcStudio.Import;

// One change an imported lane is written as: its tick, the value of each side in force or set there, its key, and where it fades, the values it lands on at the change after it.
public readonly record struct WrittenLaneChange<TKey>(int Tick, ImmutableArray<int> Values, TKey Key, bool Fades, ImmutableArray<int> FadeTo);
