using System.Text;
using SpcStudio.Export;

namespace SpcStudio.Import;

// AddmusicK's reading of a song's text, which the MML parser and the song data count both read through: the cursor and the line it is on, the
// "find=replacement" directives and their expansion, whitespace, single characters, decimal and hex numbers, quoted strings, a directive's block,
// a note's sharp or flat and its dots, and whether the SMW velocity table is on.
internal abstract class AmkTextReader(string text)
{
    protected const int NoNumber = -1;
    protected const char EndOfText = '\0';
    private const int MaxReplacementDepth = 500;
    private const int MaxHexDigits = 2;
    private const int DecimalRadix = 10;
    private const int HexRadix = 16;
    private const char QuoteEscape = '\\';
    private const int SharpSemitones = 1;
    private const int FlatSemitones = -1;
    private const char SharpMark = '+';
    private const char FlatMark = '-';
    internal const int NoAccidentalSemitones = 0;

    protected int _position;
    protected int _line = 1;
    private List<KeyValuePair<string, string>>? _replacementsLongestFirst;

    // Whether the SMW velocity table is on, which a second smwvtable option in a row writes nothing for.
    protected bool _usesSmwVelocities;

    // What each "find=replacement" defined so far stands for, matched case for case.
    private readonly Dictionary<string, string> _replacements = new(StringComparer.Ordinal);

    // The text being read, which a replacement is spliced into in place.
    protected string _text = text;

    // The next character that is not whitespace, without consuming it, or the end-of-text mark where the text runs out first.
    protected char PeekNonWhitespace()
    {
        int index = PeekNonWhitespaceIndex();
        return index < _text.Length ? _text[index] : EndOfText;
    }

    // Where the next character that is not whitespace stands, without consuming anything.
    protected int PeekNonWhitespaceIndex()
    {
        return AmkWhitespace.NextNonWhitespace(_text, _position);
    }

    // Reads a "find=replacement" directive into the table of replacements.
    protected void ReadReplacementDirective()
    {
        string definition = ReadQuotedString();
        int equalsIndex = definition.IndexOf('=');
        if (equalsIndex < 0)
        {
            throw MmlImportException.ReplacementEqualsMissing(_line);
        }

        // What the replacement finds, the whitespace in front of the equals sign taken off.
        string find = definition[..equalsIndex].TrimEnd(AmkWhitespace.WhitespaceCharacters);

        // What stands in the find's place, each line break in it held as a space.
        string replacement = definition[(equalsIndex + 1)..].TrimStart(AmkWhitespace.WhitespaceCharacters).Replace('\r', ' ').Replace('\n', ' ');
        if (find.Length == 0)
        {
            throw MmlImportException.ReplacementFindEmpty(_line);
        }

        _replacements[find] = replacement;
        _replacementsLongestFirst = null;
    }

    // Expands a replacement standing at the cursor in place, and goes on expanding what that produced.
    protected void ExpandReplacementAtCursor(int depth = 0)
    {
        if (_replacements.Count == 0)
        {
            return;
        }

        if (depth == MaxReplacementDepth)
        {
            throw MmlImportException.ReplacementExpandsIntoItself(_line);
        }

        _replacementsLongestFirst ??= [.. _replacements.OrderByDescending(pair => pair.Key.Length)];
        foreach (var (find, replacement) in _replacementsLongestFirst)
        {
            if (!IsSpelledAt(_text, _position, find))
            {
                continue;
            }

            _text = string.Concat(_text.AsSpan(0, _position), replacement, _text.AsSpan(_position + find.Length));
            ExpandReplacementAtCursor(depth + 1);
        }
    }

    // Steps over one character, counting the line it ends.
    protected void AdvanceCountingLines()
    {
        if (_text[_position] == '\n')
        {
            _line++;
        }

        _position++;
    }

    // Steps over the whitespace at the cursor, counting the lines it ends.
    protected void SkipWhitespace()
    {
        int nextNonWhitespaceIndex = PeekNonWhitespaceIndex();
        while (_position < nextNonWhitespaceIndex)
        {
            AdvanceCountingLines();
        }
    }

    // Whether the character at the cursor is the one given.
    protected bool IsCharacterAtCursor(char expected)
    {
        return IsCharacterAt(_position, expected);
    }

    // Whether the text holds a given character at a position.
    protected bool IsCharacterAt(int position, char character)
    {
        return position < _text.Length && _text[position] == character;
    }

    // Consumes the character at the cursor where it is the one given, and says whether it was.
    protected bool TryConsume(char expected)
    {
        if (!IsCharacterAtCursor(expected))
        {
            return false;
        }

        _position++;
        return true;
    }

    // Steps over the whitespace at the cursor, then consumes the character after it where it is the one given, and says whether it was.
    protected bool TryConsumeAfterWhitespace(char expected)
    {
        SkipWhitespace();
        return TryConsume(expected);
    }

    // Reads everything up to the next whitespace, which is how a directive's name, an #spc field's and an #option's are read.
    protected string ReadWord()
    {
        int startIndex = _position;
        _position = AmkWhitespace.NextWhitespace(_text, _position);
        return _text[startIndex.._position];
    }

    // Reads a decimal number after expanding a replacement at the cursor, or the no-number mark where there are no digits.
    protected int ReadInt()
    {
        ExpandReplacementAtCursor();
        return ReadDigits();
    }

    // Reads the decimal digits at the cursor as a number, or the no-number mark where there are none.
    protected int ReadDigits()
    {
        return ReadDigitsInRadix(DecimalRadix, int.MaxValue);
    }

    // Reads a decimal number with an optional leading minus after expanding a replacement at the cursor, or null where there are no digits.
    protected int? ReadIntWithNegative()
    {
        ExpandReplacementAtCursor();
        bool isNegative = TryConsume('-');
        int value = ReadDigits();
        if (value < 0)
        {
            return null;
        }

        return isNegative ? -value : value;
    }

    // Reads at most a byte's worth of hex digits after expanding a replacement at the cursor, or the no-number mark where there are none.
    protected int ReadHex()
    {
        ExpandReplacementAtCursor();
        return ReadDigitsInRadix(HexRadix, MaxHexDigits);
    }

    // Steps over a comma with the whitespace on both sides of it and says whether there was one, stepping over only the whitespace in front where there
    // was not.
    protected bool TryConsumeComma()
    {
        if (!TryConsumeAfterWhitespace(','))
        {
            return false;
        }

        SkipWhitespace();
        return true;
    }

    // Reads a decimal number, refused with the refusal given at the cursor's line where there are no digits.
    protected int ReadRequiredInt(Func<int, Exception> missingRefusal)
    {
        return RequireNumber(ReadInt(), missingRefusal);
    }

    // Reads a hex number, refused with the refusal given at the cursor's line where there are no digits.
    protected int ReadRequiredHex(Func<int, Exception> missingRefusal)
    {
        return RequireNumber(ReadHex(), missingRefusal);
    }

    // Reads a '$' and the hex byte after it at the cursor, refused one way where the '$' is missing and another where its digits are.
    protected int ReadDollarHex(Func<int, Exception> dollarMissingRefusal, Func<int, Exception> digitsMissingRefusal)
    {
        if (!TryConsume('$'))
        {
            throw dollarMissingRefusal(_line);
        }

        return ReadRequiredHex(digitsMissingRefusal);
    }

    // Reads a decimal number with an optional leading minus, refused with the refusal given at the cursor's line where there are no digits.
    protected int ReadRequiredIntWithNegative(Func<int, Exception> missingRefusal)
    {
        return ReadIntWithNegative() ?? throw missingRefusal(_line);
    }

    // Reads a directive's block from its opening brace to its closing one, an entry at a time.
    protected void ReadBraceBlock(string writtenDirective, Action readEntry)
    {
        RequireOpeningBrace(writtenDirective);
        while (!TryConsumeAfterWhitespace('}'))
        {
            readEntry();
        }
    }

    // Turns the SMW velocity table on for a smwvtable option, and says whether that writes anything, which it does not while the table is on already.
    protected bool TrySwitchSmwVelocitiesOn()
    {
        bool wasOff = !_usesSmwVelocities;
        _usesSmwVelocities = true;
        return wasOff;
    }

    // Reads the sharp or the flat behind a note letter, as the semitones it moves the note.
    protected int ReadAccidentalSemitones()
    {
        int semitones = _position < _text.Length ? AccidentalSemitonesOf(_text[_position]) : NoAccidentalSemitones;
        if (semitones != NoAccidentalSemitones)
        {
            _position++;
        }

        return semitones;
    }

    // The semitones a sharp or a flat moves a note, none for any other character.
    internal static int AccidentalSemitonesOf(char mark)
    {
        return mark switch
        {
            SharpMark => SharpSemitones,
            FlatMark => FlatSemitones,
            _ => NoAccidentalSemitones,
        };
    }

    // Whether a spelling stands whole at a position in a text, compared character for character.
    internal static bool IsSpelledAt(string text, int position, string spelling)
    {
        return position + spelling.Length <= text.Length && string.CompareOrdinal(text, position, spelling, 0, spelling.Length) == 0;
    }

    // Reads the dots written after a duration and lengthens the duration by them.
    protected int ReadDotsOnto(int ticks)
    {
        int dotCount = 0;
        while (TryConsume('.'))
        {
            dotCount++;
        }

        return MmlDurationEncoding.ApplyDotsToTicks(ticks, dotCount);
    }

    // Reads a double-quoted string, line breaks included, and returns what stands between the quotes, a backslash in front of a quote mark making it
    // part of the string and a backslash in front of anything else refused.
    protected string ReadQuotedString()
    {
        if (!TryConsumeAfterWhitespace('"'))
        {
            throw MmlImportException.QuotedStringMissing(_line);
        }

        var value = new StringBuilder();
        while (_position < _text.Length && _text[_position] != '"')
        {
            bool isAtEscape = _text[_position] == QuoteEscape;
            if (isAtEscape && !IsCharacterAt(_position + 1, '"'))
            {
                throw MmlImportException.QuotedStringBackslashUnescaped(_line);
            }

            if (isAtEscape)
            {
                _position++;
            }

            value.Append(_text[_position]);
            AdvanceCountingLines();
        }

        if (_position >= _text.Length)
        {
            throw MmlImportException.QuotedStringUnclosed(_line);
        }

        _position++;
        return value.ToString();
    }

    // Consumes the '{' a directive's block opens with, refusing the directive where it has none.
    protected void RequireOpeningBrace(string directiveName)
    {
        if (!TryConsumeAfterWhitespace('{'))
        {
            throw MmlImportException.BlockOpeningBraceMissing(directiveName, _line);
        }
    }

    // A number just read, refused with the refusal given at the cursor's line where there were no digits.
    private int RequireNumber(int readNumber, Func<int, Exception> missingRefusal)
    {
        if (readNumber == NoNumber)
        {
            throw missingRefusal(_line);
        }

        return readNumber;
    }

    // Reads the digits of a radix at the cursor as a number, at most a count of them, or the no-number mark where there are none.
    private int ReadDigitsInRadix(int radix, int maxDigitCount)
    {
        int startIndex = _position;
        int value = 0;
        while (_position < _text.Length && _position - startIndex < maxDigitCount && Uri.IsHexDigit(_text[_position]) && Uri.FromHex(_text[_position]) < radix)
        {
            value = value * radix + Uri.FromHex(_text[_position++]);
        }

        return _position == startIndex ? NoNumber : value;
    }
}
