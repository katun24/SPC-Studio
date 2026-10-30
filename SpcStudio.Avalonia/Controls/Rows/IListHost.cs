using System.Collections.Immutable;

namespace SpcStudio.Controls;

// What a list of rows standing for a chain entry for entry is editing on one subject - a note's chain or an instrument's: the chain itself, and where a write goes.
public interface IListHost<T> : IRowHost where T : struct
{
    // The chain the rows show.
    ImmutableArray<T> Chain { get; }

    // Writes a chain without recording it, answering whether there was a subject to write it onto.
    bool Write(ImmutableArray<T> chain);
}
