using SpcStudio.Theme;

namespace SpcStudio.Controls;

// One column of a list table: its heading, how wide the widest of the heading and anything its cells can ever show comes out, and whether its
// cells stand each hex digit in a slot of one width.
public readonly record struct ListTableColumn(string Header, double ContentWidth, bool HasDigitSlots = false)
{
    public const int DecimalBase = 10;
    public const int HexBase = 16;
    private const string HexDigits = "0123456789ABCDEF";

    // How wide the slot each hex digit of a slotted cell stands in is: the widest hex digit in tabular figures.
    public static readonly double HexDigitSlotWidth = AppFonts.WidestTabularTextWidth(HexDigits.Select(digit => digit.ToString()), ThemeResource.UiFontSize);

    // A column of text, as wide as the wider of its heading and the widest of the texts given in the tabular figures a row's cells are drawn with.
    public static ListTableColumn OfTexts(string header, IEnumerable<string> widestTexts)
    {
        return new ListTableColumn(header, Math.Max(HeaderWidthOf(header), AppFonts.WidestTabularTextWidth(widestTexts, ThemeResource.UiFontSize)));
    }

    // A column of plain numbers, as wide as the wider of its heading and the widest number up to a largest value can come out.
    public static ListTableColumn OfNumbers(string header, int largestValue)
    {
        return OfTexts(header, DigitFills(largestValue, DecimalBase, number => number.ToString()));
    }

    // A column of hex numbers with each digit in a slot, as wide as the wider of its heading and its longest text.
    public static ListTableColumn OfHexNumbers(string header, string longestText)
    {
        return new ListTableColumn(header, Math.Max(HeaderWidthOf(header), SlottedWidthOf(longestText)), HasDigitSlots: true);
    }

    // A column that shows an icon or nothing, as wide as the wider of its heading and the icon.
    public static ListTableColumn OfIcon(string header)
    {
        return new ListTableColumn(header, Math.Max(HeaderWidthOf(header), AppIcons.Size));
    }

    // What a number up to a largest value can come out as at its widest through a format: the format of every number as long as the largest whose
    // digits are all one digit, so whichever digit is widest fills every place.
    public static IEnumerable<string> DigitFills(int largestValue, int numberBase, Func<int, string> textOf)
    {
        int digitCount = DigitCountOf(largestValue, numberBase);
        for (int digit = 0; digit < numberBase; digit++)
        {
            int filledNumber = 0;
            for (int place = 0; place < digitCount; place++)
            {
                filledNumber = filledNumber * numberBase + digit;
            }

            yield return textOf(filledNumber);
        }
    }

    // Whether a character of a slotted cell stands in a slot, which every hex digit does.
    public static bool IsSlotted(char character)
    {
        return char.IsAsciiHexDigit(character);
    }

    // How wide a heading comes out, a heading's text being drawn without tabular figures.
    private static double HeaderWidthOf(string header)
    {
        return AppFonts.WidestUiTextWidth([header], ThemeResource.UiFontSize);
    }

    // How wide a text comes out with each of its hex digits in a slot and the rest of it in tabular figures.
    private static double SlottedWidthOf(string text)
    {
        int slotCount = text.Count(IsSlotted);
        string unslottedText = new([.. text.Where(character => !IsSlotted(character))]);
        double unslottedWidth = unslottedText.Length == 0 ? 0 : AppFonts.WidestTabularTextWidth([unslottedText], ThemeResource.UiFontSize);
        return slotCount * HexDigitSlotWidth + unslottedWidth;
    }

    // How many digits a value takes in a base, which is at least one.
    private static int DigitCountOf(int value, int numberBase)
    {
        int digitCount = 1;
        for (int rest = value / numberBase; rest > 0; rest /= numberBase)
        {
            digitCount++;
        }

        return digitCount;
    }
}
