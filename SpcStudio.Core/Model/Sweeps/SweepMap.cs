using System.Collections.Immutable;

namespace SpcStudio.Model;

// One channel's sweep as a function of position, derived from its notes by a delta pass: the state in force at a tick, and the ticks a command goes at.
public abstract class SweepMap<TState> where TState : struct, ISweepState<TState>
{
    private readonly List<(int Tick, TState State)> _commands;

    protected SweepMap(List<(int Tick, TState State)> commands)
    {
        _commands = commands;
    }

    // The whole state in force at a tick: what the last command at or before it put there, or no sweep where none has run yet.
    public TState StateAt(double tick)
    {
        int commandCountAtOrBeforeTick = SortedSearch.CountLeading(_commands, command => command.Tick <= tick);
        return commandCountAtOrBeforeTick == 0 ? TState.Off : _commands[commandCountAtOrBeforeTick - 1].State;
    }

    // The ticks past tick 0 at which a command has to be written, in order and distinct.
    public IEnumerable<int> StateChangeTicks()
    {
        foreach (var (tick, _) in _commands)
        {
            if (tick > 0)
            {
                yield return tick;
            }
        }
    }

    // The commands a run of notes comes to, in tick order and never two at one tick: a state where a stretch differs from what the one before it left,
    // and no sweep where a stretch ends before the next one starts or its note ends.
    protected static List<(int Tick, TState State)> CommandsFor<TStretch>(IEnumerable<Note> notes, Func<Note, ImmutableArray<TStretch>> stretchesOf)
        where TStretch : struct, ISweepStretch<TStretch, TState>
    {
        var commands = new List<(int Tick, TState State)>();
        var stateInForce = TState.Off;
        var activeSpans = new List<(int StartTick, int EndTick, TState State)>();
        foreach (var note in notes)
        {
            activeSpans.Clear();
            int previousEndTick = 0;
            foreach (var stretch in stretchesOf(note))
            {
                int startTick = previousEndTick + stretch.DelayTicks;
                previousEndTick = startTick + stretch.LengthTicks;
                if (stretch.IsActive)
                {
                    activeSpans.Add((startTick, previousEndTick, stretch.State));
                }
            }

            if (activeSpans.Count == 0)
            {
                AddCommand(note.StartTick, TState.Off);
                continue;
            }

            // The first active stretch's own start, which every command on the note carries as its delay byte.
            int delayByte = activeSpans[0].StartTick;
            for (int index = 0; index < activeSpans.Count; index++)
            {
                var (startTick, endTick, state) = activeSpans[index];
                AddCommand(index == 0 ? note.StartTick : note.StartTick + startTick, state.WithDelay(delayByte));
                bool doesNextStretchStartWhereThisEnds = index + 1 < activeSpans.Count && activeSpans[index + 1].StartTick == endTick;
                if (!doesNextStretchStartWhereThisEnds && endTick < note.LengthTicks)
                {
                    AddCommand(note.StartTick + endTick, TState.Off);
                }
            }
        }

        return commands;

        // Writes a command at a tick where the state as heard differs from the one in force.
        void AddCommand(int tick, TState state)
        {
            var audibleState = state.Audible();
            if (audibleState.Equals(stateInForce))
            {
                return;
            }

            commands.Add((tick, audibleState));
            stateInForce = audibleState;
        }
    }
}
