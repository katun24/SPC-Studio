namespace SpcStudio.Audio;

// A sample's source converted to the rate that fits a loop to whole blocks, before it is encoded: its points up to the loop's end, the point the loop
// starts at, on a block, and the rate in hertz.
public sealed record FittedLoop(short[] Points, int LoopStartPoint, int RateHz);
