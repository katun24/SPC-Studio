using System.Collections.Immutable;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int HoldingRest = -2;
        private const double TripletLengthNumerator = 2.0;
        private const double TripletLengthDenominator = 3.0;

        private static readonly ValueRange NoteLengthNumberRange = new(1, SnesTiming.TicksPerWholeNote);

        // Whether what is being read goes into the channel's own stream rather than into a [ ] body compiled apart from it.
        private bool IsInChannelStream
        {
            get
            {
                return !_openLoopBodies.Any(openBody => !openBody.IsSubloop);
            }
        }

        // Refuses note data inside a remote code definition or above the first channel directive.
        private void RefuseNoteDataOutsideChannel(int line)
        {
            if (_remoteDefinitionChannel is not null)
            {
                throw MmlImportException.NoteDataInRemoteDefinition(line);
            }

            if (!_isChannelDefined)
            {
                throw MmlImportException.NoteDataBeforeChannel(line);
            }
        }

        // Reads a note letter: its pitch, its duration and the run of bytes it opens, or the target of a $DD waiting for one.
        private void ReadNote(char letter)
        {
            int line = _line;
            _position++;
            RefuseNoteDataOutsideChannel(line);

            int accidentalSemitones = ReadAccidentalSemitones();
            var channel = CurrentChannel(line);

            // The note value as written, moved by the transpose in force.
            int noteValue = SnesPitch.NoteValueFor(_octave, SnesPitch.SemitoneForLetter(letter)) + accidentalSemitones + NoteShiftSemitones(channel);
            RefuseNoteOutOfRange(letter, noteValue, channel);
            if (channel.ForcedNoteValue >= 0)
            {
                noteValue = channel.ForcedNoteValue;
                channel.ForcedNoteValue = MmlBuiltins.NoForcedNoteValue;
            }

            if (channel.BendAwaitingTarget is { } pendingBend)
            {
                channel.BendAwaitingTarget = null;
                AddBend(line, EventsBeingWritten(line), pendingBend.Delay, pendingBend.Length, noteValue);
                return;
            }

            int lengthTicks = ReadDurationTicks();
            var eventsBeingWritten = EventsBeingWritten(line);
            ResolveWaitingLoopEdges(line, ReadAheadStop.Note);
            MarkFetched();
            channel.MayVoiceBeKeyedOff = false;
            channel.IsLastCutTieOpen = false;
            channel.OpenRestByte = RestByteKind.None;
            CloseBodyOpeningTies();
            if (channel.IsPitchSlidePending)
            {
                AddPitchSlideBend(line, eventsBeingWritten, noteValue);
            }

            SetCurrentHeldNoteIndex(line, eventsBeingWritten.Count);
            eventsBeingWritten.Add(new MmlNote(channel.EndTick, noteValue, lengthTicks, channel.Quantization, line) { Pieces = [.. MmlDurationEncoding.SplitRunIntoTiedPieces(lengthTicks)] });
            StartDurationRun(channel, lengthTicks);
            channel.EndTick += lengthTicks;
        }

        // Refuses a note value, moved by the transpose in force, outside the notes a note byte can name.
        private void RefuseNoteOutOfRange(char letter, int noteValue, ChannelState channel)
        {
            if (noteValue < SnesPitch.MinNoteValue)
            {
                throw MmlImportException.NoteBelowRange(letter, _octave, NoteShiftSemitones(channel), _hasHTranspose, _line);
            }

            if (noteValue > SnesPitch.MaxNoteValue)
            {
                throw MmlImportException.NoteAboveRange(letter, _octave, NoteShiftSemitones(channel), _hasHTranspose, _line);
            }
        }

        // How far a note is moved before it is range-checked: h where one has been written, and the built-in instrument's own transpose otherwise.
        private int NoteShiftSemitones(ChannelState channel)
        {
            return _hasHTranspose ? _hTranspose : -channel.BuiltinTranspose;
        }

        // Reads an r or a ^: a rest, or a tie.
        private void ReadRestOrTie(char letter)
        {
            int line = _line;
            _position++;
            RefuseNoteDataOutsideChannel(line);
            int lengthTicks = ReadDurationTicks();
            var channel = CurrentChannel(line);
            ResolveWaitingLoopEdges(line, letter == '^' ? ReadAheadStop.Tie : ReadAheadStop.Rest);
            MarkFetched();
            if (letter == 'r')
            {
                AddRest(channel, line, lengthTicks);
                return;
            }

            AddTie(channel, line, lengthTicks);
        }

        // A rest at the channel's clock, which ends the note in front and the ties a body opens with.
        private void AddRest(ChannelState channel, int line, int lengthTicks)
        {
            RecordRestByte(channel, RestByteKind.Rest, lengthTicks, channel.RestBytes);
            channel.IsLastCutTieOpen = false;
            CloseBodyOpeningTies();
            SetCurrentHeldNoteIndex(line, HoldingRest);
            channel.EndTick += lengthTicks;
        }

        // A tie at the channel's clock: the rest in front carried on, a rest of its own where nothing sounds in front of it, or the note in front lengthened.
        private void AddTie(ChannelState channel, int line, int lengthTicks)
        {
            int lastNoteIndex = CurrentHeldNoteIndex(line);
            if (lastNoteIndex == HoldingRest)
            {
                AddTieBehindRest(channel, lengthTicks);
                return;
            }

            if (lastNoteIndex < 0 && _openLoopBodies.Count > 0 && _openLoopBodies.Peek() is { } openBody && !openBody.Events.OfType<MmlNote>().Any())
            {
                if (openBody.IsSubloop && !IsInChannelStream)
                {
                    throw MmlImportException.TieOpeningSubloopInsideLoop(_line);
                }

                AddTieOpeningBody(channel, openBody, line, lengthTicks);
                return;
            }

            if (channel.MayVoiceBeKeyedOff)
            {
                ListTieAfterLoop(channel, line, lengthTicks);
                if (lastNoteIndex < 0)
                {
                    channel.IsLastCutTieOpen = true;
                    AddTieAsRest(channel, line, lengthTicks, channel.RestBytes);
                    return;
                }
            }

            if (lastNoteIndex < 0 && _openLoopBodies.Count == 0 && _remoteDefinitionChannel is null && !channel.Events.OfType<MmlNote>().Any())
            {
                AddTieBeforeFirstNote(channel, line, lengthTicks);
                return;
            }

            channel.OpenRestByte = RestByteKind.None;
            if (lastNoteIndex < 0)
            {
                throw MmlImportException.TieWithoutNote(_line);
            }

            LengthenNoteByTie(channel, line, lengthTicks, lastNoteIndex);
        }

        // A tie behind a rest, which carries the rest on: the clock, the ties a body opens with and an open cut tie all grow by it.
        private void AddTieBehindRest(ChannelState channel, int lengthTicks)
        {
            RecordRestByte(channel, RestByteKind.Tie, lengthTicks, channel.RestTies);
            if (_openLoopBodies.Count > 0 && _openLoopBodies.Peek() is { IsOpeningTieRunOpen: true } openBody)
            {
                openBody.OpeningTieTicks += lengthTicks;
                if (openBody.OpeningTieQuantization != channel.Quantization)
                {
                    openBody.OpeningTieQuantization = null;
                }
            }

            if (channel.IsLastCutTieOpen)
            {
                var openCutTie = channel.CutTies[^1];
                channel.CutTies[^1] = openCutTie with
                {
                    LengthTicks = openCutTie.LengthTicks + lengthTicks,
                    Quantization = openCutTie.Quantization == channel.Quantization ? openCutTie.Quantization : null,
                };
            }

            channel.EndTick += lengthTicks;
        }

        // A tie as the first sound in a loop body, read as a rest that opens the body's run of opening ties.
        private void AddTieOpeningBody(ChannelState channel, OpenLoopBody openBody, int line, int lengthTicks)
        {
            openBody.OpeningTieLine = line;
            openBody.OpeningTieTicks = lengthTicks;
            openBody.IsOpeningTieRunOpen = true;
            openBody.OpeningTieQuantization = channel.Quantization;
            AddTieAsRest(channel, line, lengthTicks, channel.RestBytes);
        }

        // Lists a tie read where a loop in front may have keyed the voice off, for the whole song to decide whether it is a rest.
        private static void ListTieAfterLoop(ChannelState channel, int line, int lengthTicks)
        {
            channel.CutTies.Add(new MmlCutTie(channel.EndTick, lengthTicks, false, line, channel.Quantization));
        }

        // A tie before the channel's first note, read as a rest, its line kept where it stands after the loop point.
        private void AddTieBeforeFirstNote(ChannelState channel, int line, int lengthTicks)
        {
            if (channel.OpeningTieAfterLoopLine < 0 && channel.Events.OfType<MmlLoopMark>().Any())
            {
                channel.OpeningTieAfterLoopLine = line;
            }

            AddTieAsRest(channel, line, lengthTicks, channel.RestTies);
        }

        // A tie with nothing sounding in front of it, read as a rest byte at the channel's clock and listed in the list given.
        private void AddTieAsRest(ChannelState channel, int line, int lengthTicks, List<int> byteStartTicks)
        {
            channel.OpenRestByte = RestByteKind.None;
            RecordRestByte(channel, RestByteKind.Tie, lengthTicks, byteStartTicks);
            SetCurrentHeldNoteIndex(line, HoldingRest);
            channel.EndTick += lengthTicks;
        }

        // Lengthens the note in front by a tie, on the duration byte the run in front is merging or on one of its own.
        private void LengthenNoteByTie(ChannelState channel, int line, int lengthTicks, int lastNoteIndex)
        {
            var eventsBeingWritten = EventsBeingWritten(line);
            var note = (MmlNote)eventsBeingWritten[lastNoteIndex];
            if (note.EndTick != channel.EndTick)
            {
                throw MmlImportException.TieDoesNotCarryOn(_line);
            }

            // Whether the tie carries on the duration byte the run in front is merging rather than starting one of its own.
            bool doesCarryRunOn = channel.IsMergedRunOpen && !IsRawPitchBendNext();
            eventsBeingWritten[lastNoteIndex] = note with
            {
                LengthTicks = note.LengthTicks + lengthTicks,
                Pieces = doesCarryRunOn
                    ? PiecesWithRunRegrown(note.Pieces, channel.MergedRunTicks, channel.MergedRunTicks + lengthTicks)
                    : note.Pieces.AddRange(MmlDurationEncoding.SplitRunIntoTiedPieces(lengthTicks)),
                TieGates = note.TieGatesWithTie(channel.Quantization),
            };

            if (doesCarryRunOn)
            {
                ContinueDurationRun(channel, lengthTicks);
            }
            else
            {
                StartDurationRun(channel, lengthTicks);
            }

            channel.EndTick += lengthTicks;
        }

        // A note carried on through the ties a body opens with.
        private static MmlNote NoteLengthenedBy(MmlNote note, int addedTicks)
        {
            return note with
            {
                LengthTicks = note.LengthTicks + addedTicks,
                Pieces = note.Pieces.AddRange(MmlDurationEncoding.SplitRunIntoTiedPieces(addedTicks)),
            };
        }

        // Records a rest, or a tie read with nothing sounding, as a byte starting at the channel's clock unless the byte in front carries it on, and notes what the text after it carries on.
        private void RecordRestByte(ChannelState channel, RestByteKind kind, int lengthTicks, List<int> byteStartTicks)
        {
            // Whether the byte in front carries this one on.
            bool isCarriedOn = (channel.OpenRestByte == RestByteKind.Rest || (channel.OpenRestByte == RestByteKind.Tie && kind == RestByteKind.Tie)) && !IsRawPitchBendNext();
            if (!isCarriedOn && _remoteDefinitionChannel is null)
            {
                byteStartTicks.Add(channel.EndTick);
            }

            if (isCarriedOn)
            {
                ContinueDurationRun(channel, lengthTicks);
            }
            else
            {
                StartDurationRun(channel, lengthTicks);
            }

            // What the byte this one is on is being merged as.
            var mergedAs = isCarriedOn && channel.OpenRestByte == RestByteKind.Rest ? RestByteKind.Rest : kind;
            channel.OpenRestByte = PeekNonWhitespace() switch
            {
                '^' => mergedAs,
                'r' when mergedAs == RestByteKind.Rest => mergedAs,
                _ => RestByteKind.None,
            };
        }

        // Ends the run of ties the loop body being read opens with.
        private void CloseBodyOpeningTies()
        {
            if (_openLoopBodies.Count > 0)
            {
                _openLoopBodies.Peek().IsOpeningTieRunOpen = false;
            }
        }

        // A note's pieces with the run at the end of them written out again at its new length.
        private static ImmutableArray<int> PiecesWithRunRegrown(ImmutableArray<int> pieces, int oldRunTicks, int newRunTicks)
        {
            int oldRunPieceCount = MmlDurationEncoding.SplitRunIntoTiedPieces(oldRunTicks).Count;
            return pieces.RemoveRange(pieces.Length - oldRunPieceCount, oldRunPieceCount).AddRange(MmlDurationEncoding.SplitRunIntoTiedPieces(newRunTicks));
        }

        // Starts the run of ticks AddmusicK merges into one duration byte, at the channel's clock.
        private void StartDurationRun(ChannelState channel, int ticks)
        {
            channel.MergedRunStartTick = channel.EndTick;
            channel.MergedRunTicks = ticks;
            ReadDurationRunEnd(channel);
        }

        // Adds a tie's ticks to the run on the channel's open duration byte.
        private void ContinueDurationRun(ChannelState channel, int ticks)
        {
            channel.MergedRunTicks += ticks;
            ReadDurationRunEnd(channel);
        }

        // Reads the run out as its last duration byte and the tick that byte's piece begins on, and whether a ^ follows to carry it on.
        private void ReadDurationRunEnd(ChannelState channel)
        {
            (channel.LastPieceStartTick, channel.LastDurationByte) = FinalPieceOfRun(channel.MergedRunStartTick, channel.MergedRunTicks);
            channel.IsMergedRunOpen = IsTieAhead();
            channel.IsHeldNoteFromLoop = false;
        }

        // The piece AddmusicK's last duration byte for a run covers, as the tick it begins on and its length.
        private static (int Tick, int Ticks) FinalPieceOfRun(int runStartTick, int runTicks)
        {
            int finalPieceTicks = MmlDurationEncoding.LastTiedPieceTicks(runTicks);
            return (runStartTick + runTicks - finalPieceTicks, finalPieceTicks);
        }

        // Whether the next character that is not whitespace is a ^.
        private bool IsTieAhead()
        {
            return PeekNonWhitespace() is '^';
        }

        // Reads the duration written on a note, rest or tie as ticks, the l default where none is written.
        private int ReadDurationTicks()
        {
            if (TryConsume('='))
            {
                return ReadTickCountDuration();
            }

            int noteLengthNumber = ReadInt();
            if (noteLengthNumber >= 0 && !NoteLengthNumberRange.Contains(noteLengthNumber))
            {
                throw MmlImportException.NoteLengthOutOfRange(noteLengthNumber, NoteLengthNumberRange, _line);
            }

            int lengthTicksBeforeDots = noteLengthNumber < 0 ? _defaultLengthTicks : SnesTiming.TicksPerWholeNote / noteLengthNumber;
            return ReadDotsAndTripletOnto(lengthTicksBeforeDots);
        }

        // Reads the tick count after an =, with its dots and triplet under the newest #amk version.
        private int ReadTickCountDuration()
        {
            int ticks = ReadRequiredInt(MmlImportException.TickLengthMissing);

            if (ticks < 1)
            {
                throw MmlImportException.LengthZero(_line);
            }

            if (_amkVersion < MmlPreprocessor.NewestDialect)
            {
                return ticks;
            }

            return ReadDotsAndTripletOnto(ticks);
        }

        // Reads the dots written after a duration, then shortens it to a triplet's where a triplet block is open.
        private int ReadDotsAndTripletOnto(int ticks)
        {
            int dottedTicks = ReadDotsOnto(ticks);
            if (!_isInTriplet)
            {
                return dottedTicks;
            }

            return (int)Math.Floor(dottedTicks * TripletLengthNumerator / TripletLengthDenominator + 0.5);
        }

        // Reads ?n, the legacy directive whose ?0 turns the song's loop off and whose other two cases do nothing.
        private void ReadQuestionMarkDirective()
        {
            _position++;
            int directiveNumber = ReadInt();
            if (directiveNumber < 0)
            {
                directiveNumber = 0;
            }

            switch (directiveNumber)
            {
                case 0:
                    _doesLoop = false;
                    break;
                case 1:
                case 2:
                    break;
                default:
                    throw MmlImportException.QuestionDirectiveIgnored(directiveNumber, _line);
            }
        }

        // The index of the note a tie would extend in whatever is being read now.
        private int CurrentHeldNoteIndex(int line)
        {
            return _openLoopBodies.Count > 0 ? _openLoopBodies.Peek().HeldNoteIndex : CurrentChannel(line).HeldNoteIndex;
        }

        // Sets the index of the note a tie would extend in whatever is being read now.
        private void SetCurrentHeldNoteIndex(int line, int lastNoteIndex)
        {
            if (_openLoopBodies.Count > 0)
            {
                _openLoopBodies.Peek().HeldNoteIndex = lastNoteIndex;
                return;
            }

            CurrentChannel(line).HeldNoteIndex = lastNoteIndex;
        }

        // Marks the innermost open loop body as having fetched a note, a tie or a rest, and whether the last of them was a [ ] body's.
        private void MarkFetched(bool doesEndOnCall = false)
        {
            if (_openLoopBodies.Count == 0)
            {
                return;
            }

            var innermost = _openLoopBodies.Peek();
            innermost.HasFetchedByte = true;
            innermost.IsLastByteFromCall = doesEndOnCall;
        }
    }
}
