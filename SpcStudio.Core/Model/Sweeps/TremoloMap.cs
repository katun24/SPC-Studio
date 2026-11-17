namespace SpcStudio.Model;

// One channel's tremolo as a function of position, derived from its notes: the state in force at a tick, and the ticks an $E5 or an $FD goes at.
public sealed class TremoloMap : SweepMap<TremoloState>
{
    private TremoloMap(List<(int Tick, TremoloState State)> commands) : base(commands)
    {
    }

    // The tremolo map of one channel of a song, or of the notes given in place of the channel's own.
    public static TremoloMap For(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        return new(CommandsFor(notes ?? song.Notes(channel), note => note.Tremolos));
    }
}
