using System.Collections.Immutable;

namespace SpcStudio.Model;

// A note's rows on one of the two fading lanes, as the note panel's Volume and Pan sections read and write them.
public interface INoteChain<TState> : INoteOverride<ImmutableArray<NoteChainRegion<TState>>> where TState : struct
{
    // The state in force on a channel at a tick, overrides included.
    public TState StateAt(Song song, int channel, int tick);

    // The rows the note plays over, whichever rung it names.
    public ImmutableArray<NoteChainRegion<TState>> PlayedChain(Song song, int channel, Note note);

    // The state the note keys on at.
    public TState LeadState(Song song, int channel, Note note);

    // Puts the note's first row at a state, leaving every row behind it alone.
    public void SetLeadState(Song song, int channel, Note note, TState state);
}
