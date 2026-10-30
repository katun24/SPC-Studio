using System.Diagnostics.CodeAnalysis;
using System.Text;
using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// How many bytes of ARAM a song's data takes once AddmusicK has compiled it, counted by compiling the exporter's own text the way AddmusicK's parser does.
public static class SongDataSize
{
    internal const int LoopEndBytes = 1;
    internal const int HexByteBytes = 1;
    internal const int QByteBytes = 1;
    internal const int InstrumentEntryBytes = 6;
    private const int PhraseBytes = 2 + Song.DriverChannelCount * 2;
    private const int LoopJumpBytes = 4;
    private const int EndBytes = 2;

    internal static readonly int SetBytes = AmkCommands.LengthOf(AmkCommands.Tempo);
    internal static readonly int FadeBytes = AmkCommands.LengthOf(AmkCommands.TempoFade);
    internal static readonly int PanBytes = AmkCommands.LengthOf(AmkCommands.Pan);
    internal static readonly int InstrumentBytes = AmkCommands.LengthOf(AmkCommands.Instrument);
    internal static readonly int NoiseBytes = AmkCommands.LengthOf(AmkCommands.Noise);
    internal static readonly int VibratoBytes = AmkCommands.LengthOf(AmkCommands.VibratoOn);
    internal static readonly int RemoteCallBytes = AmkCommands.LengthOf(AmkCommands.RemoteCode);
    internal static readonly int LabelCallBytes = AmkCommands.LengthOf(AmkCommands.Call);
    internal static readonly int SubloopEdgeBytes = AmkCommands.LengthOf(AmkCommands.Subloop);
    private static readonly int ParameterBytes = AmkCommands.LengthOf(AmkCommands.Parameter);
    private static readonly int LocalPrependBytes = ParameterBytes + ParameterBytes;
    private static readonly HashSet<int> WrittenHexCommands = [.. MmlExporter.HexCommandsWritten.Select(commandBytes => commandBytes[0])];

    // The subcommands the export writes, by the command that picks one with its first argument.
    private static readonly Dictionary<int, HashSet<int>> WrittenSubcommands = MmlExporter.HexCommandsWritten
        .Where(commandBytes => commandBytes.Length > 1)
        .GroupBy(commandBytes => commandBytes[0], commandBytes => commandBytes[1])
        .ToDictionary(group => group.Key, group => group.ToHashSet());

    // What the song exports as under those options, compiled and counted, refusing where the export wrote something this count has no reading of.
    public static int BytesOfSong(Song song, MmlExportOptions options)
    {
        return BytesOfMml(MmlExporter.Build(song, options));
    }

    // What AddmusicK compiles MML the exporter could have written into, refusing anything outside the exporter's dialect or anything AddmusicK refuses.
    public static int BytesOfMml(string mml)
    {
        try
        {
            return new Compiler(mml).CountTotalBytes();
        }
        catch (MmlImportException textRefusal)
        {
            throw new NotSupportedException(textRefusal.Message, textRefusal);
        }
    }

    // One pass of AddmusicK's compiler over a file, holding the parser state that decides what each construct costs and the running count per channel.
    private sealed class Compiler : AmkTextReader
    {
        private const int LoopDataSlot = Song.DriverChannelCount;
        private const int FirstCustomInstrument = MmlExporter.FirstCustomInstrument;
        private const int TextPadding = 16;
        private const int ChannelWhereNoneIsNamed = 0;
        private const int MinSubloopCount = 2;
        private const int MinLabelLoopCount = 1;
        private const int ChannelEndBytes = 1;
        private const int NoteByteBytes = 1;
        private const int DurationByteBytes = 1;
        private const string TuningDirectiveRest = "uning[";

        private static readonly int SmwVelocityTableBytes = AmkCommands.LengthOf(AmkCommands.Parameter);
        private static readonly int TempoImmunityBytes = AmkCommands.LengthOf(AmkCommands.Misc);
        private readonly int[] _bytesBySlot = new int[LoopDataSlot + 1];
        private readonly int _firstChannel;
        private readonly HashSet<int> _definedLoopSlots = [];
        private readonly bool[] _owesQByte = [.. Enumerable.Repeat(true, LoopDataSlot + 1)];
        private int _currentSlot;
        private int _channelBeforeLoop;
        private bool _hasChannelStarted;
        private bool _hasDialect;
        private int _octave = SnesPitch.MmlStartOctave;
        private int _instrumentCount;
        private bool _hasIntro;
        private bool _isNoLoop;
        private bool _isInRemoteDefinition;
        private int _pendingLabelSlot = NoNumber;
        private bool _isInSubloop;
        private int _subloopSlot;
        private int _durationTicksInForce = NoNumber;
        private int _hexArgumentsLeft;
        private int _hexCommand;
        private bool _isFirstHexArgument;

        // Prepares the text as the preprocessor leaves it and finds the channel the prepend goes to.
        public Compiler(string mml) : base(MmlPreprocessor.RemoveComments(mml) + new string(' ', TextPadding))
        {
            int earliestChannel = MmlParser.EarliestChannelNamed(_text);
            _firstChannel = earliestChannel == MmlParser.NoChannel ? ChannelWhereNoneIsNamed : earliestChannel;
            _currentSlot = _firstChannel;
            _channelBeforeLoop = _firstChannel;
        }

        // The character at the cursor, or a NUL past the end of the text.
        private char CurrentChar
        {
            get
            {
                return CharAt(0);
            }
        }

        // Whether the cursor is inside a loop body, which compiles into the loop data rather than into a channel.
        private bool IsInLoopBody
        {
            get
            {
                return _currentSlot == LoopDataSlot;
            }
        }

        // Compiles the whole text and adds up the channels, the loop data, the pointer block and the instruments.
        public int CountTotalBytes()
        {
            while (_position < _text.Length)
            {
                ReadNextConstruct();
            }

            RefuseUnclosedConstructs();

            _bytesBySlot[_firstChannel] += LocalPrependBytes;

            int totalBytes = _bytesBySlot[LoopDataSlot];
            for (int channel = 0; channel < Song.DriverChannelCount; channel++)
            {
                if (_bytesBySlot[channel] > 0)
                {
                    totalBytes += _bytesBySlot[channel] + ChannelEndBytes;
                }
            }

            totalBytes += (_hasIntro ? 2 : 1) * PhraseBytes;
            totalBytes += _isNoLoop ? EndBytes : LoopJumpBytes;
            totalBytes += _instrumentCount * InstrumentEntryBytes;

            return totalBytes;
        }

        // Reads one construct at the cursor, after expanding any replacement standing there.
        private void ReadNextConstruct()
        {
            ExpandReplacementAtCursor();

            char character = char.ToLowerInvariant(CurrentChar);

            if (_hexArgumentsLeft != 0 && !AmkWhitespace.IsWhitespace(character) && character != '$')
            {
                Refuse("a hex command is missing some of its arguments");
            }

            switch (character)
            {
                case '#':
                    ReadDirective();
                    break;
                case 't':
                    ReadTempo();
                    break;
                case 'w':
                case 'v':
                    ReadValueOrFade();
                    break;
                case 'y':
                    ReadPan();
                    break;
                case 'q':
                    ReadQuantization();
                    break;
                case 'o':
                    ReadOctave();
                    break;
                case '<':
                    StepOctave(-1);
                    break;
                case '>':
                    StepOctave(1);
                    break;
                case '@':
                    ReadInstrument();
                    break;
                case 'n':
                    ReadNoise();
                    break;
                case 'p':
                    ReadVibrato();
                    break;
                case '/':
                    ReadIntroMarker();
                    break;
                case '(':
                    ReadParenthesized();
                    break;
                case '[':
                    ReadLoopOpen();
                    break;
                case ']':
                    ReadLoopClose();
                    break;
                case '$':
                    ReadHexByte();
                    break;
                case '"':
                    ReadReplacementDirective();
                    break;
                case 'c':
                case 'd':
                case 'e':
                case 'f':
                case 'g':
                case 'a':
                case 'b':
                case 'r':
                case '^':
                    ReadNoteRestOrTie();
                    break;
                default:
                    if (!AmkWhitespace.IsWhitespace(character))
                    {
                        Refuse($"'{CurrentChar}' is not something the export writes");
                    }

                    AdvanceCountingLines();
                    break;
            }
        }

        // Refuses a file that ends inside something still open, has no dialect line or has no musical data.
        private void RefuseUnclosedConstructs()
        {
            if (_hexArgumentsLeft != 0)
            {
                Refuse("the last hex command is missing some of its arguments");
            }

            if (_isInRemoteDefinition)
            {
                Refuse("a remote code definition is never closed");
            }

            if (IsInLoopBody)
            {
                Refuse("a loop is never closed");
            }

            if (_isInSubloop)
            {
                Refuse("a subloop is never closed");
            }

            if (!_hasDialect)
            {
                Refuse($"the song does not say #amk {MmlExporter.AmkVersion}");
            }

            if (!_bytesBySlot.Take(Song.DriverChannelCount).Any(bytes => bytes > 0))
            {
                Refuse("the song has no musical data");
            }
        }

        // The character that far from the cursor, or a NUL past the end of the text.
        private char CharAt(int offset)
        {
            return _position + offset < _text.Length ? _text[_position + offset] : EndOfText;
        }

        // Counts bytes into whichever channel or the loop data the cursor is writing.
        private void AddBytes(int bytes)
        {
            _bytesBySlot[_currentSlot] += bytes;
        }

        // Reads a channel directive, or a header directive where a word follows the '#'.
        private void ReadDirective()
        {
            _position++;

            if (char.IsLetter(CurrentChar))
            {
                ReadSpecialDirective();
                return;
            }

            int channel = ReadInt();
            if (channel < 0 || channel >= Song.DriverChannelCount)
            {
                Refuse("a channel directive is not #0 to #7");
            }

            _currentSlot = channel;
            _owesQByte[LoopDataSlot] = _owesQByte[channel];
            _durationTicksInForce = NoNumber;
            _hasChannelStarted = true;
        }

        // Reads one of the header directives the export writes.
        private void ReadSpecialDirective()
        {
            string word = ReadWord();

            switch (word.ToLowerInvariant())
            {
                case AmkDirectives.Amk:
                    SkipWhitespace();
                    if (ReadDigits() != MmlExporter.AmkVersion)
                    {
                        Refuse($"the song is not written in #amk {MmlExporter.AmkVersion}");
                    }

                    _hasDialect = true;
                    break;

                case AmkDirectives.Path:
                    ReadQuotedString();
                    break;

                case AmkDirectives.Samples:
                    SkipTextBlock(AmkDirectives.Written(AmkDirectives.Samples), "the #samples block holds something that is not a file or a group");
                    break;

                case AmkDirectives.Spc:
                    SkipTextBlock(AmkDirectives.Written(AmkDirectives.Spc), "the #spc block holds something that is not a field");
                    break;

                case AmkDirectives.Instruments:
                    ReadInstruments();
                    break;

                case AmkDirectives.Option:
                    ReadOption();
                    break;

                default:
                    Refuse($"#{word} is not a directive the export writes");
                    break;
            }
        }

        // Reads one of the three options the export writes, each counted into the channel the prepend goes to.
        private void ReadOption()
        {
            if (_hasChannelStarted)
            {
                Refuse("an #option comes after a channel");
            }

            SkipWhitespace();

            switch (ReadWord().ToLowerInvariant())
            {
                case AmkDirectives.SmwVelocityTableOption:
                    if (TrySwitchSmwVelocitiesOn())
                    {
                        AddBytes(SmwVelocityTableBytes);
                    }

                    break;

                case AmkDirectives.TempoImmunityOption:
                    AddBytes(TempoImmunityBytes);
                    break;

                case AmkDirectives.NoLoopOption:
                    _isNoLoop = true;
                    break;

                default:
                    Refuse("an #option is not one the export writes");
                    break;
            }
        }

        // Reads the instrument block, counting its entries: a sample name or a noise clock and five hex bytes each.
        private void ReadInstruments()
        {
            ReadBraceBlock(AmkDirectives.Written(AmkDirectives.Instruments), () =>
            {
                ReadInstrumentEntry();
                _instrumentCount++;
            });
        }

        // Reads one instrument entry's sample or noise clock and its bytes.
        private void ReadInstrumentEntry()
        {
            if (CurrentChar == '"')
            {
                ReadQuotedString();
            }
            else if (CurrentChar == 'n')
            {
                ReadNoiseClock("an instrument's noise clock is not one the chip has");
            }
            else
            {
                Refuse("an instrument entry names neither a sample nor a noise clock");
            }

            for (int byteIndex = 0; byteIndex < InstrumentEntryBytes - 1; byteIndex++)
            {
                SkipWhitespace();
                ReadDollarHex(_ => MissingCharRefusal('$'), _ => Refusal("an instrument entry has too few bytes"));
            }
        }

        // Skips a directive's block of quoted strings and words, which costs the song data nothing, refusing anything else inside it.
        private void SkipTextBlock(string directiveName, string refusalForOtherContent)
        {
            ReadBraceBlock(directiveName, () =>
            {
                char character = CurrentChar;
                if (character == '"')
                {
                    ReadQuotedString();
                }
                else if (character == '#')
                {
                    ReadWord();
                }
                else
                {
                    Refuse(refusalForOtherContent);
                }

                if (_position >= _text.Length)
                {
                    Refuse("a block is never closed");
                }
            });
        }

        // Reads a tempo, a set or a fade, refusing a tuning directive and a tempo of zero.
        private void ReadTempo()
        {
            if (IsSpelledAt(_text, _position + 1, TuningDirectiveRest))
            {
                Refuse("a tuning directive is not something the export writes");
            }

            if (ReadValueOrFade() == 0)
            {
                Refuse("a tempo is zero");
            }
        }

        // Reads the shape t, w and v share - a value, or a duration and a value - counts it and answers the value.
        private int ReadValueOrFade()
        {
            _position++;

            int value = ReadRequiredInt(_ => Refusal("a command is missing its value"));
            int bytes = SetBytes;
            if (TryConsumeComma())
            {
                int durationTicks = value;
                value = ReadRequiredInt(_ => Refusal("a fade is missing its target"));

                if (durationTicks > byte.MaxValue)
                {
                    Refuse("a fade is longer than a byte can say");
                }

                bytes = FadeBytes;
            }

            if (value > byte.MaxValue)
            {
                Refuse("a value does not fit in a byte");
            }

            AddBytes(bytes);
            return value;
        }

        // Reads a pan and its optional surround pair, which rides in the same two bytes.
        private void ReadPan()
        {
            _position++;

            int pan = ReadInt();
            if (!SnesVolume.PanIndexRange.Contains(pan))
            {
                Refuse("a pan is out of range");
            }

            if (TryConsumeAfterWhitespace(','))
            {
                ReadSurroundArgument();
                ExpectCharAfterWhitespace(',');
                ReadSurroundArgument();
            }

            AddBytes(PanBytes);
        }

        // Reads one of a pan's two surround arguments, refusing one out of range.
        private void ReadSurroundArgument()
        {
            if (!MmlParser.SurroundArgumentRange.Contains(ReadInt()))
            {
                Refuse("a pan's surround is out of range");
            }
        }

        // Reads a q, which writes no byte of its own but makes the next note carry one.
        private void ReadQuantization()
        {
            _position++;

            int q = ReadHex();
            if (!MmlParser.QuantizationByteRange.Contains(q))
            {
                Refuse("a q is out of range");
            }

            _owesQByte[IsInLoopBody ? _channelBeforeLoop : _currentSlot] = true;
            _owesQByte[LoopDataSlot] = true;
        }

        // Reads an octave, which is the parser's own state and no byte.
        private void ReadOctave()
        {
            _position++;

            int octave = ReadInt();
            RefuseOctaveOutside(MmlParser.WrittenOctaveRange, octave);
            _octave = octave;
        }

        // Moves the octave one up or down, refusing where it leaves the parser's range.
        private void StepOctave(int octaves)
        {
            _position++;
            _octave += octaves;
            RefuseOctaveOutside(MmlParser.ShiftedOctaveRange, _octave);
        }

        // Refuses an octave outside the range the parser holds it to there.
        private void RefuseOctaveOutside(ValueRange octaveRange, int octave)
        {
            if (!octaveRange.Contains(octave))
            {
                Refuse("an octave is out of range");
            }
        }

        // Reads an instrument change, which is written whether or not the channel is already under it.
        private void ReadInstrument()
        {
            _position++;

            if (CurrentChar == '@')
            {
                Refuse("a direct instrument is not something the export writes");
            }

            int number = ReadInt();
            if (number < FirstCustomInstrument || number > byte.MaxValue)
            {
                Refuse("an instrument is not one of the song's own");
            }

            if (number - FirstCustomInstrument >= _instrumentCount)
            {
                Refuse("an instrument is used before it is defined");
            }

            AddBytes(InstrumentBytes);
        }

        // Reads a noise clock change.
        private void ReadNoise()
        {
            ReadNoiseClock("a noise clock is out of range");
            AddBytes(NoiseBytes);
        }

        // Reads an 'n' and the hex clock behind it, refusing a clock the chip does not have.
        private void ReadNoiseClock(string refusalForOtherClock)
        {
            _position++;

            if (!SnesNoise.ClockRange.Contains(ReadHex()))
            {
                Refuse(refusalForOtherClock);
            }
        }

        // Reads a vibrato in either of its forms, which compile to the same bytes.
        private void ReadVibrato()
        {
            _position++;

            ReadVibratoArgument();
            ExpectComma();
            ReadVibratoArgument();
            if (TryConsumeComma())
            {
                ReadVibratoArgument();
            }

            AddBytes(VibratoBytes);
        }

        // Reads one of a vibrato's arguments, refusing one that is missing or does not fit in a byte.
        private void ReadVibratoArgument()
        {
            if (ReadInt() is < 0 or > byte.MaxValue)
            {
                Refuse("a vibrato is missing an argument");
            }
        }

        // Reads the loop marker, which writes no byte but adds a phrase to the pointer block and makes the next note state its duration again.
        private void ReadIntroMarker()
        {
            if (IsInLoopBody || _isInSubloop)
            {
                Refuse("a '/' is inside a loop");
            }

            _position++;
            _hasIntro = true;
            _durationTicksInForce = NoNumber;
        }

        // Reads a label loop, or one of the remote code forms: a definition outside every channel, or a call or a reset inside one.
        private void ReadParenthesized()
        {
            if (CharAt(1) is '"' or '@')
            {
                Refuse("a sample load is not something the export writes");
            }

            _position++;

            if (!TryConsume('!'))
            {
                ReadLabelLoop();
                return;
            }

            SkipWhitespace();

            if (!_hasChannelStarted)
            {
                ReadRemoteDefinitionOpening();
                return;
            }

            if (TryConsume('!'))
            {
                ReadRequiredSignedInt();
                ExpectCharAfterWhitespace(')');
                AddBytes(RemoteCallBytes);
                return;
            }

            ReadRemoteCall();
        }

        // Reads a remote code definition's number up to its body.
        private void ReadRemoteDefinitionOpening()
        {
            int number = ReadRequiredInt(_ => Refusal("a remote code definition has no number"));

            if (!_definedLoopSlots.Add(number))
            {
                Refuse("a remote code definition takes a slot already defined");
            }

            ExpectCharAfterWhitespace(')');
            if (CurrentChar != '[')
            {
                Refuse("a remote code definition is missing its body");
            }

            _isInRemoteDefinition = true;
        }

        // Reads a remote code call's number, type and, for the two timed types, its third argument.
        private void ReadRemoteCall()
        {
            ReadRequiredInt(_ => Refusal("a remote code call has no number"));
            ExpectComma();

            int triggerType = ReadRequiredSignedInt();
            SkipWhitespace();

            if (triggerType is 1 or 2)
            {
                ExpectChar(',');
                if (TryConsumeAfterWhitespace('$'))
                {
                    ReadRequiredHex(_ => Refusal("a remote code call's third argument is not hex"));
                }
                else if (ReadNoteLength(ReadInt()) > MmlParser.MaxRemoteCallLengthTicks)
                {
                    Refuse("a remote code call's note length is too long");
                }

                SkipWhitespace();
            }

            ExpectChar(')');
            if (CurrentChar == '[')
            {
                Refuse("remote code is defined within a channel");
            }

            AddBytes(RemoteCallBytes);
        }

        // Reads a label loop inside a channel: in front of a '[' it defines the label, and on its own, with a count or without, it calls it.
        private void ReadLabelLoop()
        {
            RefuseNestedLoop();

            if (!_hasChannelStarted)
            {
                Refuse("a label loop is outside every channel");
            }

            int number = ReadRequiredInt(_ => Refusal("a label loop has no number"));

            int slot = number + 1;
            ExpectChar(')');
            OweQByteAtLoopEdge();

            if (CurrentChar == '[')
            {
                if (_definedLoopSlots.Contains(slot))
                {
                    Refuse("a label loop takes a slot already defined");
                }

                _pendingLabelSlot = slot;
                return;
            }

            if (!_definedLoopSlots.Contains(slot))
            {
                Refuse("a label loop is called before it is defined");
            }

            ReadLabelLoopCount();
            AddBytes(LabelCallBytes);
        }

        // Reads the count behind a label loop, one where none is written, refusing one out of range.
        private void ReadLabelLoopCount()
        {
            int count = ReadInt();
            if (count == NoNumber)
            {
                count = MinLabelLoopCount;
            }

            if (count is < MinLabelLoopCount or > byte.MaxValue)
            {
                Refuse("a label loop's count is out of range");
            }
        }

        // Reads a '[': a subloop's opening where a second one follows, or the body of a remote code definition or a label, compiled into the loop data.
        private void ReadLoopOpen()
        {
            _position++;
            OweQByteAtLoopEdge();

            if (TryConsume('['))
            {
                if (_isInSubloop)
                {
                    Refuse("subloops are nested");
                }

                if (_pendingLabelSlot >= 0)
                {
                    Refuse("a label loop defines a subloop");
                }

                _isInSubloop = true;
                _subloopSlot = _currentSlot;
                AddBytes(SubloopEdgeBytes);
                return;
            }

            RefuseNestedLoop();

            if (!_isInRemoteDefinition && _pendingLabelSlot < 0)
            {
                Refuse("a loop is not something the export writes");
            }

            if (_pendingLabelSlot >= 0)
            {
                _definedLoopSlots.Add(_pendingLabelSlot);
            }

            _pendingLabelSlot = NoNumber;
            _channelBeforeLoop = _currentSlot;
            _currentSlot = LoopDataSlot;
        }

        // Reads a ']': a subloop's end and count where a second one follows, or the end of a body, with a label's call behind it.
        private void ReadLoopClose()
        {
            _position++;
            OweQByteAtLoopEdge();

            if (TryConsume(']'))
            {
                int repeats = ReadInt();
                if (repeats is < MinSubloopCount or > byte.MaxValue)
                {
                    Refuse("a subloop's count is out of range");
                }

                if (!_isInSubloop)
                {
                    Refuse("a subloop end is outside a subloop");
                }

                _isInSubloop = false;
                AddBytes(SubloopEdgeBytes);
                return;
            }

            if (!IsInLoopBody)
            {
                Refuse("a loop end is outside a loop");
            }

            if (_isInSubloop && _subloopSlot == LoopDataSlot)
            {
                Refuse("a subloop is not closed inside its loop");
            }

            if (_isInRemoteDefinition)
            {
                if (ReadInt() >= 0)
                {
                    Refuse("a remote code definition repeats");
                }

                EndLoopBody();
                _isInRemoteDefinition = false;
                return;
            }

            ReadLabelLoopCount();
            EndLoopBody();
            AddBytes(LabelCallBytes);
        }

        // Refuses a loop opened inside a loop body.
        private void RefuseNestedLoop()
        {
            if (IsInLoopBody)
            {
                Refuse("loops are nested");
            }
        }

        // Ends a loop body's bytes in the loop data and goes back to the channel it was opened in.
        private void EndLoopBody()
        {
            AddBytes(LoopEndBytes);
            _currentSlot = _channelBeforeLoop;
        }

        // Makes the next note on both sides of a loop edge carry its q byte and state its duration again.
        private void OweQByteAtLoopEdge()
        {
            if (!IsInLoopBody)
            {
                _owesQByte[_currentSlot] = true;
            }

            _owesQByte[LoopDataSlot] = true;
            _durationTicksInForce = NoNumber;
        }

        // Reads one byte of a raw hex command, counting the arguments down against the command's length.
        private void ReadHexByte()
        {
            _position++;

            int value = ReadRequiredHex(_ => Refusal("a '$' is not followed by a hex byte"));

            if (_hexArgumentsLeft == 0)
            {
                if (!WrittenHexCommands.Contains(value))
                {
                    Refuse($"{AmkCommands.HexSpelling(value)} is not a command the export writes");
                }

                _hexCommand = value;
                _hexArgumentsLeft = AmkCommands.LengthOf(value) - 1;
                _isFirstHexArgument = true;
            }
            else
            {
                if (_isFirstHexArgument && WrittenSubcommands.TryGetValue(_hexCommand, out var subcommands) && !subcommands.Contains(value))
                {
                    Refuse($"{AmkCommands.HexSpelling(_hexCommand, value)} is not a command the export writes");
                }

                _isFirstHexArgument = false;
                _hexArgumentsLeft--;
            }

            AddBytes(HexByteBytes);
        }

        // Reads a note, a rest or a tie with every '^' behind it - and every 'r' behind a rest - merged into one run, and counts the run.
        private void ReadNoteRestOrTie()
        {
            char letter = char.ToLowerInvariant(CurrentChar);
            _position++;

            if (_isInRemoteDefinition)
            {
                Refuse("a remote code definition holds a note");
            }

            if (!_hasChannelStarted)
            {
                Refuse("a note is outside every channel");
            }

            bool isRest = letter == 'r';

            if (letter is not ('r' or '^'))
            {
                ReadNoteValue(letter);
            }

            AddRunBytes(ReadMergedRunTicks(isRest));
        }

        // Reads the sharp or flat behind a note letter, refusing a note outside the note bytes.
        private void ReadNoteValue(char letter)
        {
            int noteValue = SnesPitch.NoteValueFor(_octave, SnesPitch.SemitoneForLetter(letter));

            noteValue += ReadAccidentalSemitones();

            if (!SnesPitch.NoteValueRange.Contains(noteValue))
            {
                Refuse("a note is outside the note bytes");
            }
        }

        // The ticks of one merged run, stopping in front of a piece a bend straight behind it puts in a byte of its own.
        private int ReadMergedRunTicks(bool isRest)
        {
            int runTicks = 0;
            bool canRewind = false;

            do
            {
                int ticksBeforePiece = runTicks;
                int pieceStart = _position;

                if (runTicks != 0 && (CurrentChar == '^' || (isRest && CurrentChar == 'r')))
                {
                    _position++;
                }

                runTicks += ReadNoteLength(ReadInt());
                SkipWhitespace();

                if (canRewind && IsBendAhead())
                {
                    _position = pieceStart;
                    return ticksBeforePiece;
                }

                canRewind = true;
            }
            while (CurrentChar == '^' || (isRest && CurrentChar == 'r'));

            return runTicks;
        }

        // Counts the bytes one merged run costs: a duration byte where the length is not the one in force, the q byte where one is owed, and the note,
        // tie or rest byte - or the split AddmusicK makes of a run too long for one byte.
        private void AddRunBytes(int runTicks)
        {
            if (runTicks <= 0)
            {
                return;
            }

            int slot = _currentSlot;

            if (runTicks > MmlDurationEncoding.MaxTicksWithoutAutoTies)
            {
                var pieceTicks = MmlDurationEncoding.SplitRunIntoTiedPieces(runTicks);
                int lastPieceTicks = pieceTicks[^1];

                int lastDurationBytes = lastPieceTicks != MmlDurationEncoding.TicksPerAutoTiedPiece ? DurationByteBytes : 0;
                AddBytes(DurationByteBytes + TakeOwedQByte(slot) + NoteByteBytes + (pieceTicks.Count - 1) + lastDurationBytes);
                _durationTicksInForce = lastPieceTicks;
                return;
            }

            int durationBytes = runTicks != _durationTicksInForce || _owesQByte[slot] ? DurationByteBytes : 0;
            _durationTicksInForce = runTicks;

            AddBytes(durationBytes + TakeOwedQByte(slot) + NoteByteBytes);
        }

        // The q byte a note owes, if it owes one, which it then no longer does.
        private int TakeOwedQByte(int slot)
        {
            if (!_owesQByte[slot])
            {
                return 0;
            }

            _owesQByte[slot] = false;
            _owesQByte[LoopDataSlot] = false;
            return QByteBytes;
        }

        // The ticks of a length already read as a note number - or of a '=' tick count, or the default where neither is written - with any dots behind it.
        private int ReadNoteLength(int noteNumber)
        {
            int ticks;
            if (noteNumber == NoNumber && TryConsume('='))
            {
                ticks = ReadRequiredInt(_ => Refusal("a '=' is not followed by a tick count"));
            }
            else if (noteNumber < 1 || noteNumber > SnesTiming.TicksPerWholeNote)
            {
                ticks = MmlParser.DefaultNoteLengthTicks;
            }
            else
            {
                ticks = SnesTiming.TicksPerWholeNote / noteNumber;
            }

            return ReadDotsOnto(ticks);
        }

        // Whether a bend command stands at the cursor, in either of the two cases AddmusicK checks.
        private bool IsBendAhead()
        {
            return MmlPitchBend.IsSpelledAt(_text, _position);
        }

        // A number with an optional minus sign, a replacement expanded once ahead of the sign, refusing where there is no number.
        private int ReadRequiredSignedInt()
        {
            return ReadRequiredIntWithNegative(_ => Refusal("a number is missing"));
        }

        // Steps over a character that has to be at the cursor, refusing where it is not.
        private void ExpectChar(char expected)
        {
            if (!TryConsume(expected))
            {
                throw MissingCharRefusal(expected);
            }
        }

        // Steps over the whitespace at the cursor and a character that has to follow it, refusing where it does not.
        private void ExpectCharAfterWhitespace(char expected)
        {
            if (!TryConsumeAfterWhitespace(expected))
            {
                throw MissingCharRefusal(expected);
            }
        }

        // Steps over a comma that has to follow, with the whitespace on both sides of it, refusing where it does not.
        private void ExpectComma()
        {
            if (!TryConsumeComma())
            {
                throw MissingCharRefusal(',');
            }
        }

        // Refuses with the reason and the line the cursor is on.
        [DoesNotReturn]
        private void Refuse(string reason)
        {
            throw Refusal(reason);
        }

        // The refusal of a reason at the line the cursor is on.
        private NotSupportedException Refusal(string reason)
        {
            return new NotSupportedException($"Line {_line}: {reason}.");
        }

        // The refusal of a character the text has to hold at the cursor and does not.
        private NotSupportedException MissingCharRefusal(char expected)
        {
            return Refusal($"a '{expected}' is missing");
        }
    }
}
