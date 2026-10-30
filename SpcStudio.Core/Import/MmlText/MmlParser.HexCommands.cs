using SpcStudio.Model;

namespace SpcStudio.Import;

public static partial class MmlParser
{
    private sealed partial class Parser
    {
        private const int EchoOffArgumentCount = 0;
        private const int EchoSettingsArgumentCount = 3;
        private const int ArpeggioFirstDeltaArgument = 2;
        private const int ArpeggioSpecialFormDeltaCount = 1;
        private const int ParameterValueArgument = 1;

        // Reads a raw hex command and its arguments into its event, refusing a command that is not read.
        private void ReadHexCommand()
        {
            int line = _line;
            _position++;

            int command = ReadRequiredHex(MmlImportException.DollarWithoutHexDigits);

            switch (command)
            {
                case AmkCommands.FineTune: ReadFineTune(line); return;
                case AmkCommands.EchoOff: ReadEchoCommand(command, EchoOffArgumentCount, line); return;
                case AmkCommands.EchoSend or AmkCommands.EchoUnit: ReadEchoCommand(command, EchoSettingsArgumentCount, line); return;
                case AmkCommands.EchoFade: ReadEchoFade(line); return;
                case AmkCommands.FirFilter: ReadEchoFilter(line); return;
                case AmkCommands.SampleLoad: ReadSampleLoadHex(line); return;
                case AmkCommands.Misc: ReadMiscCommand(line); return;
                case AmkCommands.Parameter: ReadParameterCommand(line); return;
                case AmkCommands.VibratoOn: ReadVibratoOn(line); return;
                case AmkCommands.VibratoFade: ReadVibratoFade(line); return;
                case AmkCommands.TremoloOn: ReadTremoloOn(line); return;
                case AmkCommands.Envelope: ReadEnvelope(line); return;
                case AmkCommands.Noise: ReadNoiseHex(line); return;
                case AmkCommands.Arpeggio: ReadArpeggio(line); return;
                case AmkCommands.PitchBend: ReadPitchBendHex(line); return;
                case AmkCommands.GlobalTranspose: ReadGlobalTranspose(line); return;
                case AmkCommands.DspWrite: ReadDspWrite(line); return;
                case AmkCommands.RemoteCode: ReadRemoteCodeHex(line); return;
                case AmkCommands.Subloop: ReadSubLoopHex(line); return;
                case AmkCommands.SendToGame: ReadSendToGame(line); return;
                case AmkCommands.PitchEnvelopeRelease or AmkCommands.PitchEnvelopeAttack: ReadPitchEnvelope(command, line); return;
                case AmkCommands.PitchEnvelopeOff: AddPitchEnvelopeOff(line); return;
                case AmkCommands.Tempo: ReadSetHex(command, 't', line); return;
                case AmkCommands.MasterVolume: ReadSetHex(command, 'w', line); return;
                case AmkCommands.ChannelVolume: ReadSetHex(command, 'v', line); return;
                case AmkCommands.Pan: ReadPanHex(line); return;
                case AmkCommands.TempoFade: ReadFadeHex(command, 't', line); return;
                case AmkCommands.MasterVolumeFade: ReadFadeHex(command, 'w', line); return;
                case AmkCommands.ChannelVolumeFade: ReadFadeHex(command, 'v', line); return;
                case AmkCommands.PanFade: ReadFadeHex(command, 'y', line); return;
                case AmkCommands.VibratoOff: AddVibratoOff(line); return;
                case AmkCommands.TremoloOff: AddTremoloOff(line); return;
            }

            throw MmlImportException.HexCommandUnread(command, _line);
        }

        // Reads the two bytes handed to the game and drops them.
        private void ReadSendToGame(int line)
        {
            ReadHexArgument(AmkCommands.SendToGame, 0);
            ReadHexArgument(AmkCommands.SendToGame, 1);
            AddIgnored(IgnoredCommand.SendToGame, line);
        }

        // Reads a miscellaneous command's subcommand into its event, refusing a subcommand that is not read.
        private void ReadMiscCommand(int line)
        {
            int subcommand = ReadHexArgument(AmkCommands.Misc, 0);

            switch (subcommand)
            {
                case AmkCommands.LegatoSubcommand:
                    EventsBeingWritten(line).Add(new MmlLegato(CurrentChannel(line).EndTick, line));
                    return;

                case AmkCommands.LightStaccatoSubcommand:
                    EventsBeingWritten(line).Add(new MmlStaccato(CurrentChannel(line).EndTick, line));
                    return;

                case AmkCommands.EchoToggleSubcommand:
                    EventsBeingWritten(line).Add(new MmlEchoToggle(CurrentChannel(line).EndTick, line));
                    return;

                case AmkCommands.InstrumentRestoreSubcommand:
                    EventsBeingWritten(line).Add(new MmlEnvelopeRestore(CurrentChannel(line).EndTick, line));
                    return;

                case AmkCommands.TempoImmunitySubcommand:
                    EventsBeingWritten(line).Add(new MmlTempoImmunity(CurrentChannel(line).EndTick, line));
                    return;

                case AmkCommands.NspcVelocityTableSubcommand:
                    AddVelocityTableSwitch(false, line);
                    return;

                case AmkCommands.YoshiDrumsChannel5Subcommand:
                    AddIgnored(IgnoredCommand.YoshiDrumsChannel5, line);
                    return;

                case AmkCommands.YoshiDrumsThisChannelSubcommand:
                    AddIgnored(IgnoredCommand.YoshiDrumsThisChannel, line);
                    return;

                case AmkCommands.GameSyncCounterSubcommand:
                    AddIgnored(IgnoredCommand.GameSyncCounter, line);
                    return;
            }

            throw MmlImportException.SubcommandUnread(AmkCommands.Misc, subcommand, _line);
        }

        // Reads a parameter command's subcommand and its value into its event, refusing a subcommand that is not read.
        private void ReadParameterCommand(int line)
        {
            int subcommand = ReadHexArgument(AmkCommands.Parameter, 0);

            switch (subcommand)
            {
                case AmkCommands.GainSubcommand:
                    ReadGain(line);
                    return;

                case AmkCommands.AmplifySubcommand:
                    ReadAmplify(line);
                    return;

                case AmkCommands.TransposeSubcommand:
                    ReadTranspose(line);
                    return;

                case AmkCommands.EchoDelaySubcommand:
                    ReadEchoDelay(line);
                    return;

                case AmkCommands.VelocityTableSubcommand:
                    AddVelocityTableSwitch(ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument) == 0, line);
                    return;

                case AmkCommands.PitchModulationSubcommand:
                    EventsBeingWritten(line).Add(MmlIgnored.ForPitchModulation(CurrentChannel(line).EndTick, ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument), line));
                    return;

                case AmkCommands.HotPatchPresetSubcommand:
                    AddIgnoredHotPatch(IgnoredCommand.HotPatchPreset, ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument), line);
                    return;

                case AmkCommands.HotPatchBitsSubcommand:
                    ReadHotPatchBits(line);
                    return;
            }

            throw MmlImportException.SubcommandUnread(AmkCommands.Parameter, subcommand, _line);
        }

        // Reads the bit-by-bit hot patch's bytes for as long as each says another follows, and drops the command.
        private void ReadHotPatchBits(int line)
        {
            int firstBitsByte = ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument);
            int bitsByte = firstBitsByte;
            for (int argumentIndex = ParameterValueArgument + 1; (bitsByte & AmkCommands.HotPatchContinuationBit) != 0; argumentIndex++)
            {
                bitsByte = ReadHexArgument(AmkCommands.Parameter, argumentIndex);
            }

            AddIgnoredHotPatch(IgnoredCommand.HotPatchBits, firstBitsByte, line);
        }

        // Adds a command that is read and modeled by nothing, at the tick it was written at.
        private void AddIgnored(IgnoredCommand command, int line)
        {
            EventsBeingWritten(line).Add(new MmlIgnored(CurrentChannel(line).EndTick, command, line));
        }

        // Adds a switch of the velocity table, whichever of its spellings wrote it.
        private void AddVelocityTableSwitch(bool isSmwTable, int line)
        {
            EventsBeingWritten(line).Add(new MmlVelocityTable(CurrentChannel(line).EndTick, isSmwTable, line));
        }

        // Adds a hot patch, which is read and modeled by nothing, carrying one of its bytes.
        private void AddIgnoredHotPatch(IgnoredCommand command, int argument, int line)
        {
            EventsBeingWritten(line).Add(MmlIgnored.ForHotPatch(CurrentChannel(line).EndTick, command, argument, line));
        }

        // Reads the amplify, which sets the channel's volume multiplier from that tick on.
        private void ReadAmplify(int line)
        {
            int value = ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument);
            RefuseOutOfRange("amplify", value, SnesVolume.AmplifyRange);
            EventsBeingWritten(line).Add(new MmlAmplify(CurrentChannel(line).EndTick, value, line));
        }

        // Reads the GAIN command, which writes GAIN and clears the ADSR enable bit.
        private void ReadGain(int line)
        {
            int value = ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument);
            RefuseOutOfRange("GAIN", value, SnesEnvelopeOverride.ByteRange);
            EventsBeingWritten(line).Add(new MmlEnvelopeGain(CurrentChannel(line).EndTick, value, line));
        }

        // Reads the echo delay, the unit command's first argument written without the other two.
        private void ReadEchoDelay(int line)
        {
            EventsBeingWritten(line).Add(new MmlEchoDelay(CurrentChannel(line).EndTick, ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument), line));
        }

        // Reads the channel's semitone transpose, a signed byte in force from that tick on.
        private void ReadTranspose(int line)
        {
            int value = ReadHexArgument(AmkCommands.Parameter, ParameterValueArgument);
            EventsBeingWritten(line).Add(new MmlTranspose(CurrentChannel(line).EndTick, (sbyte)value, line));
        }

        // Reads the global transpose, a signed byte every channel's notes are shifted by from that tick on.
        private void ReadGlobalTranspose(int line)
        {
            int value = ReadHexArgument(AmkCommands.GlobalTranspose, 0);
            EventsBeingWritten(line).Add(new MmlGlobalTranspose(CurrentChannel(line).EndTick, (sbyte)value, line));
        }

        // Reads the fine tune, in force for every note after it.
        private void ReadFineTune(int line)
        {
            int value = ReadHexArgument(AmkCommands.FineTune, 0);
            if (!SnesPitch.FineTuneRange.Contains(value))
            {
                throw MmlImportException.FineTuneOutOfRange(value, _line);
            }

            EventsBeingWritten(line).Add(new MmlFineTune(CurrentChannel(line).EndTick, value, line));
        }

        // Reads one of the three echo commands and as many arguments as it takes.
        private void ReadEchoCommand(int command, int argumentCount, int line)
        {
            int firstArgument = argumentCount > 0 ? ReadHexArgument(command, 0) : 0;
            int secondArgument = argumentCount > 1 ? ReadHexArgument(command, 1) : 0;
            int thirdArgument = argumentCount > 2 ? ReadHexArgument(command, 2) : 0;
            EventsBeingWritten(line).Add(new MmlEchoCommand(CurrentChannel(line).EndTick, command, firstArgument, secondArgument, thirdArgument, line));
        }

        // Reads the echo volume fade, a duration and a target for each side, dropping one of no ticks.
        private void ReadEchoFade(int line)
        {
            int durationTicks = ReadHexArgument(AmkCommands.EchoFade, 0);
            int targetLeft = ReadHexArgument(AmkCommands.EchoFade, 1);
            int targetRight = ReadHexArgument(AmkCommands.EchoFade, 2);
            EventsBeingWritten(line).Add(MmlIgnored.ZeroTickFadeOr(new MmlEchoFade(CurrentChannel(line).EndTick, durationTicks, targetLeft, targetRight, line), durationTicks));
        }

        // Reads the FIR filter's coefficients.
        private void ReadEchoFilter(int line)
        {
            var coefficients = new sbyte[SnesEcho.TapCount];
            for (int tap = 0; tap < SnesEcho.TapCount; tap++)
            {
                coefficients[tap] = (sbyte)ReadHexArgument(AmkCommands.FirFilter, tap);
            }

            EventsBeingWritten(line).Add(new MmlFirFilter(CurrentChannel(line).EndTick, SnesEcho.PackCoefficients(coefficients), line));
        }

        // Reads the arpeggio command: its form, its step length and as many note deltas as the form says.
        private void ReadArpeggio(int line)
        {
            int form = ReadHexArgument(AmkCommands.Arpeggio, 0);
            int stepTicks = ReadHexArgument(AmkCommands.Arpeggio, 1);
            int deltaCount = form < MmlArpeggio.FirstSpecialForm ? form : ArpeggioSpecialFormDeltaCount;
            var deltas = new int[deltaCount];
            for (int i = 0; i < deltaCount; i++)
            {
                deltas[i] = ReadHexArgument(AmkCommands.Arpeggio, i + ArpeggioFirstDeltaArgument);
            }

            EventsBeingWritten(line).Add(new MmlArpeggio(CurrentChannel(line).EndTick, form, stepTicks, [.. deltas], line));
        }

        // Reads one `$xx` argument of a raw hex command, expanding a replacement at every character of space in front of it.
        private int ReadHexArgument(int command, int index)
        {
            while (true)
            {
                ExpandReplacementAtCursor();
                if (_position >= _text.Length || !AmkWhitespace.IsWhitespace(_text[_position]))
                {
                    break;
                }

                AdvanceCountingLines();
            }

            return ReadDollarHex(
                cursorLine => MmlImportException.HexArgumentMissing(command, index + 1, cursorLine), cursorLine => MmlImportException.HexArgumentMissingDigits(command, index + 1, cursorLine));
        }

        // Reads the envelope override's two arguments as written.
        private void ReadEnvelope(int line)
        {
            int firstArgument = ReadHexArgument(AmkCommands.Envelope, 0);
            int secondArgument = ReadHexArgument(AmkCommands.Envelope, 1);
            RefuseOutOfRange("envelope first argument", firstArgument, SnesEnvelopeOverride.ByteRange);
            RefuseOutOfRange("envelope second argument", secondArgument, SnesEnvelopeOverride.ByteRange);
            EventsBeingWritten(line).Add(new MmlEnvelope(CurrentChannel(line).EndTick, firstArgument, secondArgument, line));
        }

        // Reads a raw DSP write, keeping it, dropping it or refusing it by the register it names.
        private void ReadDspWrite(int line)
        {
            int register = ReadHexArgument(AmkCommands.DspWrite, 0);
            int value = ReadHexArgument(AmkCommands.DspWrite, 1);
            var dspWriteEvent = MmlDspWrite.EventFor(CurrentChannel(line).EndTick, register, value, line) ?? throw MmlImportException.DspWriteUnread(register, _line);
            EventsBeingWritten(line).Add(dspWriteEvent);
        }
    }
}
