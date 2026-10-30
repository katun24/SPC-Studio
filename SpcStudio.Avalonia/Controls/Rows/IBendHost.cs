using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// What a list of pitch bend rows is editing on one subject - a note's chain or an instrument's: the chain, how far its offsets may carry it, and where a write goes.
public interface IBendHost : IListHost<PitchBend>
{
    // The offsets the bend at an index of a chain can hold.
    ValueRange OffsetRange(ImmutableArray<PitchBend> chain, int index);
}
