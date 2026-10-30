namespace SpcStudio.Model;

// The echo unit's three settings as one value: the delay and feedback registers, and an index into the song's filters rather than the byte $F1 carries.
public readonly record struct EchoUnitState(int Delay, int Feedback, int Filter)
{
    public static readonly EchoUnitState Default = new(SnesEcho.DefaultDelay, SnesEcho.DefaultFeedback, SnesEcho.DefaultFilter);

    // The value one of the three settings holds.
    public int this[EchoSetting setting]
    {
        get
        {
            return setting switch
            {
                EchoSetting.Delay => Delay,
                EchoSetting.Feedback => Feedback,
                _ => Filter,
            };
        }
    }

    // The same state with one of the three settings moved.
    public EchoUnitState With(EchoSetting setting, int value)
    {
        return setting switch
        {
            EchoSetting.Delay => this with { Delay = value },
            EchoSetting.Feedback => this with { Feedback = value },
            _ => this with { Filter = value },
        };
    }
}
