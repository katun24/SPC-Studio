using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int HalveTempoDivider = 2;

        // The divider the song's lengths are divided by, which AddmusicK applies under its newest dialect alone.
        private int LengthDivider
        {
            get
            {
                return _amkVersion >= MmlPreprocessor.NewestDialect ? _tempoDivider : MmlDurationEncoding.NoLengthDivider;
            }
        }

        // Reads #halvetempo, which doubles the divider, refusing one after a channel header.
        private void ReadHalveTempo(int line)
        {
            if (_isChannelDefined)
            {
                throw MmlImportException.HalveTempoAfterChannel(line);
            }

            if (_tempoDivider > int.MaxValue / HalveTempoDivider)
            {
                throw MmlImportException.HalveTempoTooOften(line);
            }

            _tempoDivider *= HalveTempoDivider;
            _tempoDividerDirective = AmkDirectives.Written(AmkDirectives.HalveTempo);
        }

        // Reads #option dividetempo's number, which replaces the divider.
        private void ReadDivideTempoOption()
        {
            SkipWhitespace();
            int divider = ReadRequiredInt(MmlImportException.DivideTempoNumberMissing);
            if (divider == 0)
            {
                throw MmlImportException.DivideTempoByZero(_line);
            }

            _tempoDivider = divider;
            _tempoDividerDirective = $"'{AmkDirectives.WrittenOption(AmkDirectives.DivideTempoOption)}'";
        }

        // A length a command carries, divided by the divider and rounded down.
        private int DividedLength(int ticks)
        {
            return ticks / LengthDivider;
        }

        // A tempo divided by the divider, refused where the division takes it down to 0.
        private int DividedTempo(int tempo, string writtenTempo)
        {
            int dividedTempo = DividedLength(tempo);
            if (dividedTempo == 0 && tempo > 0)
            {
                throw MmlImportException.TempoDividedToZero(_tempoDividerDirective, writtenTempo, _line);
            }

            return dividedTempo;
        }

        // A vibrato's or a tremolo's rate multiplied by the divider, under every dialect, refused where it passes a byte.
        private int MultipliedRate(int rate, string sweepName)
        {
            long multipliedRate = (long)rate * _tempoDivider;
            if (multipliedRate > byte.MaxValue)
            {
                throw MmlImportException.RateMultipliedPastByte(_tempoDividerDirective, sweepName, rate, _line);
            }

            return (int)multipliedRate;
        }

        // Starts dividing the run a note, rest or tie opens, refusing the run before it where the divider leaves a remainder of it.
        private void StartDividedRun()
        {
            RefuseUndividableRun();
            _dividedRunWrittenTicks = 0;
        }

        // The ticks a part of the run being divided plays for: how far the divided run grows with the part's written ticks added to it.
        private int DividedRunPartTicks(int writtenTicks)
        {
            if (!MmlDurationEncoding.CanSplitRunsUnder(LengthDivider))
            {
                throw MmlImportException.LongNotesNotDividable(_tempoDividerDirective, LengthDivider, _line);
            }

            int dividedTicksBefore = DividedLength(_dividedRunWrittenTicks);
            _dividedRunWrittenTicks += writtenTicks;
            _dividedRunLine = _line;
            return DividedLength(_dividedRunWrittenTicks) - dividedTicksBefore;
        }

        // Refuses the run last divided where the divider leaves a remainder of it.
        private void RefuseUndividableRun()
        {
            if (_dividedRunWrittenTicks % LengthDivider != 0)
            {
                throw MmlImportException.LengthNotDividable(_tempoDividerDirective, _dividedRunWrittenTicks, LengthDivider, _dividedRunLine);
            }
        }
    }
}
