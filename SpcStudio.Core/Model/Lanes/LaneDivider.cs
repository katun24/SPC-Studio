namespace SpcStudio.Model;

// One divider on a lane drawn as regions: its tick, the state it sets, and whether the region it opens ramps across the whole of itself and to what.
public readonly record struct LaneDivider<TState>(int Tick, TState Value, bool Fades, TState FadeTo) : ILaneChange where TState : struct;
