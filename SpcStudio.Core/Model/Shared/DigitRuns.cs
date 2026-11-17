namespace SpcStudio.Model;

// Runs of digits inside a text.
public static class DigitRuns
{
    // The index just past the run of digits that starts at an index.
    public static int EndOf(string text, int start)
    {
        int end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return end;
    }
}
