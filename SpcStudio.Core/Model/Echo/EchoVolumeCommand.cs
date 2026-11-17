namespace SpcStudio.Model;

// One command the Echo Volume lane writes at a tick: whether an $EF states both sides' volumes there, and a piece of the region's fade, with a target for each side, where it fades.
public readonly record struct EchoVolumeCommand(int Tick, bool Sets, int Left, int Right, int FadeTicks = SnesFade.NoFade, int FadeToLeft = 0, int FadeToRight = 0)
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
