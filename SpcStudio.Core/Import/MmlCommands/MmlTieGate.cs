namespace SpcStudio.Import;

// A tie written under a q of its own: how many ticks into its note it starts, and the q.
public sealed record MmlTieGate(int OffsetTicks, int Quantization);
