using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {

        // Reads the vibrato written as hex: a delay, a rate and an extent.
        private void ReadVibratoOn(int line)
        {
            int delay = ReadHexArgument(AmkCommands.VibratoOn, 0);
            int rate = ReadHexArgument(AmkCommands.VibratoOn, 1);
            int extent = ReadHexArgument(AmkCommands.VibratoOn, 2);
            AddVibrato(delay, rate, extent, line);
        }

        // Reads the vibrato written as `p`, with a rate and an extent or with a delay in front of them.
        private void ReadVibratoLetter()
        {
            int line = _line;
            _position++;

            int firstArgument = ReadRequiredInt(cursorLine => MmlImportException.CommandHasNoValue('p', cursorLine));

            if (!TryConsumeComma())
            {
                throw MmlImportException.VibratoOneArgument(_line);
            }

            int secondArgument = ReadRequiredInt(MmlImportException.VibratoExtentMissing);

            if (!TryConsumeComma())
            {
                AddVibrato(SnesSweep.MinDelay, firstArgument, secondArgument, line);
                return;
            }

            int thirdArgument = ReadRequiredInt(MmlImportException.VibratoExtentMissing);

            AddVibrato(firstArgument, secondArgument, thirdArgument, line);
        }

        // Adds a vibrato after refusing a delay, a rate or an extent outside the driver's range.
        private void AddVibrato(int delay, int rate, int extent, int line)
        {
            RefuseOutOfRange("vibrato delay", delay, SnesSweep.DelayRange);
            RefuseOutOfRange("vibrato rate", rate, SnesSweep.RateRange);
            RefuseOutOfRange("vibrato extent", extent, SnesVibrato.ExtentRange);
            EventsBeingWritten(line).Add(new MmlVibrato(CurrentChannel(line).EndTick, delay, rate, extent, line));
        }

        // Adds the vibrato off, which takes the extent to nothing and leaves the delay and the rate where they were.
        private void AddVibratoOff(int line)
        {
            EventsBeingWritten(line).Add(new MmlVibrato(CurrentChannel(line).EndTick, null, null, SnesVibrato.MinExtent, line));
        }

        // Reads the vibrato fade: how many ticks the sweep takes to widen from nothing to the extent in force.
        private void ReadVibratoFade(int line)
        {
            int fadeTicks = ReadHexArgument(AmkCommands.VibratoFade, 0);
            RefuseOutOfRange("vibrato fade", fadeTicks, SnesVibrato.FadeTicksRange);
            EventsBeingWritten(line).Add(new MmlVibratoFade(CurrentChannel(line).EndTick, fadeTicks, line));
        }

        // Reads the tremolo: a delay, a rate and an amount.
        private void ReadTremoloOn(int line)
        {
            int delay = ReadHexArgument(AmkCommands.TremoloOn, 0);
            int rate = ReadHexArgument(AmkCommands.TremoloOn, 1);
            int amount = ReadHexArgument(AmkCommands.TremoloOn, 2);
            RefuseOutOfRange("tremolo delay", delay, SnesSweep.DelayRange);
            RefuseOutOfRange("tremolo rate", rate, SnesSweep.RateRange);
            RefuseOutOfRange("tremolo amount", amount, SnesTremolo.AmountRange);
            EventsBeingWritten(line).Add(new MmlTremolo(CurrentChannel(line).EndTick, delay, rate, amount, line));
        }

        // Adds the tremolo off, which takes the amount to nothing and leaves the delay and the rate where they were.
        private void AddTremoloOff(int line)
        {
            EventsBeingWritten(line).Add(new MmlTremolo(CurrentChannel(line).EndTick, null, null, SnesTremolo.MinAmount, line));
        }
    }
}
