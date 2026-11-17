using SpcStudio.Model;

namespace SpcStudio;

// A setting held as a whole percentage inside a range, read as that percentage or as the fraction of a whole it is.
public sealed class PercentSetting
{
    private int _percentage;

    // Starts the setting at its default inside its range.
    public PercentSetting(ValueRange range, int defaultPercentage)
    {
        Range = range;
        DefaultPercentage = defaultPercentage;
        _percentage = defaultPercentage;
    }

    public ValueRange Range { get; }
    public int DefaultPercentage { get; }

    // The setting in percent, held inside its range.
    public int Percentage
    {
        get
        {
            return _percentage;
        }
        set
        {
            _percentage = Range.Clamp(value);
        }
    }

    // The setting as the fraction of a whole.
    public double Fraction
    {
        get
        {
            return _percentage / Percent.PerWhole;
        }
    }
}
