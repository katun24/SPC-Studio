namespace SpcStudio.Import;

// One channel's two legato bits as the driver holds them: the bit a $F4 $01 toggles, and whether the voice has keyed on since - both set, every cut is abandoned and a key-on is left out.
internal readonly record struct LegatoBits(bool IsBitSet, bool IsKeyedOn)
{
    // Whether the driver abandons what asked it: a cut is not made, and a key-on is left out of KON.
    public bool DoesAbandonCuts
    {
        get
        {
            return IsBitSet && IsKeyedOn;
        }
    }

    // What a note keying on under that bit leaves.
    public static LegatoBits KeyedOnUnder(bool isBitSet)
    {
        return new LegatoBits(isBitSet, true);
    }

    // The ticks a channel's $F4 $01s are written at, in order.
    public static List<int> ToggleTicks(IEnumerable<MmlEvent> events)
    {
        return [.. events.OfType<MmlLegato>().Select(toggle => toggle.Tick).Order()];
    }

    // Whether the bit is set for the byte ending at a tick: an odd number of $F4 $01s written before it.
    public static bool IsBitSetBefore(IEnumerable<MmlEvent> events, int tick)
    {
        return events.OfType<MmlLegato>().Count(toggle => toggle.Tick < tick) % 2 == 1;
    }

    // The bits as the cuts of the note at an index find them: keyed on under the bit it started under, then toggled by every $F4 $01 written inside it.
    public static LegatoBits AtCutsOf(IReadOnlyList<MmlEvent> events, int noteIndex, int noteEndTick, bool isBitSetAtKeyOn)
    {
        int togglesInside = 0;
        for (int eventIndex = noteIndex + 1; eventIndex < events.Count && events[eventIndex].Tick < noteEndTick; eventIndex++)
        {
            if (events[eventIndex] is MmlLegato)
            {
                togglesInside++;
            }
        }

        return KeyedOnUnder(isBitSetAtKeyOn).Toggle(togglesInside);
    }

    // A channel's notes walked against its toggles: the bit each note keys on under, and whether that note is bound - left out of KON, the note in front handing over to it.
    public static (bool[] IsBitSetUnder, bool[] IsBound) WalkNotes(IReadOnlyList<(int StartTick, int EndTick)> notes, IEnumerable<int> toggleTicks)
    {
        var orderedToggleTicks = toggleTicks.Order().ToList();
        var isBitSetUnder = new bool[notes.Count];
        var isBound = new bool[notes.Count];
        var bits = default(LegatoBits);
        int nextToggleIndex = 0;
        for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            for (; nextToggleIndex < orderedToggleTicks.Count && orderedToggleTicks[nextToggleIndex] <= notes[noteIndex].StartTick; nextToggleIndex++)
            {
                bits = bits.Toggle();
            }

            if (noteIndex > 0 && notes[noteIndex].StartTick > notes[noteIndex - 1].EndTick)
            {
                bits = bits.FetchRest();
            }

            isBitSetUnder[noteIndex] = bits.IsBitSet;
            isBound[noteIndex] = bits.DoesAbandonCuts;
            bits = bits.KeyOn();
        }

        return (isBitSetUnder, isBound);
    }

    // A $F4 $01, which flips the bit and clears the key-on.
    public LegatoBits Toggle()
    {
        return new LegatoBits(!IsBitSet, false);
    }

    // That many $F4 $01s.
    public LegatoBits Toggle(int count)
    {
        var bits = this;
        for (int toggleIndex = 0; toggleIndex < count; toggleIndex++)
        {
            bits = bits.Toggle();
        }

        return bits;
    }

    // The key-on pass, for a voice with a new note.
    public LegatoBits KeyOn()
    {
        return this with { IsKeyedOn = true };
    }

    // A rest byte's fetch, whose key-off clears the key-on.
    public LegatoBits FetchRest()
    {
        return this with { IsKeyedOn = false };
    }
}
