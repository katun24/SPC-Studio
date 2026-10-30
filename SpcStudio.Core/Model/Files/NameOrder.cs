namespace SpcStudio.Model;

// The order names are listed in: letter case aside, a run of digits by the number it spells, and the names' own characters where nothing else tells two apart.
public sealed class NameOrder : IComparer<string>
{
    private const char LeadingZero = '0';

    public static readonly NameOrder Instance = new();

    private NameOrder()
    {
    }

    // Which of two names comes first.
    public int Compare(string? name, string? otherName)
    {
        if (name is null || otherName is null)
        {
            return string.CompareOrdinal(name, otherName);
        }

        int position = 0;
        int otherPosition = 0;
        while (position < name.Length && otherPosition < otherName.Length)
        {
            int pieceOrder;
            if (char.IsAsciiDigit(name[position]) && char.IsAsciiDigit(otherName[otherPosition]))
            {
                int runEnd = DigitRuns.EndOf(name, position);
                int otherRunEnd = DigitRuns.EndOf(otherName, otherPosition);
                pieceOrder = CompareNumbers(name[position..runEnd], otherName[otherPosition..otherRunEnd]);
                position = runEnd;
                otherPosition = otherRunEnd;
            }
            else
            {
                pieceOrder = char.ToUpperInvariant(name[position]).CompareTo(char.ToUpperInvariant(otherName[otherPosition]));
                position++;
                otherPosition++;
            }

            if (pieceOrder != 0)
            {
                return pieceOrder;
            }
        }

        int remainderLengthOrder = (name.Length - position).CompareTo(otherName.Length - otherPosition);
        return remainderLengthOrder != 0 ? remainderLengthOrder : string.CompareOrdinal(name, otherName);
    }

    // Which of two runs of digits spells the smaller number, however long either is.
    private static int CompareNumbers(string digits, string otherDigits)
    {
        string significantDigits = digits.TrimStart(LeadingZero);
        string otherSignificantDigits = otherDigits.TrimStart(LeadingZero);
        int digitCountOrder = significantDigits.Length.CompareTo(otherSignificantDigits.Length);
        return digitCountOrder != 0 ? digitCountOrder : string.CompareOrdinal(significantDigits, otherSignificantDigits);
    }
}
