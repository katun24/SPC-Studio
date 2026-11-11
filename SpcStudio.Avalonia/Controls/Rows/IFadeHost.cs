using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// What a list of volume or pan rows is editing on one subject - a note's rows or an instrument's: its instrument's rows, and the state a right-click puts the first row back at.
public interface IFadeHost<TState> : IListHost<NoteChainRegion<TState>> where TState : struct
{
    // The rows the subject's instrument gives it, which are what '+' gives a subject holding none of its own.
    ImmutableArray<NoteChainRegion<TState>> InstrumentRows { get; }

    // The state the first row goes back to, which is the one the row above the section goes back to.
    TState BaseLeadState { get; }
}
