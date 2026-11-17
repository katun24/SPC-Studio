namespace SpcStudio.Model;

// What two whole numbers share: the greatest number both divide by, and the least number both divide into.
public static class WholeNumbers
{
    // The greatest whole number both divide by.
    public static long GreatestCommonDivisor(long one, long other)
    {
        return other == 0 ? one : GreatestCommonDivisor(other, one % other);
    }

    // The least whole number both divide into.
    public static long LeastCommonMultiple(long one, long other)
    {
        return one / GreatestCommonDivisor(one, other) * other;
    }
}
