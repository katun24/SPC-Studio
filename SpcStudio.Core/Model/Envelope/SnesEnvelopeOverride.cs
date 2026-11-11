namespace SpcStudio.Model;

// The driver's envelope override: what $ED accepts, and how its two arguments encode three registers.
public static class SnesEnvelopeOverride
{
    public const int MinValue = 0;
    public const int MaxValue = 255;

    public static readonly ValueRange ByteRange = new(MinValue, MaxValue);

    // Which of the other two registers $ED's second argument is, given the ADSR1 the command is setting.
    public static EnvelopeSetting SecondRegister(int adsr1)
    {
        return EnvelopeState.IsAdsrEnabledBy(adsr1) ? EnvelopeSetting.Adsr2 : EnvelopeSetting.Gain;
    }

    // $ED's first argument for an ADSR1, which is the ADSR1 with its top bit flipped.
    public static int FirstArgument(int adsr1)
    {
        return adsr1 ^ EnvelopeState.AdsrEnableBit;
    }

    // The ADSR1 an $ED's first argument sets, which is the same flip again.
    public static int Adsr1Of(int firstArgument)
    {
        return firstArgument ^ EnvelopeState.AdsrEnableBit;
    }

    // The $EDs that take one envelope to another: none where they are already equal, one where only the register the target's ADSR1 selects moved beside it, and two where the other one moved as well.
    public static IEnumerable<(int First, int Second)> CommandsBetween(EnvelopeState from, EnvelopeState to)
    {
        if (from == to)
        {
            yield break;
        }

        var selectedRegister = SecondRegister(to.Adsr1);
        var otherRegister = selectedRegister == EnvelopeSetting.Adsr2 ? EnvelopeSetting.Gain : EnvelopeSetting.Adsr2;
        if (from[otherRegister] != to[otherRegister])
        {
            yield return (FirstArgument(to.Adsr1 ^ EnvelopeState.AdsrEnableBit), to[otherRegister]);
        }

        yield return (FirstArgument(to.Adsr1), to[selectedRegister]);
    }

    // What one $ED does to an envelope: ADSR1 always, and whichever of the other two the new ADSR1 selects, the third left as it was.
    public static EnvelopeState WithCommandApplied(EnvelopeState envelope, int firstArgument, int secondArgument)
    {
        int adsr1 = Adsr1Of(firstArgument);
        return envelope.With(EnvelopeSetting.Adsr1, adsr1).With(SecondRegister(adsr1), secondArgument);
    }
}
