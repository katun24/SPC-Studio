using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the vibrato's and the tremolo's commands.
public static partial class MmlExporter
{
    // The vibrato's command for a state: a p, with an $EA behind it where it fades, or $DF where the state does not sweep.
    private static string VibratoCommandFor(VibratoState state)
    {
        if (!state.IsActive)
        {
            return VibOffName.Write();
        }

        string sweep = $"p{state.Delay},{state.Rate},{state.Extent}";
        return state.HasFade ? $"{sweep} {VibFadeName.Write(state.FadeTicks)}" : sweep;
    }

    // The tremolo's command for a state: an $E5, or $FD where the state does not duck.
    private static string TremoloCommandFor(TremoloState state)
    {
        return state.IsActive ? TremName.Write(state.Delay, state.Rate, state.Amount) : TremOffName.Write();
    }

    // What a channel's header says about one of its sweeps: the command where the channel starts under one, and nothing where it does not.
    private static string SweepHeader<TState>(SweepMap<TState> sweep, Func<TState, string> commandFor) where TState : struct, ISweepState<TState>
    {
        var state = sweep.StateAt(0);
        return state.IsActive ? $"{commandFor(state)} " : "";
    }

    // The commands one of a channel's sweeps writes after its header, one wherever its state changes.
    private static IEnumerable<TimedCommand> SweepCommands<TState>(SweepMap<TState> sweep, int sortOrder, Func<TState, string> commandFor) where TState : struct, ISweepState<TState>
    {
        return sweep.StateChangeTicks().Select(tick => new TimedCommand(tick, sortOrder, commandFor(sweep.StateAt(tick))));
    }
}
