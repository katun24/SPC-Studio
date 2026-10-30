using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int MinDefaultLengthTicks = 1;

        private static readonly ValueRange LengthDivisorRange = new(1, SnesTiming.TicksPerWholeNote);
        private static readonly ValueRange WrittenFadeTicksRange = new(SnesFade.NoFade, SnesFade.MaxFadeTicks);

        // Reads 'o', which sets the octave the notes after it are written in.
        private void ReadOctave()
        {
            _position++;
            int octave = ReadRequiredInt(cursorLine => MmlImportException.CommandHasNoValue('o', cursorLine));

            if (!WrittenOctaveRange.Contains(octave))
            {
                throw MmlImportException.OctaveOutOfRange(octave, _line);
            }

            _octave = octave;
        }

        // Reads 'l', the length in ticks a note, rest or tie written with no duration of its own takes.
        private void ReadDefaultLength()
        {
            _position++;
            if (TryConsume('='))
            {
                ReadVerbatimDefaultLength();
                return;
            }

            int lengthDivisor = ReadRequiredInt(cursorLine => MmlImportException.CommandHasNoValue('l', cursorLine));

            if (!LengthDivisorRange.Contains(lengthDivisor))
            {
                throw MmlImportException.DefaultLengthOutOfRange(lengthDivisor, _line);
            }

            int ticks = SnesTiming.TicksPerWholeNote / lengthDivisor;
            if (_amkVersion < MmlPreprocessor.NewestDialect)
            {
                _defaultLengthTicks = ticks;
                return;
            }

            _defaultLengthTicks = ReadDotsOnto(ticks);
        }

        // Reads an 'l=' tick count and the dots after it as the default length.
        private void ReadVerbatimDefaultLength()
        {
            if (_amkVersion < MmlPreprocessor.NewestDialect)
            {
                throw MmlImportException.DefaultLengthTicksInAmkTwo(_line);
            }

            int verbatimTicks = ReadRequiredInt(MmlImportException.DefaultLengthTicksMissing);

            if (verbatimTicks < MinDefaultLengthTicks)
            {
                throw MmlImportException.DefaultLengthZero(_line);
            }

            _defaultLengthTicks = ReadDotsOnto(verbatimTicks);
        }

        // Reads '>', which raises the octave, or '<', which lowers it.
        private void ShiftOctave(char letter)
        {
            _position++;
            if (letter == '>')
            {
                if (!ShiftedOctaveRange.Contains(_octave + 1))
                {
                    throw MmlImportException.OctaveRaisedPastTop(_line);
                }

                _octave++;
                return;
            }

            if (!ShiftedOctaveRange.Contains(_octave - 1))
            {
                throw MmlImportException.OctaveLoweredPastBottom(_line);
            }

            _octave--;
        }

        // Reads 'q', the gate and velocity every note after it on this channel is played with.
        private void ReadQuantization()
        {
            _position++;
            int quantization = ReadRequiredHex(cursorLine => MmlImportException.CommandHasNoValue('q', cursorLine));

            if (!QuantizationByteRange.Contains(quantization))
            {
                throw MmlImportException.QuantizationOutOfRange(quantization, _line);
            }

            CurrentChannel(_line).Quantization = quantization;
        }

        // Reads '{', which opens a triplet block.
        private void OpenTriplet()
        {
            _position++;
            if (_isInTriplet)
            {
                throw MmlImportException.TripletInsideTriplet(_line);
            }

            _isInTriplet = true;
            _tripletOpenLine = _line;
        }

        // Reads '}', which closes the triplet block.
        private void CloseTriplet()
        {
            _position++;
            if (!_isInTriplet)
            {
                throw MmlImportException.TripletCloseWithoutOpen(_line);
            }

            _isInTriplet = false;
        }

        // Reads one of 't', 'w', 'v' and 'y' with its arguments: a set, a fade, or a pan with its surround pair.
        private void ReadLetteredCommand(char letter)
        {
            int line = _line;
            _position++;
            int value = ReadRequiredInt(cursorLine => MmlImportException.CommandHasNoValue(letter, cursorLine));

            var surround = SnesSurround.None;
            int? fadeTicks = null;
            if (letter == 'y')
            {
                surround = ReadPanSurround();
            }
            else if (_amkVersion >= MmlPreprocessor.NewestDialect)
            {
                (fadeTicks, value) = ReadLetterFade(letter, value);
            }

            var range = LetterValueRange(letter);
            if (!range.Contains(value))
            {
                throw MmlImportException.LetterValueOutOfRange(letter, value, range, _line);
            }

            AddLaneCommand(letter, value, fadeTicks, surround, line);
        }

        // Reads the surround pair a 'y' may carry after its pan, or none where no comma follows.
        private SnesSurround ReadPanSurround()
        {
            if (!TryConsumeComma())
            {
                return SnesSurround.None;
            }

            int surroundLeft = ReadSurroundArgument(MmlImportException.SurroundValueMissing);
            if (!TryConsumeComma())
            {
                throw MmlImportException.SurroundValueSingle(_line);
            }

            int surroundRight = ReadSurroundArgument(MmlImportException.SurroundSecondValueMissing);
            return SnesSurround.InPanByte(((surroundLeft << SnesSurround.PanByteLeftShift) | (surroundRight << SnesSurround.PanByteRightShift)) & byte.MaxValue);
        }

        // Reads one of the surround pair's two numbers, refused where it is missing or past the range.
        private int ReadSurroundArgument(Func<int, MmlImportException> missingRefusal)
        {
            int surroundArgument = ReadRequiredInt(missingRefusal);

            RefuseOutOfRange("y surround argument", surroundArgument, SurroundArgumentRange);
            return surroundArgument;
        }

        // Reads the value after a fade's length where a comma follows the first number, returning the length and the value, or no length where none was written.
        private (int? FadeTicks, int Value) ReadLetterFade(char letter, int firstNumber)
        {
            if (!TryConsumeComma())
            {
                return (null, firstNumber);
            }

            int value = ReadRequiredInt(cursorLine => MmlImportException.FadeHasNoValue(letter, cursorLine));

            RefuseOutOfRange($"{letter} fade length", firstNumber, WrittenFadeTicksRange);
            return (firstNumber, value);
        }

        // Reads one of the fade commands $E3, $E1, $E8 and $DC: a duration and then a target.
        private void ReadFadeHex(int command, char letter, int line)
        {
            int fadeTicks = ReadHexArgument(command, 0);
            int value = ReadHexArgument(command, 1);
            RefuseOutOfRange($"${command:X2} fade length", fadeTicks, WrittenFadeTicksRange);
            RefuseOutOfRange($"${command:X2} fade target", value, LetterValueRange(letter));
            AddLaneCommand(letter, value, fadeTicks, SnesSurround.None, line);
        }

        // Reads one of the set commands $E2, $E0 and $E7, which are what 't', 'w' and 'v' compile to.
        private void ReadSetHex(int command, char letter, int line)
        {
            int value = ReadHexArgument(command, 0);
            RefuseOutOfRange($"${command:X2} value", value, LetterValueRange(letter));
            AddLaneCommand(letter, value, null, SnesSurround.None, line);
        }

        // Reads $DB, the pan, whose one byte carries the surround pair as well.
        private void ReadPanHex(int line)
        {
            int panByte = ReadHexArgument(AmkCommands.Pan, 0);
            int pan = SnesVolume.PanIndexInPanByte(panByte);
            RefuseOutOfRange("$DB pan", pan, SnesVolume.PanIndexRange);
            AddLaneCommand('y', pan, null, SnesSurround.InPanByte(panByte), line);
        }

        // Adds a set of one of the lettered lanes at the current tick, or a fade where a fade length was written, one of no ticks dropped.
        private void AddLaneCommand(char letter, int value, int? fadeTicks, SnesSurround surround, int line)
        {
            var command = new MmlCommand(CurrentChannel(line).EndTick, letter, value, line, fadeTicks ?? SnesFade.NoFade, surround);
            EventsBeingWritten(line).Add(fadeTicks is { } writtenFadeTicks ? MmlIgnored.ZeroTickFadeOr(command, writtenFadeTicks) : command);
        }

        // The values AddmusicK's own parser accepts for one of the four lettered lanes.
        private static ValueRange LetterValueRange(char letter)
        {
            return letter switch
            {
                't' => SnesTiming.TempoRange,
                'w' => SnesVolume.MasterVolumeRange,
                'v' => SnesVolume.ChannelVolumeRange,
                _ => SnesVolume.PanIndexRange,
            };
        }

        // Refuses a value outside the driver's range, naming what it is.
        private void RefuseOutOfRange(string valueName, int value, ValueRange range)
        {
            if (!range.Contains(value))
            {
                throw MmlImportException.ValueOutOfRange(valueName, value, range, _line);
            }
        }
    }
}
