using System.Text.RegularExpressions;

namespace SpcStudio.Model;

// The names something new is given where the name it would take is taken: a numbered name, or the name with a copy number after it.
public static partial class FreeNames
{
    private const int FirstNumber = 1;
    private const int FirstCopyNumber = 2;

    // The first of a prefix followed by 1, 2 and so on, then an extension, that is not taken.
    public static string FirstFreeNumberedName(string prefix, string extension, Func<string, bool> isTaken)
    {
        return FirstUntakenNumbered(FirstNumber, number => $"{prefix} {number}{extension}", isTaken);
    }

    // The name itself where it is not taken, and otherwise its stem followed by the first copy number from 2 up that is not, an extension kept at the end.
    public static string FirstFreeName(string name, string extension, Func<string, bool> isTaken)
    {
        if (!isTaken(name + extension))
        {
            return name + extension;
        }

        string stem = StemOf(name);
        return FirstUntakenNumbered(FirstCopyNumber, number => $"{stem} ({number}){extension}", isTaken);
    }

    // The name with any copy number at its end taken off.
    public static string StemOf(string name)
    {
        var match = CopyNumberedName().Match(name);
        return match.Success ? match.Groups[1].Value : name;
    }

    // The first name a number from a starting number up makes that is not taken.
    private static string FirstUntakenNumbered(int firstNumber, Func<int, string> nameOf, Func<string, bool> isTaken)
    {
        for (int number = firstNumber; ; number++)
        {
            string numberedName = nameOf(number);
            if (!isTaken(numberedName))
            {
                return numberedName;
            }
        }
    }

    // A name ending in a space and a number in brackets, the part in front of them captured.
    [GeneratedRegex(@"^(.*) \(\d+\)$")]
    private static partial Regex CopyNumberedName();
}
