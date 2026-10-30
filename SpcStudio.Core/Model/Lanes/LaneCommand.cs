namespace SpcStudio.Model;

// One command a region lane writes at a tick: the value a set writes there, or null where none is written, and a piece of the region's fade where it
// fades, a fade too long for the duration byte arriving as several of these.
public readonly record struct LaneCommand(int Tick, int? SetValue, int FadeTicks = SnesFade.NoFade, int FadeTo = 0)
{
    // Whether the command carries a piece of a fade.
    public bool Fades
    {
        get
        {
            return SnesFade.DoesFade(FadeTicks);
        }
    }
}
