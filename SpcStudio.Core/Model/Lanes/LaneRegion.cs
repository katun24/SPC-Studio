namespace SpcStudio.Model;

// One stretch of a region lane at one value, from its start up to but not including its end, ramping to the fade target across the whole of itself where it fades.
public readonly record struct LaneRegion(int Start, int End, int Value, bool Fades, int FadeTo)
{
    // How long the region is, which is also how long its fade takes.
    public int LengthTicks
    {
        get
        {
            return End - Start;
        }
    }

    // The value the region leaves behind it, which is where the next one starts from.
    public int EndValue
    {
        get
        {
            return Fades ? FadeTo : Value;
        }
    }
}
