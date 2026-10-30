namespace SpcStudio.Model;

// One channel's $F4 $01 toggles as derived from its notes: whether its header writes one, the ticks of the rest, the bit they leave in $0161, and the
// arms and clears of the key-on block that toggles it twice for the notes written whole under it.
public sealed record LegatoToggleList(bool DoesHeaderToggle, IReadOnlyList<int> ToggleTicks, HoldCommandList KeyOnArms)
{
    // Whether the channel's bit is set at a tick - what the notes require there, whatever the loop marker does to the toggles that say it.
    public bool IsBitSetAt(int tick)
    {
        bool hasOddToggleCountUpToTick = SortedSearch.CountLeading(ToggleTicks, toggleTick => toggleTick <= tick) % 2 == 1;
        return DoesHeaderToggle != hasOddToggleCountUpToTick;
    }
}
