namespace SpcStudio.Model;

// One channel's pan divider at a tick: from there on that channel plays at the value, an index into the driver's pan table, ramping to the fade
// target across the whole region where it fades, with the sides its surround pair inverts.
public readonly record struct PanChange(int Tick, int Value, bool Fades = false, int FadeTo = 0, SnesSurround Surround = default) : ILaneChange;
