namespace SpcStudio.Model;

// One channel's vibrato as a function of position, derived from its notes: the state in force at a tick, and the ticks a p or a $DF goes at.
public sealed class VibratoMap : SweepMap<VibratoState>
{
    private VibratoMap(List<(int Tick, VibratoState State)> commands) : base(commands)
    {
    }

    // The vibrato map of one channel of a song, or of the notes given in place of the channel's own.
    public static VibratoMap For(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        return new(CommandsFor(notes ?? song.Notes(channel), note => note.Vibratos));
    }
}
