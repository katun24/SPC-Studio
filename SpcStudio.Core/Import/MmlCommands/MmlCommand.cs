using SpcStudio.Model;

namespace SpcStudio.Import;

// One of the four single-byte commands written between notes - t, w, v or y - with the ticks of its fade form, and y's surround pair.
public sealed record MmlCommand(int Tick, char Letter, int Value, int Line, int FadeTicks = SnesFade.NoFade, SnesSurround Surround = default)
    : MmlEvent(Tick, Line);
