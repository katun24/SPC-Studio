namespace SpcStudio.Model;

// One stretch of a note played under $E5: where it starts, counted from where the one in front ended, how long it lasts, and the rate and the amount the command puts there.
public readonly record struct NoteTremolo(int DelayTicks, int LengthTicks, int Rate, int Amount) : ISweepStretch<NoteTremolo, TremoloState>
{
    public bool IsEndless { get; init; }

    // The whole state this puts in the driver's three bytes while it runs.
    public TremoloState State
    {
        get
        {
            return new(SnesSweep.DefaultDelay, Rate, Amount);
        }
    }

    // Whether it ducks anything at all.
    public bool IsActive
    {
        get
        {
            return State.IsActive;
        }
    }

    // The value one of the four holds.
    public int this[TremoloSetting setting]
    {
        get
        {
            return setting switch
            {
                TremoloSetting.Delay => DelayTicks,
                TremoloSetting.Length => LengthTicks,
                TremoloSetting.Rate => Rate,
                _ => Amount,
            };
        }
    }

    // This stretch with its delay and length replaced and its own values untouched.
    public NoteTremolo WithSpan(int delayTicks, int lengthTicks)
    {
        return this with { DelayTicks = delayTicks, LengthTicks = lengthTicks };
    }

    // This stretch running on to the end of every note playing it, or lasting its own length, with its other values untouched.
    public NoteTremolo WithEndless(bool isEndless)
    {
        return this with { IsEndless = isEndless };
    }

    // This stretch with one of the four replaced and the other three as they were.
    public NoteTremolo With(TremoloSetting setting, int value)
    {
        return setting switch
        {
            TremoloSetting.Delay => this with { DelayTicks = value },
            TremoloSetting.Length => this with { LengthTicks = value },
            TremoloSetting.Rate => this with { Rate = value },
            _ => this with { Amount = value },
        };
    }
}
