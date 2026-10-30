using System.Text;
using System.Text.RegularExpressions;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the reading of the channel text the export has already written.
public static partial class MmlExporter
{
    // Reads the tokens of the channel text the export has already written, which the loop and key-on passes work on.
    internal static class WrittenTokens
    {
        public const string LoopMarker = "/";
        public const string OctaveDown = "<";
        public const string OctaveUp = ">";
        public const string SubloopOpen = "[[";
        public const string SubloopClose = "]]";
        public const string LabelDefinitionOpening = "[";
        private const int OctaveTokenLength = 2;
        private const int HexLetteredTokenLength = 3;
        private const int FallbackCommandBytes = 2;
        private const int LetterLength = 1;
        private const int AccidentalLength = 1;

        public static readonly string BendCommand = MmlPitchBend.HexSpelling;
        public static readonly int BendTokenCount = AmkCommands.LengthOf(AmkCommands.PitchBend);

        // Matches a label's definition or call, taking its number and the bracket or count behind it.
        private static readonly Regex LabelToken = new(@"^\((\d+)\)(\[|\d*)$");

        // A text's tokens, each named command joined back into one token with its bracketed arguments.
        public static List<string> Split(string text)
        {
            return WithBracketsJoined(text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        }

        // Whether a token is a note, which is what keys the voice on.
        public static bool IsNote(string token)
        {
            return token[0] is >= 'a' and <= 'g';
        }

        // Whether a token states the octave outright.
        public static bool IsOctave(string token)
        {
            return token.Length == OctaveTokenLength && token[0] == 'o' && char.IsDigit(token[1]);
        }

        // Whether a token moves the octave one down or one up.
        public static bool IsOctaveShift(string token)
        {
            return token is OctaveDown or OctaveUp;
        }

        // Whether a token states the octave or moves it, which only the parser reads.
        public static bool IsOctaveMark(string token)
        {
            return IsOctave(token) || IsOctaveShift(token);
        }

        // Whether a token is a q with its hex digits.
        public static bool IsQ(string token)
        {
            return IsHexLettered(token, 'q');
        }

        // Whether a token is the command with that letter and a two-digit hex value behind it.
        public static bool IsHexLettered(string token, char letter)
        {
            return token.Length == HexLetteredTokenLength && token[0] == letter && Uri.IsHexDigit(token[1]) && Uri.IsHexDigit(token[2]);
        }

        // A command written as its letter and a value as two hex digits, which is how a q and a noise clock are written.
        public static string HexLettered(char letter, int value)
        {
            return $"{letter}{value:X2}";
        }

        // The semitones a written note's sharp or flat moves it, none where it has neither.
        public static int AccidentalSemitonesOf(string note)
        {
            return note.Length > LetterLength ? AmkTextReader.AccidentalSemitonesOf(note[LetterLength]) : AmkTextReader.NoAccidentalSemitones;
        }

        // Where a written note's length starts, behind its letter and its sharp or flat.
        public static int NoteLengthStartOf(string note)
        {
            return AccidentalSemitonesOf(note) == AmkTextReader.NoAccidentalSemitones ? LetterLength : LetterLength + AccidentalLength;
        }

        // Whether a token is the lettered command with that letter, a number following it.
        public static bool IsLettered(string token, char letter)
        {
            return token.Length > 1 && token[0] == letter && char.IsDigit(token[1]);
        }

        // Whether a token is one of the loop edges the loop pass writes: a label's definition or call, the end of a body, or either end of a subloop.
        public static bool IsLoopEdge(string token)
        {
            return token[0] is '[' or ']' || (token.Length > 1 && token[0] == '(' && char.IsDigit(token[1]));
        }

        // The number of the label a token defines or calls and whether it opens the definition, or null where it does neither.
        public static (int Number, bool IsDefinition)? LabelOf(string token)
        {
            var match = LabelToken.Match(token);
            if (!match.Success)
            {
                return null;
            }

            return (int.Parse(match.Groups[1].Value), match.Groups[2].Value == LabelDefinitionOpening);
        }

        // A label's call as written, with no count behind it.
        public static string LabelCall(int labelNumber)
        {
            return $"({labelNumber})";
        }

        // Whether a token closes a subloop, its count behind it.
        public static bool IsSubloopClose(string token)
        {
            return token.StartsWith(SubloopClose, StringComparison.Ordinal);
        }

        // Whether a token closes a label's definition rather than a subloop.
        public static bool IsLabelDefinitionClose(string token)
        {
            return token[0] == ']' && !IsSubloopClose(token);
        }

        // The ticks a note, rest or tie lasts, or null where the token is none of those.
        public static int? TicksOf(string token)
        {
            if (token.Length < 2)
            {
                return null;
            }

            int lengthStart;
            if (IsNote(token))
            {
                lengthStart = NoteLengthStartOf(token);
            }
            else if (token[0] is 'r' or '^')
            {
                lengthStart = LetterLength;
            }
            else
            {
                return null;
            }

            if (lengthStart >= token.Length)
            {
                return null;
            }

            if (token[lengthStart] == '=')
            {
                return int.TryParse(token.AsSpan(lengthStart + 1), out int rawTicks) ? rawTicks : null;
            }

            int dotCount = 0;
            while (token[^(dotCount + 1)] == '.')
            {
                dotCount++;
            }

            bool hasNoteNumber = int.TryParse(token.AsSpan(lengthStart, token.Length - lengthStart - dotCount), out int noteNumber) && noteNumber > 0;
            return hasNoteNumber ? MmlDurationEncoding.TicksForNoteNumber(noteNumber, dotCount) : null;
        }

        // An estimate of what a command compiles to, in bytes - its own and those it makes the next note write.
        public static int CompiledBytesOf(string token)
        {
            if (IsOctaveMark(token))
            {
                return 0;
            }

            if (IsQ(token))
            {
                return SongDataSize.QByteBytes;
            }

            switch (token[0])
            {
                case '$':
                    return SongDataSize.HexByteBytes;
                case '@':
                    return SongDataSize.InstrumentBytes;
                case 'v':
                case 'w':
                case 't':
                    return token.Contains(',') ? SongDataSize.FadeBytes : SongDataSize.SetBytes;
                case 'y':
                    return SongDataSize.PanBytes;
                case 'n':
                    return SongDataSize.NoiseBytes;
                case 'p':
                    return SongDataSize.VibratoBytes;
                case '(':
                    return SongDataSize.RemoteCallBytes;
            }

            if (CommandNamesInDefinitionOrder.FirstOrDefault(name => name.IsNameOf(token)) is not { } commandName)
            {
                return FallbackCommandBytes;
            }

            if (commandName.Expansion[0] == '(')
            {
                return SongDataSize.RemoteCallBytes;
            }

            return (commandName.Expansion.Count(character => character == '$') + token.Count(character => character == '$')) * SongDataSize.HexByteBytes;
        }

        // The words of a line with every bracket's contents joined back into the word that opens it.
        private static IEnumerable<string> WithBracketsJoined(IEnumerable<string> words)
        {
            var group = new StringBuilder();
            int depth = 0;

            foreach (string word in words)
            {
                if (group.Length > 0)
                {
                    group.Append(' ');
                }

                group.Append(word);
                depth += word.Count(character => character == '(') - word.Count(character => character == ')');

                if (depth > 0)
                {
                    continue;
                }

                yield return group.ToString();
                group.Clear();
                depth = 0;
            }

            if (group.Length > 0)
            {
                yield return group.ToString();
            }
        }
    }
}
