namespace SpcStudio.Import;

// The characters AddmusicK reads as whitespace, which are the C library's own.
public static class AmkWhitespace
{
    public static readonly char[] WhitespaceCharacters = [' ', '\t', '\n', '\v', '\f', '\r'];

    // Whether a character is whitespace as AddmusicK reads it.
    public static bool IsWhitespace(char character)
    {
        return Array.IndexOf(WhitespaceCharacters, character) >= 0;
    }

    // Whether a character is whitespace that does not end a line, which is what AddmusicK skips between a directive's words.
    public static bool IsWhitespaceWithinLine(char character)
    {
        return IsWhitespace(character) && !IsLineBreak(character);
    }

    // Whether a character ends a line.
    public static bool IsLineBreak(char character)
    {
        return character is '\n' or '\r';
    }

    // Where the line a position is on ends: its first line break at or after the position, or the text's length where there is none.
    public static int LineEndFrom(string text, int position)
    {
        return FirstMatchFrom(text, position, IsLineBreak);
    }

    // Where the first character at or after a position that is not whitespace stands, or the text's length where every one is.
    public static int NextNonWhitespace(string text, int position)
    {
        return FirstMatchFrom(text, position, character => !IsWhitespace(character));
    }

    // Where the first whitespace at or after a position stands, or the text's length where there is none.
    public static int NextWhitespace(string text, int position)
    {
        return FirstMatchFrom(text, position, IsWhitespace);
    }

    // Where the first character at or after a position that a test accepts stands, or the text's length where none is.
    private static int FirstMatchFrom(string text, int position, Func<char, bool> isMatch)
    {
        while (position < text.Length && !isMatch(text[position]))
        {
            position++;
        }

        return position;
    }
}
