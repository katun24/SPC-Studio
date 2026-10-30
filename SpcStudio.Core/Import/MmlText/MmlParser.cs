using SpcStudio.Model;

namespace SpcStudio.Import;

// Reads AddmusicK MML into a parsed file, refusing what AddmusicK refuses, by name and with a line number.
public static partial class MmlParser
{
    public const int DefaultNoteLengthTicks = SnesTiming.TicksPerEighthNote;
    internal const int NoChannel = -1;
    internal const int MaxRemoteCallLengthTicks = 0x100;
    private const int NoNote = -1;
    private const int NoLine = -1;
    private const int NoTick = -1;
    private const int NoDurationByte = -1;

    internal static readonly ValueRange WrittenOctaveRange = new(0, 6);
    internal static readonly ValueRange ShiftedOctaveRange = new(-1, 7);
    internal static readonly ValueRange QuantizationByteRange = new(0x01, 0x7F);
    internal static readonly ValueRange SurroundArgumentRange = new(0, 2);

    // Reads a whole file, under whichever read-ahead the song's hot patches leave the driver with.
    public static MmlFile Parse(string text)
    {
        int amkVersion = MmlPreprocessor.ReadAmkVersion(text);
        string prepared = MmlPreprocessor.RemoveComments(MmlPreprocessor.ResolveDirectives(text));
        return ReadAheadPatch.ReadUnderPatch(doesReadIntoLoops => new Parser(prepared, amkVersion, doesReadIntoLoops).ReadFile());
    }

    // Whether a text is a song: outside its comments it says which AddmusicK it is written for and names a channel.
    internal static bool IsSongText(string text)
    {
        string textOutsideComments = MmlPreprocessor.RemoveComments(text);
        return MmlPreprocessor.WritesDialectLine(textOutsideComments) && EarliestChannelNamed(textOutsideComments) != NoChannel;
    }

    // The lowest-numbered channel marker anywhere in the text, which is the channel commands above the first header go to, or none.
    internal static int EarliestChannelNamed(string text)
    {
        for (int number = 0; number < Song.DriverChannelCount; number++)
        {
            if (text.Contains($"#{number}"))
            {
                return number;
            }
        }

        return NoChannel;
    }

    // AddmusicK's parser state over one file: the octave, the note each channel is still building, and where each channel's clock has got to.
    private sealed partial class Parser(string text, int amkVersion, bool doesReadIntoLoops) : AmkTextReader(text)
    {
        private readonly bool _doesReadIntoLoops = doesReadIntoLoops;
        private readonly int _amkVersion = amkVersion;
        private readonly List<MmlSampleEntry> _samples = [];
        private readonly List<MmlInstrument> _instruments = [];
        private readonly ChannelState[] _channels = [.. Enumerable.Range(0, Song.DriverChannelCount).Select(_ => new ChannelState())];
        private readonly List<int> _channelOrder = [];
        private readonly Dictionary<int, LoopBody> _loopBodiesByLabel = [];
        private readonly Dictionary<int, IReadOnlyList<MmlEvent>> _remoteCodeBodiesByLabel = [];
        private string _samplePath = "";
        private bool _doesLoop = true;
        private bool _hasTempoImmunity;
        private SpcTags _spcTags = SpcTags.Empty;
        private int _octave = SnesPitch.MmlStartOctave;
        private int _defaultLengthTicks = DefaultNoteLengthTicks;
        private int _hTranspose;
        private bool _hasHTranspose;
        private bool _isInTriplet;
        private int _tripletOpenLine;
        private bool _isChannelDefined;
        private int _remoteDefinitionLabel;

        // The loop bodies being read, innermost last.
        private readonly Stack<OpenLoopBody> _openLoopBodies = new();

        // The channel being written to, starting at the one commands above the first header go to.
        private int _currentChannel = EarliestChannelNamed(text);

        // The label a loop opened straight after it would define.
        private int? _pendingLabel;

        // The channel a remote code definition's commands are read into while one is open.
        private ChannelState? _remoteDefinitionChannel;

        // The most recent [ ] body, which a * repeats.
        private LoopBody? _previousLoopBody;

        // Reads the whole text and returns the file it holds.
        public MmlFile ReadFile()
        {
            if (_currentChannel != NoChannel)
            {
                _channels[_currentChannel].Events.Add(new MmlVelocityTable(0, false, 0));
            }

            while (_position < _text.Length)
            {
                ExpandReplacementAtCursor();
                if (_position >= _text.Length)
                {
                    break;
                }

                ReadConstruct();
            }

            RefuseUnclosedConstructs();
            RefuseUnreadableChannels();
            return new MmlFile(
                _samplePath,
                _samples,
                _instruments,
                _doesLoop,
                _hasTempoImmunity,
                [.. _channelOrder.Select(number => ChannelRead(number, _channels[number]))],
                _remoteCodeBodiesByLabel)
            {
                SpcTags = _spcTags,
                DoesReadIntoLoops = _doesReadIntoLoops,
            };
        }

        // Reads the construct the character at the cursor opens, by the character it starts with whatever its case.
        private void ReadConstruct()
        {
            char character = _text[_position];
            if (AmkWhitespace.IsWhitespace(character))
            {
                AdvanceCountingLines();
                return;
            }

            char lower = char.ToLowerInvariant(character);
            switch (lower)
            {
                case '#': ReadDirective(); break;
                case 'o': ReadOctave(); break;
                case 'l': ReadDefaultLength(); break;
                case '<': case '>': ShiftOctave(lower); break;
                case '@': ReadInstrumentCommand(); break;
                case 'h': ReadHTranspose(); break;
                case '$': ReadHexCommand(); break;
                case 't': case 'w': case 'v': case 'y': ReadLetteredCommand(lower); break;
                case 'p': ReadVibratoLetter(); break;
                case '&': ReadPitchSlideMark(); break;
                case 'q': ReadQuantization(); break;
                case 'n': ReadNoiseLetter(); break;
                case '/': ReadLoopMark(); break;
                case 'r': case '^': ReadRestOrTie(lower); break;
                case '[': ReadLoopOpen(); break;
                case ']': ReadLoopClose(); break;
                case '(': ReadParenthesized(); break;
                case '*': ReadStarLoop(); break;
                case '{': OpenTriplet(); break;
                case '}': CloseTriplet(); break;
                case '"': ReadReplacementDirective(); break;
                case '?': ReadQuestionMarkDirective(); break;
                case '|': _position++; break;
                default:
                    if (SnesPitch.SemitoneForLetter(lower) >= 0)
                    {
                        ReadNote(lower);
                        break;
                    }

                    SkipStrayCharacter();
                    break;
            }
        }

        // Refuses a loop, a triplet, a pitch bend or a remote code definition still open at the end of the file.
        private void RefuseUnclosedConstructs()
        {
            if (_openLoopBodies.Count > 0)
            {
                var innermost = _openLoopBodies.Peek();
                throw innermost.IsSubloop
                    ? MmlImportException.SubloopUnclosed(innermost.IsOpenedByHex, innermost.OpeningLine)
                    : MmlImportException.LoopUnclosed(innermost.OpeningLine);
            }

            if (_isInTriplet)
            {
                throw MmlImportException.TripletUnclosed(_tripletOpenLine);
            }

            RequirePitchBendResolved();
            if (_remoteDefinitionChannel is not null)
            {
                throw MmlImportException.RemoteDefinitionUnclosed(_remoteDefinitionLabel);
            }
        }

        // Refuses a file with no channel, commands in a channel no header writes, a channel ending where the driver hangs, and a tie opening the loop that a note could sound into.
        private void RefuseUnreadableChannels()
        {
            if (_channelOrder.Count == 0)
            {
                throw MmlImportException.NoChannels();
            }

            if (_currentChannel != NoChannel && !_channels[_currentChannel].HasChannelDirective && _channels[_currentChannel].Events.Count > 0)
            {
                throw MmlImportException.CommandsAboveUnwrittenChannel(_currentChannel);
            }

            // The first channel ending on a loop the driver hangs on, where the read-ahead looks inside loops.
            var hungChannel = _doesReadIntoLoops ? _channelOrder.Select(number => _channels[number]).FirstOrDefault(channel => channel.HangingCallLine >= 0) : null;
            if (hungChannel is not null)
            {
                throw MmlImportException.EmptyLoopEndsChannel(hungChannel.HangingCallLine);
            }

            foreach (int number in _channelOrder)
            {
                RefuseHeldOpeningTie(_channels[number]);
            }
        }

        // One channel as the file carries it once the whole text has been read.
        private MmlChannel ChannelRead(int number, ChannelState channel)
        {
            return new MmlChannel(number, channel.Events, channel.EndTick)
            {
                CutTies = CutTiesWithRestsMarked(channel),
                LoopEdges = _doesReadIntoLoops ? LoopEdgesReadingIntoLoops(channel) : LoopEdgesWithStreamEnd(channel),
                RestBytes = [.. channel.RestBytes.Distinct().Order()],
                RestTies = [.. channel.RestTies.Distinct().Order()],
            };
        }

        // Refuses a tie opening the loop, read as a rest for want of a note, where legato, remote code or an arpeggio could keep the channel's last note sounding into it.
        private static void RefuseHeldOpeningTie(ChannelState channel)
        {
            if (channel.OpeningTieAfterLoopLine < 0)
            {
                return;
            }

            bool isLegatoOnAtEnd = LegatoBits.IsBitSetBefore(channel.Events, channel.EndTick);
            if (isLegatoOnAtEnd || channel.Events.Any(channelEvent => channelEvent is MmlRemoteCall or MmlArpeggio))
            {
                throw MmlImportException.OpeningTieAfterSoundingNote(channel.OpeningTieAfterLoopLine);
            }
        }

        // A channel's loop edges under the read-ahead that looks inside loops, each with what it finds played next.
        private static List<MmlLoopEdge> LoopEdgesReadingIntoLoops(ChannelState channel)
        {
            var notes = channel.Events.OfType<MmlNote>().ToList();
            var restByteTicks = channel.RestBytes.ToHashSet();

            // What the read-ahead finds at a tick: the channel's end, a note starting there, a tie inside a note, a rest byte, or a tie behind a rest.
            ReadAheadStop StopAt(int tick)
            {
                if (tick >= channel.EndTick)
                {
                    return ReadAheadStop.KeyOff;
                }

                int firstAtOrAfter = notes.FindIndex(note => note.Tick >= tick);
                if (firstAtOrAfter >= 0 && notes[firstAtOrAfter].Tick == tick)
                {
                    return ReadAheadStop.Note;
                }

                var noteInFront = firstAtOrAfter < 0 ? notes.LastOrDefault() : firstAtOrAfter > 0 ? notes[firstAtOrAfter - 1] : null;
                if (noteInFront is not null && noteInFront.EndTick > tick)
                {
                    return ReadAheadStop.Tie;
                }

                return restByteTicks.Contains(tick) ? ReadAheadStop.Rest : ReadAheadStop.Tie;
            }

            return [.. LoopEdgesWithStreamEnd(channel).Select(edge => edge with { ReadAheadStop = StopAt(edge.Tick) }).Distinct()];
        }

        // A channel's cut ties with whether a rest byte was fetched since the note in front of each filled in.
        private static List<MmlCutTie> CutTiesWithRestsMarked(ChannelState channel)
        {
            var ties = channel.CutTies.OrderBy(tie => tie.Tick).ToList();
            var notes = channel.Events.OfType<MmlNote>().ToList();
            for (int index = 0; index < ties.Count; index++)
            {
                var tie = ties[index];
                var noteInFront = notes.LastOrDefault(note => note.Tick < tie.Tick);

                // The tick the stretch in front of the tie begins on: the end of the note in front, or the channel's start.
                int stretchStart = noteInFront is null ? 0 : noteInFront.EndTick;

                // How far the ties in front, each clipped to the stretch and laid end to end, reach without a gap.
                int reachedTick = stretchStart;
                bool hasGap = false;
                for (int earlier = 0; earlier < index && !hasGap; earlier++)
                {
                    int start = Math.Max(stretchStart, ties[earlier].Tick);
                    int end = Math.Min(tie.Tick, ties[earlier].EndTick);
                    if (end <= start)
                    {
                        continue;
                    }

                    hasGap = start > reachedTick;
                    reachedTick = Math.Max(reachedTick, end);
                }

                ties[index] = tie with { IsAfterRest = hasGap || tie.Tick > reachedTick };
            }

            return ties;
        }

        // Steps over a character the language has no case for, which AddmusicK deletes from the song.
        private void SkipStrayCharacter()
        {
            _position++;
        }

        // The channel being written to: an open remote code definition's, or the current channel's.
        private ChannelState CurrentChannel(int line)
        {
            if (_remoteDefinitionChannel is not null)
            {
                return _remoteDefinitionChannel;
            }

            if (_currentChannel == NoChannel)
            {
                throw MmlImportException.MusicBeforeChannel(line);
            }

            return _channels[_currentChannel];
        }

        // Where an event goes: the loop body being read, or the channel itself.
        private List<MmlEvent> EventsBeingWritten(int line)
        {
            return _openLoopBodies.Count > 0 ? _openLoopBodies.Peek().Events : CurrentChannel(line).Events;
        }
    }
}
