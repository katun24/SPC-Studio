using System.Collections.Immutable;

namespace SpcStudio.Model;

// How many beats a bar holds and which note value one beat is, which is what the roll's bar lines and the ruler's bar numbers follow.
public readonly record struct TimeSignature(int BeatsPerBar, int BeatUnit)
{
    public const int MinBeatsPerBar = 1;
    public const int MaxBeatsPerBar = 16;
    private const int DefaultBeatsPerBar = 4;
    private const int DefaultBeatUnit = 4;

    public static readonly TimeSignature Default = new(DefaultBeatsPerBar, DefaultBeatUnit);
    public static readonly ValueRange BeatsPerBarRange = new(MinBeatsPerBar, MaxBeatsPerBar);

    // The note values a beat can be, longest first, as the bottom number writes them.
    public static readonly ImmutableArray<int> BeatUnits = [2, 4, 8, 16];

    // The longest bar any time signature makes.
    public static int LongestBarTicks
    {
        get
        {
            return new TimeSignature(MaxBeatsPerBar, BeatUnits[0]).TicksPerBar;
        }
    }

    // How many ticks one bar lasts.
    public int TicksPerBar
    {
        get
        {
            return BeatsPerBar * SnesTiming.TicksPerWholeNote / BeatUnit;
        }
    }

    // Whether both numbers are ones a time signature can hold.
    public bool IsValid
    {
        get
        {
            return BeatsPerBarRange.Contains(BeatsPerBar) && BeatUnits.Contains(BeatUnit);
        }
    }

    // The beat unit a number of places along the list from another, held to the list's ends.
    public static int BeatUnitSteppedBy(int beatUnit, int places)
    {
        int steppedBeatUnitIndex = Math.Clamp(BeatUnits.IndexOf(beatUnit) + places, 0, BeatUnits.Length - 1);
        return BeatUnits[steppedBeatUnitIndex];
    }
}
