namespace SpcStudio.Model;

// One stretch of a note played under p: where it starts, counted from where the one in front ended, how long it lasts, the rate and the extent $DE puts
// there, and how long $EA takes to widen it from nothing.
public readonly record struct NoteVibrato(int DelayTicks, int LengthTicks, int Rate, int Extent, int FadeTicks = SnesVibrato.MinFadeTicks)
    : ISweepStretch<NoteVibrato, VibratoState>
{
    public bool IsEndless { get; init; }

    // The whole state this puts in the driver's three bytes while it runs.
    public VibratoState State
    {
        get
        {
            return new(SnesSweep.DefaultDelay, Rate, Extent, FadeTicks);
        }
    }

    // Whether it sweeps anything at all.
    public bool IsActive
    {
        get
        {
            return State.IsActive;
        }
    }

    // The value one of the five holds.
    public int this[VibratoSetting setting]
    {
        get
        {
            return setting switch
            {
                VibratoSetting.Delay => DelayTicks,
                VibratoSetting.Length => LengthTicks,
                VibratoSetting.Rate => Rate,
                VibratoSetting.Extent => Extent,
                _ => FadeTicks,
            };
        }
    }

    // This stretch with its delay and length replaced and its own values untouched.
    public NoteVibrato WithSpan(int delayTicks, int lengthTicks)
    {
        return this with { DelayTicks = delayTicks, LengthTicks = lengthTicks };
    }

    // This stretch running on to the end of every note playing it, or lasting its own length, with its other values untouched.
    public NoteVibrato WithEndless(bool isEndless)
    {
        return this with { IsEndless = isEndless };
    }

    // This stretch with one of the five replaced and the other four as they were.
    public NoteVibrato With(VibratoSetting setting, int value)
    {
        return setting switch
        {
            VibratoSetting.Delay => this with { DelayTicks = value },
            VibratoSetting.Length => this with { LengthTicks = value },
            VibratoSetting.Rate => this with { Rate = value },
            VibratoSetting.Extent => this with { Extent = value },
            _ => this with { FadeTicks = value },
        };
    }
}
