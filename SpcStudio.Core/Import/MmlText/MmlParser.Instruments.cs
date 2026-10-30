using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int SampleNumberOfNamedFile = -1;

        // Reads 'n', which switches this channel to the noise generator at a clock.
        private void ReadNoiseLetter()
        {
            int line = _line;
            _position++;
            int clock = ReadRequiredHex(cursorLine => MmlImportException.CommandHasNoValue('n', cursorLine));

            AddNoiseCommand(clock, $"n{clock:X2}", line);
        }

        // Reads $F8, the noise command written as raw hex.
        private void ReadNoiseHex(int line)
        {
            SkipWhitespace();
            int clock = ReadDollarHex(MmlImportException.NoiseHexArgumentMissing, MmlImportException.NoiseHexArgumentMissingDigits);
            AddNoiseCommand(clock, $"$F8 ${clock:X2}", line);
        }

        // Refuses a noise clock past the driver's highest, naming the command as written, and adds the noise command at the current tick.
        private void AddNoiseCommand(int clock, string writtenAs, int line)
        {
            if (clock > SnesNoise.MaxClock)
            {
                throw MmlImportException.NoiseClockOutOfRange(writtenAs, _line);
            }

            EventsBeingWritten(line).Add(new MmlNoise(CurrentChannel(line).EndTick, clock, line));
        }

        // Reads $F3, the sample load written as raw hex: the sample's position in #samples and the integer half of the tuning multiplier.
        private void ReadSampleLoadHex(int line)
        {
            int sampleNumber = ReadHexArgument(AmkCommands.SampleLoad, 0);
            int tuning = ReadHexArgument(AmkCommands.SampleLoad, 1);
            EventsBeingWritten(line).Add(new MmlSampleLoad(CurrentChannel(line).EndTick, sampleNumber, null, tuning, line));
        }

        // Reads '@', which selects a built-in or one of the song's own instruments and sets the transpose the notes after it are written under.
        private void ReadInstrumentCommand()
        {
            int line = _line;
            _position++;
            if (TryConsume('@'))
            {
                throw MmlImportException.DirectInstrumentUnread(_line);
            }

            int instrumentNumber = ReadRequiredInt(cursorLine => MmlImportException.CommandHasNoValue('@', cursorLine));

            if (instrumentNumber > byte.MaxValue)
            {
                throw MmlImportException.InstrumentNumberOutOfRange(instrumentNumber, _line);
            }

            if (instrumentNumber > MmlBuiltins.MaxPitchedNumber && !MmlBuiltins.IsPercussion(instrumentNumber) && instrumentNumber < MmlExporter.FirstCustomInstrument)
            {
                throw MmlImportException.InstrumentNumberUndefined(instrumentNumber, _line);
            }

            var channel = CurrentChannel(line);
            channel.BuiltinTranspose = MmlBuiltins.TransposeOf(instrumentNumber);
            channel.ForcedNoteValue = MmlBuiltins.IsPercussion(instrumentNumber) ? MmlBuiltins.EntryFor(instrumentNumber).ForcedNoteValue : MmlBuiltins.NoForcedNoteValue;
            EventsBeingWritten(line).Add(new MmlInstrumentChange(channel.EndTick, instrumentNumber, line));
        }

        // Reads 'h', the semitones every note written after it on this channel is moved by.
        private void ReadHTranspose()
        {
            _position++;
            int? semitones = ReadIntWithNegative();
            if (semitones is null)
            {
                throw MmlImportException.CommandHasNoValue('h', _line);
            }

            _hTranspose = semitones.Value;
            _hasHTranspose = true;
        }

        // Reads a sample load written with a letter: a quoted sample file or an '@' built-in, then its tuning byte.
        private void ReadSampleLoadLetter(int line)
        {
            int tick = CurrentChannel(line).EndTick;
            _position++;
            int sampleNumber = SampleNumberOfNamedFile;
            string? sampleFile = null;
            if (TryConsume('@'))
            {
                sampleNumber = ReadSampleLoadBuiltinSample();
            }
            else
            {
                sampleFile = ReadQuotedString();
            }

            EventsBeingWritten(line).Add(new MmlSampleLoad(tick, sampleNumber, sampleFile, ReadSampleLoadTuning(), line));
        }

        // Reads the built-in a sample load's '@' names and returns the number of the sample it plays.
        private int ReadSampleLoadBuiltinSample()
        {
            int builtinNumber = ReadRequiredInt(MmlImportException.SampleLoadInstrumentMissing);

            if (builtinNumber > MmlBuiltins.MaxCopiedNumber)
            {
                throw MmlImportException.SampleLoadPastBuiltinTable(builtinNumber, _line);
            }

            return MmlBuiltins.SampleNumberOf(builtinNumber);
        }

        // Reads the rest of a sample load written with a letter: the ',$yy' tuning byte and the ')' after it.
        private int ReadSampleLoadTuning()
        {
            if (!TryConsume(','))
            {
                throw MmlImportException.SampleLoadCommaMissing(_line);
            }

            SkipWhitespace();
            int tuning = ReadDollarHex(MmlImportException.SampleLoadTuningMissing, MmlImportException.SampleLoadTuningMissingDigits);

            if (!TryConsume(')'))
            {
                throw MmlImportException.SampleLoadCloseMissing(_line);
            }

            return tuning;
        }
    }
}
