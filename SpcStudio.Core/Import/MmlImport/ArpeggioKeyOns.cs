namespace SpcStudio.Import;

// Where a channel's arpeggio keys the voice on in place of its notes: the tick of every step, in order, and the ticks of the notes it silences.
internal sealed record ArpeggioKeyOns(IReadOnlyList<int> StepTicks, IReadOnlySet<int> SilencedNoteTicks)
{
    public static readonly ArpeggioKeyOns None = new([], new HashSet<int>());

    // Whether the arpeggio keys on nowhere and silences nothing.
    public bool IsNone
    {
        get
        {
            return StepTicks.Count == 0 && SilencedNoteTicks.Count == 0;
        }
    }

    // The first tick two channels' key-ons part at - a step one has and the other does not, or a note one silences and the other does not - or null where they are the same.
    public int? FirstTickApart(ArpeggioKeyOns other)
    {
        int sharedStepCount = StepTicks.Zip(other.StepTicks).TakeWhile(pair => pair.First == pair.Second).Count();
        var apartTicks = SilencedNoteTicks.Except(other.SilencedNoteTicks).Concat(other.SilencedNoteTicks.Except(SilencedNoteTicks)).ToList();
        if (sharedStepCount < StepTicks.Count)
        {
            apartTicks.Add(StepTicks[sharedStepCount]);
        }

        if (sharedStepCount < other.StepTicks.Count)
        {
            apartTicks.Add(other.StepTicks[sharedStepCount]);
        }

        return apartTicks.Count == 0 ? null : apartTicks.Min();
    }
}
