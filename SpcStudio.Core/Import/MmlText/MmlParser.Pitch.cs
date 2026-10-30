using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private static readonly ValueRange WrittenBendLengthRange = new(MmlPitchBend.NoRampTicks, PitchBend.MaxLengthTicks);

        // Reads a $DD's delay, ramp length and target, the target written either as a byte or as the note letter that follows.
        private void ReadPitchBendHex(int line)
        {
            int delay = ReadHexArgument(AmkCommands.PitchBend, 0);
            int length = ReadHexArgument(AmkCommands.PitchBend, 1);
            RefuseOutOfRange("pitch bend delay", delay, PitchBend.DelayRange);
            RefuseOutOfRange("pitch bend length", length, WrittenBendLengthRange);
            SkipWhitespace();
            if (_position < _text.Length && _text[_position] != '$')
            {
                CurrentChannel(line).BendAwaitingTarget = (delay, length, line);
                return;
            }

            AddBend(line, EventsBeingWritten(line), delay, length, BendTargetNoteValue(ReadHexArgument(AmkCommands.PitchBend, 2)));
        }

        // The note value a $DD's target byte names, its high bit masked off.
        private static int BendTargetNoteValue(int targetByte)
        {
            return targetByte & SnesPitch.NoteValueMask;
        }

        // Hangs a bend on the note whose own duration byte it was written behind, or on the rest it was written behind.
        private void AddBend(int line, List<MmlEvent> eventsBeingWritten, int delay, int length, int targetNoteValue)
        {
            int lastNoteIndex = CurrentHeldNoteIndex(line);
            if (lastNoteIndex < 0 && lastNoteIndex != HoldingRest)
            {
                throw MmlImportException.BendWithNothingInFront(line);
            }

            var channel = CurrentChannel(line);
            if (channel.IsHeldNoteFromLoop)
            {
                throw MmlImportException.BendBehindLoop(line);
            }

            if (lastNoteIndex == HoldingRest)
            {
                eventsBeingWritten.Add(new MmlRestBend(channel.LastPieceStartTick, delay, length, targetNoteValue, line));
                return;
            }

            MmlPitchBend.HangOnNote(eventsBeingWritten, lastNoteIndex, channel.LastPieceStartTick, channel.EndTick, line, delay, length, targetNoteValue);
        }

        // Reads an &, which marks the note after it as the target of a bend on the note before it.
        private void ReadPitchSlideMark()
        {
            int line = _line;
            _position++;
            RefuseNoteDataOutsideChannel(line);
            var channel = CurrentChannel(line);
            if (channel.IsPitchSlidePending)
            {
                throw MmlImportException.SlideWithoutNoteBetween(_line);
            }

            channel.IsPitchSlidePending = true;
        }

        // Refuses a $DD in the channel being left that is still waiting for the note letter that was to be its target.
        private void RequirePitchBendResolved()
        {
            if (_currentChannel == NoChannel || _channels[_currentChannel].BendAwaitingTarget is not { } pending)
            {
                return;
            }

            throw MmlImportException.BendTargetMissing(pending.Line);
        }

        // Reads an $EB or an $EC's delay, ramp length and extent into a pitch envelope.
        private void ReadPitchEnvelope(int command, int line)
        {
            int delay = ReadHexArgument(command, 0);
            int length = ReadHexArgument(command, 1);
            int extent = ReadHexArgument(command, 2);
            EventsBeingWritten(line).Add(new MmlPitchEnvelope(CurrentChannel(line).EndTick, command == AmkCommands.PitchEnvelopeAttack, delay, length, extent, line));
        }

        // Adds the bend an & asks for to the note before it, over the channel's last duration byte and ending on the note value that follows.
        private void AddPitchSlideBend(int line, List<MmlEvent> eventsBeingWritten, int noteValue)
        {
            var channel = CurrentChannel(line);
            channel.IsPitchSlidePending = false;
            if (channel.LastDurationByte < 0)
            {
                throw MmlImportException.SlideWithoutNote(line);
            }

            AddBend(line, eventsBeingWritten, PitchBend.MinDelayTicks, channel.LastDurationByte, noteValue);
        }

        // Whether a raw $DD, spelled all upper or all lower case, is the next thing in the text.
        private bool IsRawPitchBendNext()
        {
            return MmlPitchBend.IsSpelledAt(_text, PeekNonWhitespaceIndex());
        }

        // Adds the pitch envelope off, which takes no arguments.
        private void AddPitchEnvelopeOff(int line)
        {
            EventsBeingWritten(line).Add(new MmlPitchEnvelope(CurrentChannel(line).EndTick, IsAttack: false, DelayTicks: 0, LengthTicks: 0, Extent: 0, line));
        }
    }
}
