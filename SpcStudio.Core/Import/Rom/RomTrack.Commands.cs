using SpcStudio.Model;
using static SpcStudio.Import.RomTrackReader;

namespace SpcStudio.Import;

internal sealed partial class RomTrack
{
    private const int ArpeggioFirstDeltaOffset = 3;

    // Everything from $DA up that is not control flow, turned into its event.
    private void ReadCommand(int address, byte command)
    {
        // The command's argument at a position after the command byte.
        int ArgumentAt(int position)
        {
            return _reader.ByteAt(address + position);
        }

        switch (command)
        {
            case AmkCommands.Instrument:
                AddEvent(new MmlInstrumentChange(Tick, ArgumentAt(1), 0));
                return;
            case AmkCommands.Pan:
                AddEvent(new MmlCommand(Tick, 'y', SnesVolume.PanIndexInPanByte(ArgumentAt(1)), 0, SnesFade.NoFade, SnesSurround.InPanByte(ArgumentAt(1))));
                return;
            case AmkCommands.PanFade:
                ReadFade('y', ArgumentAt(1), ArgumentAt(2));
                return;
            case AmkCommands.PitchBend:
                ReadBend(ArgumentAt(1), ArgumentAt(2), ArgumentAt(3) & SnesPitch.NoteValueMask);
                return;
            case AmkCommands.VibratoOn:
                AddEvent(new MmlVibrato(Tick, ArgumentAt(1), ArgumentAt(2), ArgumentAt(3), 0));
                return;
            case AmkCommands.VibratoOff:
                AddEvent(new MmlVibrato(Tick, null, null, SnesVibrato.MinExtent, 0));
                return;
            case AmkCommands.MasterVolume:
                AddEvent(new MmlCommand(Tick, 'w', ArgumentAt(1), 0));
                return;
            case AmkCommands.MasterVolumeFade:
                ReadFade('w', ArgumentAt(1), ArgumentAt(2));
                return;
            case AmkCommands.Tempo:
                AddEvent(new MmlCommand(Tick, 't', ArgumentAt(1), 0));
                return;
            case AmkCommands.TempoFade:
                ReadFade('t', ArgumentAt(1), ArgumentAt(2));
                return;
            case AmkCommands.GlobalTranspose:
                AddEvent(new MmlGlobalTranspose(Tick, (sbyte)ArgumentAt(1), 0));
                return;
            case AmkCommands.TremoloOn:
                AddEvent(new MmlTremolo(Tick, ArgumentAt(1), ArgumentAt(2), ArgumentAt(3), 0));
                return;
            case AmkCommands.ChannelVolume:
                AddEvent(new MmlCommand(Tick, 'v', ArgumentAt(1), 0));
                return;
            case AmkCommands.ChannelVolumeFade:
                ReadFade('v', ArgumentAt(1), ArgumentAt(2));
                return;
            case AmkCommands.VibratoFade:
                AddEvent(new MmlVibratoFade(Tick, ArgumentAt(1), 0));
                return;
            case AmkCommands.PitchEnvelopeRelease:
                AddEvent(new MmlPitchEnvelope(Tick, false, ArgumentAt(1), ArgumentAt(2), ArgumentAt(3), 0));
                return;
            case AmkCommands.PitchEnvelopeAttack:
                AddEvent(new MmlPitchEnvelope(Tick, true, ArgumentAt(1), ArgumentAt(2), ArgumentAt(3), 0));
                return;
            case AmkCommands.Envelope:
                AddEvent(new MmlEnvelope(Tick, ArgumentAt(1), ArgumentAt(2), 0));
                return;
            case AmkCommands.FineTune:
                AddEvent(new MmlFineTune(Tick, ArgumentAt(1), 0));
                return;
            case AmkCommands.EchoSend:
                AddEvent(new MmlEchoCommand(Tick, AmkCommands.EchoSend, ArgumentAt(1), ArgumentAt(2), ArgumentAt(3), 0));
                return;
            case AmkCommands.EchoOff:
                AddEvent(new MmlEchoCommand(Tick, AmkCommands.EchoOff, 0, 0, 0, 0));
                return;
            case AmkCommands.EchoUnit:
                ReadEchoUnit(ArgumentAt(1), ArgumentAt(2), ArgumentAt(3));
                return;
            case AmkCommands.EchoFade:
                ReadEchoFade(ArgumentAt(1), ArgumentAt(2), ArgumentAt(3));
                return;
            case AmkCommands.SampleLoad:
                AddEvent(new MmlSampleLoad(Tick, ArgumentAt(1), null, ArgumentAt(2), 0));
                return;
            case AmkCommands.Misc:
                ReadMiscCommand(ArgumentAt(1));
                return;
            case AmkCommands.FirFilter:
                ReadFirFilter(address);
                return;
            case AmkCommands.DspWrite:
                ReadDspWrite(ArgumentAt(1), ArgumentAt(2));
                return;
            case AmkCommands.Noise:
                AddEvent(new MmlNoise(Tick, ArgumentAt(1), 0));
                return;
            case AmkCommands.SendToGame:
                AddEvent(new MmlIgnored(Tick, IgnoredCommand.SendToGame, 0));
                return;
            case AmkCommands.Parameter:
                ReadParameterCommand(ArgumentAt(1), ArgumentAt(2));
                return;
            case AmkCommands.Arpeggio:
                ReadArpeggio(address, ArgumentAt(1), ArgumentAt(2));
                return;
            case AmkCommands.RemoteCode:
                ReadRemoteCall(ArgumentAt(1) | ArgumentAt(2) << 8, (sbyte)ArgumentAt(3), ArgumentAt(4));
                return;
            case AmkCommands.TremoloOff:
                AddEvent(new MmlTremolo(Tick, null, null, SnesTremolo.MinAmount, 0));
                return;
            case AmkCommands.PitchEnvelopeOff:
                AddEvent(new MmlPitchEnvelope(Tick, false, 0, 0, 0, 0));
                return;
        }

        throw MmlImportException.CommandNotModeled(_channel + 1, command, address);
    }

    // Adds an event to the track.
    private void AddEvent(MmlEvent channelEvent)
    {
        _events.Add(channelEvent);
    }

    // One of the four fades, which carry the duration first and the target second, a duration of zero being dropped.
    private void ReadFade(char letter, int durationTicks, int target)
    {
        AddEvent(MmlIgnored.ZeroTickFadeOr(new MmlCommand(Tick, letter, target, NoTextLine, durationTicks), durationTicks));
    }

    // A $DD pitch bend: onto the open note's chain, or read in the rest behind it, or refused where nothing fetched in front can read it.
    private void ReadBend(int delayTicks, int rampTicks, int targetNoteValue)
    {
        if (_openNoteIndex < 0)
        {
            if (!_doesPeekReachNextByte)
            {
                throw _hasFetchedAny ? MmlImportException.BendBehindLoop(0) : MmlImportException.BendWithNothingInFront(0);
            }

            AddEvent(new MmlRestBend(_lastByteStartTick, delayTicks, rampTicks, targetNoteValue, 0));
            return;
        }

        MmlPitchBend.HangOnNote(_events, _openNoteIndex, _lastByteStartTick, Tick, NoTextLine, delayTicks, rampTicks, targetNoteValue);
    }

    // A $FB, whose deltas its first argument counts below the special forms, and which carries one delta in a special form.
    private void ReadArpeggio(int address, int form, int stepTicks)
    {
        int deltaCount = form < MmlArpeggio.FirstSpecialForm ? form : 1;
        var deltas = new int[deltaCount];
        for (int deltaIndex = 0; deltaIndex < deltaCount; deltaIndex++)
        {
            deltas[deltaIndex] = _reader.ByteAt(address + ArpeggioFirstDeltaOffset + deltaIndex);
        }

        AddEvent(new MmlArpeggio(Tick, form, stepTicks, [.. deltas], 0));
    }

    // An $F2 echo volume fade: a duration and a target for each side, a duration of zero being dropped.
    private void ReadEchoFade(int durationTicks, int targetLeft, int targetRight)
    {
        AddEvent(MmlIgnored.ZeroTickFadeOr(new MmlEchoFade(Tick, durationTicks, targetLeft, targetRight, NoTextLine), durationTicks));
    }

    // An $F1, a filter index past the driver's preset tables read as the default preset and the coefficients the driver copies from there.
    private void ReadEchoUnit(int delay, int feedback, int filterIndex)
    {
        if (filterIndex <= SnesEcho.MaxFilter)
        {
            AddEvent(new MmlEchoCommand(Tick, AmkCommands.EchoUnit, delay, feedback, filterIndex, 0));
            return;
        }

        if (_reader.EchoFilters is not { } echoFilters)
        {
            throw MmlImportException.EchoFilterPastPresetsUnreadDriver(filterIndex);
        }

        var coefficients = new sbyte[SnesEcho.TapCount];
        int filterOffset = SnesEcho.FilterOffsetOf(filterIndex);
        for (int tap = 0; tap < coefficients.Length; tap++)
        {
            coefficients[tap] = (sbyte)echoFilters[filterOffset + tap];
        }

        AddEvent(new MmlEchoCommand(Tick, AmkCommands.EchoUnit, delay, feedback, SnesEcho.DefaultFilter, 0));
        AddEvent(new MmlFirFilter(Tick, SnesEcho.PackCoefficients(coefficients), 0));
    }

    // An $F5, the filter's coefficients written out.
    private void ReadFirFilter(int address)
    {
        var coefficients = new sbyte[SnesEcho.TapCount];
        for (int tap = 0; tap < coefficients.Length; tap++)
        {
            coefficients[tap] = (sbyte)_reader.ByteAt(address + 1 + tap);
        }

        AddEvent(new MmlFirFilter(Tick, SnesEcho.PackCoefficients(coefficients), 0));
    }

    // An $F6 raw DSP write, sorted by the register it reaches.
    private void ReadDspWrite(int register, int value)
    {
        AddEvent(MmlDspWrite.EventFor(Tick, register, value, NoTextLine) ?? throw MmlImportException.DspWriteNotModeled(register));
    }

    // An $F4 and its subcommand.
    private void ReadMiscCommand(int subcommand)
    {
        switch (subcommand)
        {
            case AmkCommands.YoshiDrumsChannel5Subcommand:
                AddEvent(new MmlIgnored(Tick, IgnoredCommand.YoshiDrumsChannel5, 0));
                return;
            case AmkCommands.LegatoSubcommand:
                AddEvent(new MmlLegato(Tick, 0));
                return;
            case AmkCommands.LightStaccatoSubcommand:
                AddEvent(new MmlStaccato(Tick, 0));
                return;
            case AmkCommands.EchoToggleSubcommand:
                AddEvent(new MmlEchoToggle(Tick, 0));
                return;
            case AmkCommands.GameSyncCounterSubcommand:
                AddEvent(new MmlIgnored(Tick, IgnoredCommand.GameSyncCounter, 0));
                return;
            case AmkCommands.YoshiDrumsThisChannelSubcommand:
                AddEvent(new MmlIgnored(Tick, IgnoredCommand.YoshiDrumsThisChannel, 0));
                return;
            case AmkCommands.TempoImmunitySubcommand:
                AddEvent(new MmlTempoImmunity(Tick, 0));
                return;
            case AmkCommands.NspcVelocityTableSubcommand:
                AddEvent(new MmlVelocityTable(Tick, false, 0));
                return;
            case AmkCommands.InstrumentRestoreSubcommand:
                AddEvent(new MmlEnvelopeRestore(Tick, 0));
                return;
        }

        throw MmlImportException.SubcommandNotModeled(AmkCommands.Misc, subcommand);
    }

    // An $FA and its subcommand.
    private void ReadParameterCommand(int subcommand, int value)
    {
        switch (subcommand)
        {
            case AmkCommands.PitchModulationSubcommand:
                AddEvent(MmlIgnored.ForPitchModulation(Tick, value, 0));
                return;
            case AmkCommands.GainSubcommand:
                AddEvent(new MmlEnvelopeGain(Tick, value, 0));
                return;
            case AmkCommands.TransposeSubcommand:
                AddEvent(new MmlTranspose(Tick, (sbyte)value, 0));
                return;
            case AmkCommands.AmplifySubcommand:
                AddEvent(new MmlAmplify(Tick, value, 0));
                return;
            case AmkCommands.EchoDelaySubcommand:
                AddEvent(new MmlEchoDelay(Tick, value, 0));
                return;
            case AmkCommands.VelocityTableSubcommand:
                AddEvent(new MmlVelocityTable(Tick, value == 0, 0));
                return;
            case AmkCommands.HotPatchPresetSubcommand:
                AddEvent(MmlIgnored.ForHotPatch(Tick, IgnoredCommand.HotPatchPreset, value, 0));
                return;
            case AmkCommands.HotPatchBitsSubcommand:
                AddEvent(MmlIgnored.ForHotPatch(Tick, IgnoredCommand.HotPatchBits, value, 0));
                return;
        }

        throw MmlImportException.SubcommandNotModeled(AmkCommands.Parameter, subcommand);
    }

    // An $FC remote code call, the block it arms decoded and filed.
    private void ReadRemoteCall(int address, int triggerType, int timeTicks)
    {
        if (IsRemoteBlock)
        {
            throw MmlImportException.RemoteCallInsideRemoteBlock();
        }

        if (!MmlRemoteCall.IsReadType(triggerType))
        {
            throw MmlImportException.RemoteEventTypeUnread(triggerType, 0);
        }

        int? blockLabel = MmlRemoteCall.IsClearType(triggerType) ? null : _reader.RemoteBlockKey(address);
        AddEvent(new MmlRemoteCall(Tick, blockLabel, triggerType, timeTicks, 0));
    }
}
