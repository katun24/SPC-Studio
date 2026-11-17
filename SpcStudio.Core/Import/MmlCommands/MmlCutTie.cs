namespace SpcStudio.Import;

// One tie read with a key-off in front of it: where it starts, how long its tie bytes run, whether a rest byte was fetched last in front of it, its line, and the q its cuts are timed by.
public readonly record struct MmlCutTie(int Tick, int LengthTicks, bool IsAfterRest, int Line, int? Quantization)
{
    // The tick the tie's last byte ends on.
    public int EndTick
    {
        get
        {
            return Tick + LengthTicks;
        }
    }
}
