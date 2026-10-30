namespace SpcStudio.Controls;

// One stretch of a lane as it is drawn: the ticks it runs between, the value it opens at, and whether and to what it ramps.
public readonly record struct DrawnRegion(int StartTick, int EndTick, int Value, bool DoesFade, int FadeTo);
