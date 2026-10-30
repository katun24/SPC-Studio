using System.Text;
using SpcStudio.Model;

namespace SpcStudio.Import;

// AddmusicK's pass over a song's text before its parser reads it: the #amk version, the conditional directives and the comments.
internal static class MmlPreprocessor
{
    private const string AmkOneSpelling = "amk=1";
    private const int ValuelessDefineValue = 1;
    internal const int OldestDialect = 2;
    internal const int NewestDialect = 4;

    // Reads the #amk version the song is written in, the last one written being the one that counts, and refuses a version this parser does not read.
    internal static int ReadAmkVersion(string text)
    {
        var (version, versionLine) = LastAmkVersionWritten(text);
        return version switch
        {
            OldestDialect or NewestDialect => version,
            0 => throw MmlImportException.NoAmkDirective(),
            3 => throw MmlImportException.AmkThreeRefused(versionLine),
            _ => throw MmlImportException.AmkVersionUnread(version, versionLine),
        };
    }

    // Whether a text writes a line saying which AddmusicK it is written for, one this parser refuses included.
    internal static bool WritesDialectLine(string text)
    {
        try
        {
            return LastAmkVersionWritten(text).Version != 0;
        }
        catch (MmlImportException)
        {
            return true;
        }
    }

    // The last #amk version a text writes and its line, a version of zero where it writes none, refusing another dialect and an #amk with no number.
    private static (int Version, int Line) LastAmkVersionWritten(string text)
    {
        int version = 0;
        int line = 1;
        int versionLine = 0;
        for (int position = 0; position < text.Length; position++)
        {
            if (text[position] == '\n')
            {
                line++;
                continue;
            }

            if (text[position] == '"')
            {
                int closingQuote = ClosingQuoteAfter(text, position);
                line += NewlineCount(text[(position + 1)..closingQuote]);
                position = closingQuote;
                continue;
            }

            if (text[position] != '#')
            {
                continue;
            }

            position++;
            bool isAmkOne = IsAmkOneSpelledAt(text, position);
            string directive = ReadWord(text, ref position);
            position--;
            if (isAmkOne)
            {
                version = 1;
                versionLine = line;
                continue;
            }

            if (directive is AmkDirectives.Am4 or AmkDirectives.Amm)
            {
                throw MmlImportException.OtherDialect(directive, line);
            }

            if (directive != AmkDirectives.Amk)
            {
                continue;
            }

            int versionDigitsStart = position + 1;
            SkipBlanksWithinLine(text, ref versionDigitsStart);
            int versionDigitsEnd = DigitRuns.EndOf(text, versionDigitsStart);
            if (versionDigitsEnd == versionDigitsStart)
            {
                throw MmlImportException.AmkVersionMissing(line);
            }

            version = int.Parse(text[versionDigitsStart..versionDigitsEnd]);
            versionLine = line;
        }

        return (version, versionLine);
    }

    // Where the quoted text opened by the quote mark at a position closes, or the text's end where nothing closes it.
    private static int ClosingQuoteAfter(string text, int openingQuote)
    {
        int position = openingQuote + 1;
        while (position < text.Length && text[position] != '"')
        {
            position++;
        }

        return position;
    }

    // How many line breaks a text holds.
    private static int NewlineCount(string text)
    {
        return text.Count(character => character == '\n');
    }

    // Whether #amk=1 is spelled at a position, just after its '#', compared character for character.
    private static bool IsAmkOneSpelledAt(string text, int position)
    {
        return AmkTextReader.IsSpelledAt(text, position, AmkOneSpelling);
    }

    // Resolves the conditional directives and #error, and takes #amk out, keeping only the text of the blocks that are switched on and every newline.
    internal static string ResolveDirectives(string text)
    {
        var defines = new Dictionary<string, int>(StringComparer.Ordinal);
        var enclosingBlocksSwitchedOn = new Stack<bool>();
        var keptText = new StringBuilder(text.Length);
        int position = 0;
        int line = 1;
        bool isSwitchedOn = true;
        while (position < text.Length)
        {
            if (text[position] == '\n')
            {
                line++;
            }

            if (text[position] == '"')
            {
                int quotedStart = position + 1;
                position = ClosingQuoteAfter(text, position);
                if (position == text.Length)
                {
                    throw MmlImportException.QuoteUnclosed(line);
                }

                string quotedText = text[quotedStart..position++];
                int quotedNewlineCount = NewlineCount(quotedText);
                line += quotedNewlineCount;
                if (isSwitchedOn)
                {
                    keptText.Append('"').Append(quotedText).Append('"');
                }
                else
                {
                    keptText.Append('\n', quotedNewlineCount);
                }

                continue;
            }

            if (text[position] != '#')
            {
                if (isSwitchedOn || text[position] == '\n')
                {
                    keptText.Append(text[position]);
                }

                position++;
                continue;
            }

            position++;
            if (IsAmkOneSpelledAt(text, position))
            {
                position += AmkOneSpelling.Length;
                continue;
            }

            string directive = ReadWord(text, ref position);
            switch (directive)
            {
                case AmkDirectives.Define or AmkDirectives.Undef or AmkDirectives.Ifdef or AmkDirectives.Ifndef or AmkDirectives.If when !isSwitchedOn:
                    throw MmlImportException.DirectiveInSwitchedOffBlock(directive, line);

                case AmkDirectives.Define:
                    ReadDefine(text, ref position, defines, line);
                    break;

                case AmkDirectives.Undef:
                    defines.Remove(ReadDirectiveName(text, ref position, directive, line));
                    break;

                case AmkDirectives.Ifdef or AmkDirectives.Ifndef:
                    {
                        string name = ReadDirectiveName(text, ref position, directive, line);
                        enclosingBlocksSwitchedOn.Push(isSwitchedOn);
                        isSwitchedOn = defines.ContainsKey(name) == (directive == AmkDirectives.Ifdef);
                        break;
                    }

                case AmkDirectives.If:
                    {
                        bool isConditionMet = ReadIfCondition(text, ref position, defines, line);
                        enclosingBlocksSwitchedOn.Push(isSwitchedOn);
                        isSwitchedOn = isConditionMet;
                        break;
                    }

                case AmkDirectives.Endif:
                    if (enclosingBlocksSwitchedOn.Count == 0)
                    {
                        throw MmlImportException.EndifWithoutIf(line);
                    }

                    isSwitchedOn = enclosingBlocksSwitchedOn.Pop();
                    break;

                case AmkDirectives.Error when isSwitchedOn:
                    throw MmlImportException.ErrorDirectiveReached(line);

                case AmkDirectives.Error:
                    break;

                case AmkDirectives.Amk:
                    ReadNextWord(text, ref position);
                    break;

                default:
                    if (isSwitchedOn)
                    {
                        keptText.Append('#').Append(directive);
                    }

                    break;
            }
        }

        if (enclosingBlocksSwitchedOn.Count != 0)
        {
            throw MmlImportException.IfWithoutEndif(line);
        }

        return keptText.ToString();
    }

    // Takes the ; comments out of the text, keeping every newline.
    internal static string RemoveComments(string text)
    {
        if (!text.Contains(';'))
        {
            return text;
        }

        var keptText = new StringBuilder(text.Length);
        bool isInComment = false;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                isInComment = false;
            }
            else if (character == ';')
            {
                isInComment = true;
            }

            if (!isInComment)
            {
                keptText.Append(character);
            }
        }

        return keptText.ToString();
    }

    // Reads a #define's name and value into the defined names.
    private static void ReadDefine(string text, ref int position, Dictionary<string, int> defines, int line)
    {
        string name = ReadDirectiveName(text, ref position, AmkDirectives.Define, line);
        string value = ReadNextWord(text, ref position);
        defines[name] = value.Length == 0 ? ValuelessDefineValue : ParseDirectiveNumber(value, AmkDirectives.Written(AmkDirectives.Define), line);
    }

    // Reads a #if's name, comparison and number, and answers whether the defined name's value meets the condition.
    private static bool ReadIfCondition(string text, ref int position, Dictionary<string, int> defines, int line)
    {
        string name = ReadDirectiveName(text, ref position, AmkDirectives.If, line);
        if (!defines.TryGetValue(name, out int definedValue))
        {
            throw MmlImportException.IfOnUndefinedName(name, line);
        }

        string comparison = ReadNextWord(text, ref position);
        if (comparison.Length == 0)
        {
            throw MmlImportException.IfComparisonMissing(line);
        }

        string value = ReadNextWord(text, ref position);
        if (value.Length == 0)
        {
            throw MmlImportException.IfNumberMissing(line);
        }

        int comparedValue = ParseDirectiveNumber(value, AmkDirectives.Written(AmkDirectives.If), line);
        return comparison switch
        {
            "==" => definedValue == comparedValue,
            "!=" => definedValue != comparedValue,
            ">" => definedValue > comparedValue,
            "<" => definedValue < comparedValue,
            ">=" => definedValue >= comparedValue,
            "<=" => definedValue <= comparedValue,
            _ => throw MmlImportException.IfComparisonUnknown(comparison, line),
        };
    }

    // Reads the name a directive is followed by, refusing a directive with none.
    private static string ReadDirectiveName(string text, ref int position, string directive, int line)
    {
        string name = ReadNextWord(text, ref position);
        if (name.Length == 0)
        {
            throw MmlImportException.DirectiveNameMissing(directive, line);
        }

        return name;
    }

    // Steps past the whitespace in front of the next word on the line and reads that word.
    private static string ReadNextWord(string text, ref int position)
    {
        SkipBlanksWithinLine(text, ref position);
        return ReadWord(text, ref position);
    }

    // Reads one word, which ends at a space, a tab or the end of a line.
    private static string ReadWord(string text, ref int position)
    {
        int start = position;
        while (position < text.Length && text[position] is not (' ' or '\t' or '\r' or '\n'))
        {
            position++;
        }

        return text[start..position];
    }

    // Steps past the whitespace in front of the next word, stopping at the end of the line.
    private static void SkipBlanksWithinLine(string text, ref int position)
    {
        while (position < text.Length && AmkWhitespace.IsWhitespaceWithinLine(text[position]))
        {
            position++;
        }
    }

    // The integer a #define or a #if carries: an optional sign and the digits behind it, anything after those being ignored.
    private static int ParseDirectiveNumber(string token, string directive, int line)
    {
        int digitsStart = token.Length > 0 && token[0] is '-' or '+' ? 1 : 0;
        int digitsEnd = DigitRuns.EndOf(token, digitsStart);
        if (digitsEnd == digitsStart || !int.TryParse(token[..digitsEnd], out int value))
        {
            throw MmlImportException.DirectiveValueNotNumber(directive, token, line);
        }

        return value;
    }
}
