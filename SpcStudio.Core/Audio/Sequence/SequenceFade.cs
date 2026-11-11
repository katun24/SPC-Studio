namespace SpcStudio.Audio;

// The fade a volume, pan, echo volume or tempo event arms beside the value it sets: its length in ticks, where it lands, and what its ticks are worth in samples from the fade's own tick.
public sealed record SequenceFade(int LengthTicks, int Target, long LengthSamples);
