namespace SpcStudio.Audio;

// One row of the sample editor's rack - which effect, whether it is switched on, and one value per setting its kind declares - and the effects themselves, each audio in and audio out.
public sealed record SampleEffect(SampleEffectKind Kind, bool IsEnabled, double[] Values)
{
    private const double LowestLowpassHz = 100.0;
    private const int LowpassStages = 2;
    private const double HighpassOffHz = 20.0;
    private const int HighpassStages = 2;
    private const double NeutralGain = 1.0;
    private const double MaxGain = 4.0;
    private const double HalfPowerRatio = 2.0;

    // The sliders on a kind's row on a sample at a rate, in the order they appear on it.
    public static IReadOnlyList<SampleSetting> SettingsOf(SampleEffectKind kind, int rateHz)
    {
        return kind switch
        {
            SampleEffectKind.Gain => [new("Gain", "", 0.0, MaxGain, NeutralGain, "0.00", IsLogarithmic: false, IsWholeNumber: false)],
            SampleEffectKind.Lowpass => [CutoffSetting(LowestLowpassHz, HighestHzAt(rateHz), HighestHzAt(rateHz))],
            SampleEffectKind.Highpass => [CutoffSetting(HighpassOffHz, HighestHzAt(rateHz), HighpassOffHz)],
            _ => [],
        };
    }

    // The row a kind starts as on a sample at a rate: switched off, at the settings that change nothing.
    public static SampleEffect FreshRow(SampleEffectKind kind, int rateHz)
    {
        return new(kind, IsEnabled: false, SettingsOf(kind, rateHz).Select(setting => setting.DefaultValue).ToArray());
    }

    // What a kind's row is called in the panel.
    public static string NameOf(SampleEffectKind kind)
    {
        return kind switch
        {
            SampleEffectKind.Gain => "Gain",
            SampleEffectKind.Lowpass => "Lowpass",
            SampleEffectKind.Highpass => "Highpass",
            SampleEffectKind.Normalize => "Normalize",
            _ => kind.ToString(),
        };
    }

    // A number of passes of a one-pole lowpass at a cutoff over points at a rate, the passes together at 71% at that cutoff.
    public static short[] Lowpass(short[] points, double cutoffHz, int stages, int rateHz)
    {
        double perPassCutoffHz = cutoffHz / PassCutoffFactor(stages);
        double stepFractionTowardInput = 1.0 - Math.Exp(-2.0 * Math.PI * perPassCutoffHz / rateHz);
        return Filtered(points, stages, work =>
        {
            double output = work[0];
            for (int i = 0; i < work.Length; i++)
            {
                output += stepFractionTowardInput * (work[i] - output);
                work[i] = output;
            }
        });
    }

    // A number of passes of a bilinear one-pole highpass at a cutoff over points at a rate, the passes together at 71% at that cutoff.
    public static short[] Highpass(short[] points, double cutoffHz, int stages, int rateHz)
    {
        double perPassCutoffHz = cutoffHz * PassCutoffFactor(stages);
        double prewarpedCutoff = Math.Tan(Math.PI * perPassCutoffHz / rateHz);
        double inputCoefficient = 1.0 / (1.0 + prewarpedCutoff);
        double feedbackCoefficient = (prewarpedCutoff - 1.0) / (prewarpedCutoff + 1.0);
        return Filtered(points, stages, work =>
        {
            double previousIn = work[0];
            double output = 0.0;
            for (int i = 0; i < work.Length; i++)
            {
                double input = work[i];
                output = inputCoefficient * (input - previousIn) - feedbackCoefficient * output;
                previousIn = input;
                work[i] = output;
            }
        });
    }

    // Whether these settings would change the audio of a sample at a rate at all, which a normalize row always answers yes to.
    public bool DoesChangeAudioAt(int rateHz)
    {
        return Kind switch
        {
            SampleEffectKind.Gain => Values[0] != NeutralGain,
            SampleEffectKind.Lowpass => Values[0] < HighestHzAt(rateHz),
            SampleEffectKind.Highpass => Values[0] > HighpassOffHz,
            SampleEffectKind.Normalize => true,
            _ => false,
        };
    }

    // Whether the row is still as its kind starts on a sample at a rate: switched off, at every setting's default.
    public bool IsUntouchedAt(int rateHz)
    {
        return IsSameAs(FreshRow(Kind, rateHz));
    }

    // This row with every value above its setting's top on a sample at a rate moved down to that top, or this row where none is.
    public SampleEffect HeldTo(int rateHz)
    {
        var heldValues = Values.Zip(SettingsOf(Kind, rateHz), (value, setting) => Math.Min(value, setting.Maximum)).ToArray();
        return heldValues.AsSpan().SequenceEqual(Values) ? this : this with { Values = heldValues };
    }

    // This row with one setting changed, the rest left alone.
    public SampleEffect WithValue(int setting, double value)
    {
        var copiedValues = (double[])Values.Clone();
        copiedValues[setting] = value;
        return this with { Values = copiedValues };
    }

    // Whether another row is of the same kind, switched the same way and set to the same values.
    public bool IsSameAs(SampleEffect other)
    {
        return Kind == other.Kind && IsEnabled == other.IsEnabled && Values.AsSpan().SequenceEqual(other.Values);
    }

    // Points at a rate with this effect applied, or the same array where the row is switched off or set where it changes nothing.
    public short[] Apply(short[] points, int rateHz)
    {
        if (!IsEnabled || !DoesChangeAudioAt(rateHz))
        {
            return points;
        }

        return Kind switch
        {
            SampleEffectKind.Gain => DspPoint.Scaled(points, _ => Values[0]),
            SampleEffectKind.Lowpass => Lowpass(points, Values[0], LowpassStages, rateHz),
            SampleEffectKind.Highpass => Highpass(points, Values[0], HighpassStages, rateHz),
            SampleEffectKind.Normalize => Normalized(points),
            _ => points,
        };
    }

    // A filter's cutoff slider over a range of frequencies, moving through them in doublings.
    private static SampleSetting CutoffSetting(double minimumHz, double maximumHz, double defaultHz)
    {
        return new("Cutoff", "Hz", minimumHz, maximumHz, defaultHz, "0", IsLogarithmic: true, IsWholeNumber: false);
    }

    // The highest frequency a sample at a rate can hold, which is the top of both filters' sliders.
    private static double HighestHzAt(int rateHz)
    {
        return rateHz / 2.0;
    }

    // Points scaled so the loudest is full scale, or the same array where they are silent or already there.
    private static short[] Normalized(short[] points)
    {
        double peak = 0.0;
        foreach (short point in points)
        {
            peak = Math.Max(peak, Math.Abs((double)point));
        }

        if (peak == 0.0)
        {
            return points;
        }

        double fullScaleMultiplier = short.MaxValue / peak;
        return fullScaleMultiplier == 1.0 ? points : DspPoint.Scaled(points, _ => fullScaleMultiplier);
    }

    // Points run through a number of passes of one filter in place, or the same array where there are no points.
    private static short[] Filtered(short[] points, int stages, Action<double[]> runPass)
    {
        if (points.Length == 0)
        {
            return points;
        }

        var work = Widened(points);
        for (int pass = 0; pass < stages; pass++)
        {
            runPass(work);
        }

        return Narrowed(work);
    }

    // The points as doubles, which is what a filter chain runs in.
    private static double[] Widened(short[] points)
    {
        var work = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            work[i] = points[i];
        }

        return work;
    }

    // A filter chain's result back as points, rounded and clamped.
    private static short[] Narrowed(double[] work)
    {
        var points = new short[work.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = DspPoint.Rounded(work[i]);
        }

        return points;
    }

    // How far one pass's cutoff stands from the chain's, which puts a chain of that many passes at 71% at the chain's cutoff.
    private static double PassCutoffFactor(int stages)
    {
        return Math.Sqrt(Math.Pow(HalfPowerRatio, 1.0 / stages) - 1.0);
    }
}
