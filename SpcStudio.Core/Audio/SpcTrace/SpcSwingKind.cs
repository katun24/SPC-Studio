using System.Collections.Immutable;

namespace SpcStudio.Audio;

// One of the driver's two sweeps as a block is read for it: the scale its register swings in, the path of that register the block holds, the stretches the
// block keeps of it, and the block given one inferred stretch of it.
public sealed record SpcSwingKind(
    SpcSwingScale Scale,
    Func<SpcTraceBlock, IReadOnlyList<SpcTracePoint>> PathOf,
    Func<SpcTraceBlock, ImmutableArray<SpcTraceSwing>> SwingsOf,
    Func<SpcTraceBlock, SpcTraceSwing, SpcTraceBlock> WithInferred);
