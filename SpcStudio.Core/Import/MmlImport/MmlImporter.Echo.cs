using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's echo: the channels' echo bits, the echo volume lane and the echo unit's runs.
public static partial class MmlImporter
{
    private const string EchoVolumeLaneName = "echo volume";

    // The song's echo read off every command and register write that reaches it: whether it has any, and what it has as the channels' echo bits, the echo volume lane and the echo unit's runs.
    private static void ApplyEcho(Song song, WalkedCommands walked, int? loopTick, Action<string>? warn)
    {
        var echoCommands = walked.EchoCommands;
        var echoToggles = walked.EchoToggles;
        var echoFades = walked.EchoFades;
        var firFilters = walked.FirFilters;
        var echoDelays = walked.EchoDelays;
        var echoDspWrites = walked.ChipRegisterWrites;
        var noisePushes = walked.NoiseClockPushes;
        if (echoCommands.Count == 0 && echoToggles.Count == 0 && echoFades.Count == 0 && firFilters.Count == 0 && echoDelays.Count == 0 && echoDspWrites.Count == 0)
        {
            return;
        }

        var flagsWrites = echoDspWrites.Where(write => SnesDspRegister.Classify(write.Written.Register) == DspRegisterKind.Flags).ToList();
        foreach (var flagsWrite in flagsWrites)
        {
            RefuseFlagsWriteBeyondEchoBit(flagsWrite, noisePushes);
        }

        var echoRegisterWrites = echoDspWrites.Except(flagsWrites).ToList();
        var sendCommands = echoCommands.Where(command => command.Written.CommandByte == AmkCommands.EchoSend).ToList();
        var offCommands = echoCommands.Where(command => command.Written.CommandByte == AmkCommands.EchoOff).ToList();
        var unitCommands = echoCommands.Where(command => command.Written.CommandByte == AmkCommands.EchoUnit).ToList();
        int? firstUnitTick = unitCommands.Select(command => (int?)command.Written.Tick).Min();
        var sendEvents = EchoSendEventsInDriverOrder([.. sendCommands, .. offCommands], echoToggles, echoFades, [.. echoRegisterWrites.Where(write => IsEchoVolumeRegister(write.Written.Register))]);
        if (IsEchoHeardBeforeUnit(song, sendEvents, firstUnitTick))
        {
            warn?.Invoke(ImportWarnings.EchoHeardBeforeUnit());
        }

        if (unitCommands.Count == 0)
        {
            return;
        }

        ApplyEchoSend(song, [.. sendEvents.Where(sendEvent => sendEvent.Command is not { CommandByte: AmkCommands.EchoOff })], warn);
        ApplyEchoUnit(
            song, unitCommands, offCommands, firFilters, echoDelays, [.. echoRegisterWrites.Where(write => !IsEchoVolumeRegister(write.Written.Register))], flagsWrites, noisePushes, loopTick);
    }

    // Refuses a write to FLG whose bits other than the echo-write bit differ from what the driver's own copy of the register holds at that moment.
    private static void RefuseFlagsWriteBeyondEchoBit((MmlDspWrite Written, int Channel, int Order) flagsWrite, List<(int Tick, int Channel, int Order, int Clock)> noisePushes)
    {
        var (written, channel, order) = flagsWrite;
        if ((written.Value & SnesDspRegister.FlagsResetAndMute) != 0)
        {
            throw MmlImportException.FlagsWriteResetsOrMutes(written.Tick, written.Line);
        }

        var writeOrderKey = DriverReadOrder.Key(written.Tick, channel, order);

        int noiseClockAtWrite = noisePushes
            .Where(push => DriverReadOrder.Key(push.Tick, push.Channel, push.Order).CompareTo(writeOrderKey) < 0)
            .OrderBy(push => DriverReadOrder.Key(push.Tick, push.Channel, push.Order))
            .Select(push => push.Clock & SnesDspRegister.FlagsNoiseClock)
            .DefaultIfEmpty(SnesNoise.MinClock)
            .Last();

        if ((written.Value & SnesDspRegister.FlagsNoiseClock) != noiseClockAtWrite)
        {
            throw MmlImportException.FlagsWriteChangesNoiseClock(written.Tick, written.Line);
        }
    }

    // Whether a register is one of the two echo volumes.
    private static bool IsEchoVolumeRegister(int register)
    {
        return SnesDspRegister.Classify(register) is DspRegisterKind.EchoVolumeLeft or DspRegisterKind.EchoVolumeRight;
    }

    // The $EF and $F0 commands, the $F4 $03s, the $F2 fades and the writes to the two echo volumes, put together in the driver's order.
    private static List<EchoSendEvent> EchoSendEventsInDriverOrder(
        List<(MmlEchoCommand Written, int Channel, int Order)> sendAndOffCommands, List<(int Tick, int Channel, int Order, int Line)> echoToggles,
        List<(MmlEchoFade Written, int Channel, int Order)> echoFades, List<(MmlDspWrite Written, int Channel, int Order)> echoVolumeWrites)
    {
        return
        [
            .. sendAndOffCommands.Select(entry => new EchoSendEvent(entry.Written.Tick, entry.Channel, entry.Order, entry.Written, null, null))
                .Concat(echoToggles.Select(entry => new EchoSendEvent(entry.Tick, entry.Channel, entry.Order, null, null, null)))
                .Concat(echoFades.Select(entry => new EchoSendEvent(entry.Written.Tick, entry.Channel, entry.Order, null, entry.Written, null)))
                .Concat(echoVolumeWrites.Select(entry => new EchoSendEvent(entry.Written.Tick, entry.Channel, entry.Order, null, null, entry.Written)))
                .OrderBy(sendEvent => DriverReadOrder.Key(sendEvent.Tick, sendEvent.Channel, sendEvent.Order)),
        ];
    }

    // Whether a note is heard through the echo before the song's first $F1, or anywhere where it has none.
    private static bool IsEchoHeardBeforeUnit(Song song, List<EchoSendEvent> sendEvents, int? firstUnitTick)
    {
        int endTick = firstUnitTick ?? int.MaxValue;
        int sendMask = 0;
        bool isVolumeHeard = true;
        var sentFromTicks = new int?[Song.DriverChannelCount];
        foreach (var tickEvents in sendEvents.Where(sendEvent => sendEvent.Tick < endTick).GroupBy(sendEvent => sendEvent.Tick))
        {
            foreach (var sendEvent in tickEvents)
            {
                if (sendEvent.Command is { } command)
                {
                    bool isOff = command.CommandByte == AmkCommands.EchoOff;
                    (sendMask, isVolumeHeard) = (isOff ? 0 : command.FirstArgument, !isOff && (command.SecondArgument != 0 || command.ThirdArgument != 0));
                }
                else if (sendEvent.Fade is { } fade)
                {
                    isVolumeHeard |= fade.TargetLeft != 0 || fade.TargetRight != 0;
                }
                else if (sendEvent.VolumeWrite is { } volumeWrite)
                {
                    isVolumeHeard |= volumeWrite.Value != 0;
                }
                else
                {
                    sendMask ^= SnesDspRegister.VoiceBit(sendEvent.Channel);
                }
            }

            for (int channel = 0; channel < Song.DriverChannelCount; channel++)
            {
                bool isSent = isVolumeHeard && SnesDspRegister.HasVoiceBit(sendMask, channel);
                if (isSent && sentFromTicks[channel] is null)
                {
                    sentFromTicks[channel] = tickEvents.Key;
                }
                else if (!isSent && sentFromTicks[channel] is { } sentFromTick)
                {
                    if (DoesChannelSoundBetween(song, channel, sentFromTick, tickEvents.Key))
                    {
                        return true;
                    }

                    sentFromTicks[channel] = null;
                }
            }
        }

        for (int channel = 0; channel < Song.DriverChannelCount; channel++)
        {
            if (sentFromTicks[channel] is { } sentFromTick && DoesChannelSoundBetween(song, channel, sentFromTick, endTick))
            {
                return true;
            }
        }

        return false;
    }

    // Whether any note of a channel sounds between two ticks.
    private static bool DoesChannelSoundBetween(Song song, int channel, int startTick, int stopTick)
    {
        return song.Notes(channel).Any(note => note.StartTick < stopTick && note.EndTick() > startTick);
    }

    // The $EF commands, the $F4 $03s, the $F2 fades and the writes to the two echo volumes, walked together in the driver's order into the channels' echo bits and the echo volume lane.
    private static void ApplyEchoSend(Song song, List<EchoSendEvent> sendEvents, Action<string>? warn)
    {
        int sendMask = 0;
        var volumeLeft = SnesFade.HeldAt(SnesEcho.Default(EchoSetting.VolumeLeft));
        var volumeRight = SnesFade.HeldAt(SnesEcho.Default(EchoSetting.VolumeRight));
        int walkedTick = 0;
        var collectedLeft = new List<CollectedRegionCommand>();
        var collectedRight = new List<CollectedRegionCommand>();
        var inexactHeldTicks = new List<int>();
        var cappedTicks = new List<int>();

        // The driver's own countdown of the running fade, which a set that wipes a side does not stop.
        int fadeCountdown = 0;
        foreach (var tickEvents in sendEvents.GroupBy(sendEvent => sendEvent.Tick).OrderBy(group => group.Key))
        {
            int tick = tickEvents.Key;
            volumeLeft = volumeLeft.Advanced(tick - walkedTick);
            volumeRight = volumeRight.Advanced(tick - walkedTick);
            fadeCountdown = Math.Max(0, fadeCountdown - (tick - walkedTick));
            walkedTick = tick;
            fadeCountdown = tickEvents.LastOrDefault(sendEvent => sendEvent.Fade is not null).Fade is { } lastFade ? lastFade.DurationTicks : fadeCountdown;

            int beforeLeft = volumeLeft.Value;
            int beforeRight = volumeRight.Value;
            int endMask = sendMask;
            int? setLeft = null;
            int? setRight = null;
            (int Ticks, int ToLeft, int ToRight)? armedFade = null;

            // Whether a fade is still counting once the tick's commands are read, which pushes both volumes from their shadows over whatever a register write put there.
            bool areRegisterWritesPushedOver = fadeCountdown > 0;

            // Whether a set has written each side behind a fade armed at this tick that had nothing left to add on that side.
            bool isLeftWiped = false;
            bool isRightWiped = false;

            // Whether a set has written each side while that side's fade still had a step to add, which the driver goes on adding from the set value.
            bool isLeftSetInsideFade = false;
            bool isRightSetInsideFade = false;

            // Writes a set to one side's volume, over whatever that side's fade is doing.
            void WriteVolume(ref SnesFade volume, ref bool isWiped, ref bool isSetInsideFade, int value)
            {
                isSetInsideFade = volume.IsFading && !volume.HasNoAdditionLeft;
                if (armedFade is not null && !isSetInsideFade)
                {
                    isWiped = true;
                }

                volume = isSetInsideFade ? volume.WithValueSet(value) : SnesFade.HeldAt(value);
            }

            foreach (var sendEvent in tickEvents.Where(sendEvent => sendEvent.VolumeWrite is null || !areRegisterWritesPushedOver))
            {
                if (sendEvent.Fade is { } fade)
                {
                    armedFade = (fade.DurationTicks, fade.TargetLeft, fade.TargetRight);
                    volumeLeft = volumeLeft.FadingTo(fade.TargetLeft, fade.DurationTicks);
                    volumeRight = volumeRight.FadingTo(fade.TargetRight, fade.DurationTicks);
                    (isLeftWiped, isRightWiped) = (false, false);
                    (isLeftSetInsideFade, isRightSetInsideFade) = (false, false);
                }
                else if (sendEvent.Command is { } sendCommand)
                {
                    (endMask, setLeft, setRight) = (sendCommand.FirstArgument, sendCommand.SecondArgument, sendCommand.ThirdArgument);
                    WriteVolume(ref volumeLeft, ref isLeftWiped, ref isLeftSetInsideFade, sendCommand.SecondArgument);
                    WriteVolume(ref volumeRight, ref isRightWiped, ref isRightSetInsideFade, sendCommand.ThirdArgument);
                }
                else if (sendEvent.VolumeWrite is { } volumeWrite)
                {
                    if (SnesDspRegister.Classify(volumeWrite.Register) == DspRegisterKind.EchoVolumeLeft)
                    {
                        setLeft = volumeWrite.Value;
                        WriteVolume(ref volumeLeft, ref isLeftWiped, ref isLeftSetInsideFade, volumeWrite.Value);
                    }
                    else
                    {
                        setRight = volumeWrite.Value;
                        WriteVolume(ref volumeRight, ref isRightWiped, ref isRightSetInsideFade, volumeWrite.Value);
                    }
                }
                else
                {
                    endMask ^= SnesDspRegister.VoiceBit(sendEvent.Channel);
                }
            }

            int endLeft = setLeft ?? beforeLeft;
            int endRight = setRight ?? beforeRight;
            if (armedFade is not null && isLeftWiped && isRightWiped)
            {
                armedFade = null;
            }
            else if (armedFade is { } fadeStanding)
            {
                armedFade = (fadeStanding.Ticks, isLeftWiped ? endLeft : fadeStanding.ToLeft, isRightWiped ? endRight : fadeStanding.ToRight);
            }

            bool isDividerWritten = armedFade is not null || endLeft != beforeLeft || endRight != beforeRight;
            EchoSideFade? fadeLeft = null;
            EchoSideFade? fadeRight = null;
            if (isDividerWritten)
            {
                fadeLeft = FadeForEchoSide(volumeLeft, isLeftSetInsideFade, armedFade is { } armedLeft ? (armedLeft.Ticks, armedLeft.ToLeft) : null);
                fadeRight = FadeForEchoSide(volumeRight, isRightSetInsideFade, armedFade is { } armedRight ? (armedRight.Ticks, armedRight.ToRight) : null);
            }

            if (fadeLeft is { IsCapped: true } || fadeRight is { IsCapped: true })
            {
                cappedTicks.Add(tick);
            }
            else if (fadeLeft is { IsExact: false } || fadeRight is { IsExact: false })
            {
                inexactHeldTicks.Add(tick);
            }

            if (fadeLeft is { IsHeld: true } heldLeft)
            {
                volumeLeft = SnesFade.HeldAt(endLeft).FadingTo(heldLeft.To, heldLeft.Ticks);
            }

            if (fadeRight is { IsHeld: true } heldRight)
            {
                volumeRight = SnesFade.HeldAt(endRight).FadingTo(heldRight.To, heldRight.Ticks);
            }

            if (tick == 0)
            {
                for (int channel = 0; channel < Song.DriverChannelCount; channel++)
                {
                    song.SetBaseChannelEchoOn(channel, SnesDspRegister.HasVoiceBit(endMask, channel));
                }

                song.SetBaseEchoVolume(EchoSetting.VolumeLeft, endLeft);
                song.SetBaseEchoVolume(EchoSetting.VolumeRight, endRight);
                if (fadeLeft is not null || fadeRight is not null)
                {
                    collectedLeft.Add(EchoSideChange(0, endLeft, fadeLeft));
                    collectedRight.Add(EchoSideChange(0, endRight, fadeRight));
                }

                sendMask = endMask;
                continue;
            }

            for (int channel = 0; channel < Song.DriverChannelCount; channel++)
            {
                bool isEchoOnAtEnd = SnesDspRegister.HasVoiceBit(endMask, channel);
                if (isEchoOnAtEnd != SnesDspRegister.HasVoiceBit(sendMask, channel))
                {
                    song.SetChannelEchoChange(channel, tick, isEchoOnAtEnd ? Song.ChannelEchoOn : Song.ChannelEchoOff);
                }
            }

            if (isDividerWritten)
            {
                collectedLeft.Add(EchoSideChange(tick, endLeft, fadeLeft));
                collectedRight.Add(EchoSideChange(tick, endRight, fadeRight));
            }

            sendMask = endMask;
        }

        WarnAboutFades(inexactHeldTicks, EchoVolumeLaneName, ImportWarnings.SetsInsideFades, warn);
        WarnAboutFades(cappedTicks, EchoVolumeLaneName, ImportWarnings.FadesStoppedAtRangeEnd, warn);
        SetEchoVolumeRegions(song, collectedLeft, collectedRight, warn);
    }

    // The fade one echo volume side's divider holds at the end of a tick: the fade armed there where no set was written behind it, the fade the driver goes on
    // running from where the side now sits where a set was written inside one or the side's fade carries on under a divider, and none where the side is flat.
    private static EchoSideFade? FadeForEchoSide(SnesFade volume, bool isSetInsideFade, (int Ticks, int To)? armedSideFade)
    {
        if (!isSetInsideFade && armedSideFade is { } armed)
        {
            return new EchoSideFade(armed.Ticks, armed.To, IsHeld: false, IsCapped: false, IsExact: true);
        }

        if (!volume.IsFading || volume.HasNoAdditionLeft)
        {
            return null;
        }

        int finalValue16 = volume.Value16 + volume.Delta16 * (volume.TicksLeft - 1);
        bool isCarriedPastRange = finalValue16 < SnesFade.HeldAt(SnesEcho.MinVolume).Value16 || finalValue16 >= SnesFade.HeldAt(SnesEcho.MaxVolume + 1).Value16;
        if (isCarriedPastRange)
        {
            int rangeEnd = volume.Delta16 > 0 ? SnesEcho.MaxVolume : SnesEcho.MinVolume;
            return new EchoSideFade(NearestStepTicks(volume, rangeEnd), rangeEnd, IsHeld: true, IsCapped: true, IsExact: false);
        }

        // The target whose step over the ticks the fade has left, divided the way the driver divides it, comes nearest the driver's own.
        int heldTo = Enumerable.Range(SnesEcho.MinVolume, SnesEcho.MaxVolume - SnesEcho.MinVolume + 1).MinBy(to => StepMismatch(volume, to, volume.TicksLeft));

        bool isExact = volume.Fraction == 0 && StepMismatch(volume, heldTo, volume.TicksLeft) == 0;
        return new EchoSideFade(volume.TicksLeft, heldTo, IsHeld: true, IsCapped: false, IsExact: isExact);
    }

    // One echo volume side's divider at a tick, carrying the fade it holds or none; a held fade is marked as set inside the fade it cuts, which is warned about.
    private static CollectedRegionCommand EchoSideChange(int tick, int value, EchoSideFade? fade)
    {
        return new CollectedRegionCommand(tick, value, fade?.Ticks ?? SnesFade.NoFade, fade?.To ?? 0, default, IsSetInsideFade: fade is { IsHeld: true });
    }

    // Lays each echo volume's collected commands out as regions and writes the pair as the lane's dividers, a warning both sides give being given once.
    private static void SetEchoVolumeRegions(Song song, List<CollectedRegionCommand> collectedLeft, List<CollectedRegionCommand> collectedRight, Action<string>? warn)
    {
        var dividersLeft = new List<LaneDivider<int>>();
        var dividersRight = new List<LaneDivider<int>>();
        var warnedMessages = new HashSet<string>();

        Action<string>? warnOnce = warn is null ? null : message =>
        {
            if (warnedMessages.Add(message))
            {
                warn(message);
            }
        };

        ApplyRegions(collectedLeft, EchoVolumeLaneName, (tick, value, doesFade, fadeTo, _) => dividersLeft.Add(new(tick, value, doesFade, fadeTo)), warnOnce);
        ApplyRegions(collectedRight, EchoVolumeLaneName, (tick, value, doesFade, fadeTo, _) => dividersRight.Add(new(tick, value, doesFade, fadeTo)), warnOnce);
        song.SetEchoVolumeChanges(SplitAtOtherSidesDividers(dividersLeft, dividersRight), SplitAtOtherSidesDividers(dividersRight, dividersLeft));
    }

    // One echo volume side's dividers with each fading region split at every divider the other side has inside it, at the value its ramp has reached there, so the pair divides at the same ticks.
    private static List<LaneDivider<int>> SplitAtOtherSidesDividers(List<LaneDivider<int>> dividers, List<LaneDivider<int>> otherSidesDividers)
    {
        var splitDividers = new List<LaneDivider<int>>();
        for (int dividerIndex = 0; dividerIndex < dividers.Count; dividerIndex++)
        {
            var divider = dividers[dividerIndex];
            if (!divider.Fades || dividerIndex + 1 == dividers.Count)
            {
                splitDividers.Add(divider);
                continue;
            }

            int regionEndTick = dividers[dividerIndex + 1].Tick;
            var ramp = SnesFade.HeldAt(divider.Value).FadingTo(divider.FadeTo, regionEndTick - divider.Tick);
            var piece = divider;
            foreach (int splitTick in otherSidesDividers.Select(otherDivider => otherDivider.Tick).Where(tick => tick > divider.Tick && tick < regionEndTick))
            {
                int reachedValue = ramp.Advanced(splitTick - divider.Tick).Value;
                splitDividers.Add(piece with { FadeTo = reachedValue });
                piece = new LaneDivider<int>(splitTick, reachedValue, true, divider.FadeTo);
            }

            splitDividers.Add(piece);
        }

        return splitDividers;
    }

    // The $F1 commands, the $F0s, the $F5s, the $FA $04s, the noise pushes and the writes to the unit's own registers and to FLG, walked together in the driver's order into the echo unit's runs.
    private static void ApplyEchoUnit(
        Song song, List<(MmlEchoCommand Written, int Channel, int Order)> unitCommands, List<(MmlEchoCommand Written, int Channel, int Order)> offCommands,
        List<(MmlFirFilter Written, int Channel, int Order)> firFilters, List<(MmlEchoDelay Written, int Channel, int Order)> echoDelays,
        List<(MmlDspWrite Written, int Channel, int Order)> unitRegisterWrites, List<(MmlDspWrite Written, int Channel, int Order)> flagsWrites,
        List<(int Tick, int Channel, int Order, int Clock)> noisePushes, int? loopTick)
    {
        RefuseEchoSettingsOutOfRange(unitCommands, echoDelays);

        // Every unit command, echo off, filter, delay, register write and noise push, in the driver's order, marked where it pushes FLG.
        var unitEvents = unitCommands
            .Select(entry => (entry.Written.Tick, entry.Channel, entry.Order,
                Unit: (MmlEchoCommand?)entry.Written, Off: (MmlEchoCommand?)null, Fir: (MmlFirFilter?)null, Delay: (MmlEchoDelay?)null, RegisterWrite: (MmlDspWrite?)null,
                IsFlagsPush: false))
            .Concat(offCommands.Select(entry => (entry.Written.Tick, entry.Channel, entry.Order,
                Unit: (MmlEchoCommand?)null, Off: (MmlEchoCommand?)entry.Written, Fir: (MmlFirFilter?)null, Delay: (MmlEchoDelay?)null, RegisterWrite: (MmlDspWrite?)null,
                IsFlagsPush: false)))
            .Concat(firFilters.Select(entry => (entry.Written.Tick, entry.Channel, entry.Order,
                Unit: (MmlEchoCommand?)null, Off: (MmlEchoCommand?)null, Fir: (MmlFirFilter?)entry.Written, Delay: (MmlEchoDelay?)null, RegisterWrite: (MmlDspWrite?)null,
                IsFlagsPush: false)))
            .Concat(echoDelays.Select(entry => (entry.Written.Tick, entry.Channel, entry.Order,
                Unit: (MmlEchoCommand?)null, Off: (MmlEchoCommand?)null, Fir: (MmlFirFilter?)null, Delay: (MmlEchoDelay?)entry.Written, RegisterWrite: (MmlDspWrite?)null,
                IsFlagsPush: true)))
            .Concat(unitRegisterWrites.Concat(flagsWrites).Select(entry => (entry.Written.Tick, entry.Channel, entry.Order,
                Unit: (MmlEchoCommand?)null, Off: (MmlEchoCommand?)null, Fir: (MmlFirFilter?)null, Delay: (MmlEchoDelay?)null, RegisterWrite: (MmlDspWrite?)entry.Written,
                IsFlagsPush: false)))
            .Concat(noisePushes.Select(entry => (entry.Tick, entry.Channel, entry.Order,
                Unit: (MmlEchoCommand?)null, Off: (MmlEchoCommand?)null, Fir: (MmlFirFilter?)null, Delay: (MmlEchoDelay?)null, RegisterWrite: (MmlDspWrite?)null,
                IsFlagsPush: true)))
            .OrderBy(unitEvent => DriverReadOrder.Key(unitEvent.Tick, unitEvent.Channel, unitEvent.Order));

        bool isBufferFrozen = false;
        bool wasBufferFrozenAtLoop = false;
        int? firstNoteTick = Enumerable.Range(0, Song.DriverChannelCount).SelectMany(song.Notes).Select(note => (int?)note.StartTick).Min();
        bool isBeforeFirstUnitOrOff = true;
        var echoRuns = new EchoRunsBuilder(song);
        foreach (var tickEvents in unitEvents.GroupBy(unitEvent => unitEvent.Tick).OrderBy(group => group.Key))
        {
            int tick = tickEvents.Key;
            int endDelay = echoRuns.Delay;
            int endFeedback = echoRuns.Feedback;
            long endCoefficients = echoRuns.Coefficients;
            bool isRunningAtEnd = echoRuns.IsRunning;
            foreach (var unitEvent in tickEvents)
            {
                if (unitEvent.IsFlagsPush && isBufferFrozen)
                {
                    (isBufferFrozen, isRunningAtEnd) = (false, true);
                }

                if (unitEvent.Off is not null)
                {
                    (isRunningAtEnd, isBufferFrozen, isBeforeFirstUnitOrOff) = (false, false, false);
                }
                else if (unitEvent.Unit is { } unitCommand)
                {
                    (endDelay, endFeedback, endCoefficients) = (unitCommand.FirstArgument, unitCommand.SecondArgument, SnesEcho.PackedPresets[unitCommand.ThirdArgument]);
                    (isRunningAtEnd, isBufferFrozen, isBeforeFirstUnitOrOff) = (true, false, false);
                }
                else if (unitEvent.Fir is { } firFilter)
                {
                    endCoefficients = firFilter.PackedCoefficients;
                }
                else if (unitEvent.RegisterWrite is { } flagsWrite && SnesDspRegister.Classify(flagsWrite.Register) == DspRegisterKind.Flags)
                {
                    bool isEchoWriteOff = (flagsWrite.Value & SnesDspRegister.FlagsEchoWriteOff) != 0;
                    if (isEchoWriteOff && isRunningAtEnd)
                    {
                        if (firstNoteTick < tick)
                        {
                            throw MmlImportException.FlagsWriteStopsUsedEcho(tick, flagsWrite.Line);
                        }

                        (isRunningAtEnd, isBufferFrozen) = (false, true);
                    }
                    else if (!isEchoWriteOff && isBufferFrozen)
                    {
                        (isRunningAtEnd, isBufferFrozen) = (true, false);
                    }
                    else if (!isEchoWriteOff && !isRunningAtEnd && !isBeforeFirstUnitOrOff)
                    {
                        throw MmlImportException.FlagsWriteStartsEchoWithoutEcho(tick, flagsWrite.Line);
                    }
                }
                else if (unitEvent.RegisterWrite is { } registerWrite)
                {
                    if (SnesDspRegister.Classify(registerWrite.Register) == DspRegisterKind.EchoFeedback)
                    {
                        endFeedback = registerWrite.Value;
                    }
                    else
                    {
                        endCoefficients = SnesEcho.WithCoefficient(endCoefficients, SnesDspRegister.FirTapOf(registerWrite.Register), registerWrite.Value);
                    }
                }
                else if (unitEvent.Delay is { } delayCommand)
                {
                    endDelay = delayCommand.Delay;
                }
            }

            echoRuns.Reach(tick, isRunningAtEnd, endDelay, endFeedback, endCoefficients);
            if (tick <= loopTick)
            {
                wasBufferFrozenAtLoop = isBufferFrozen;
            }
        }

        if (loopTick is not null && isBufferFrozen != wasBufferFrozenAtLoop)
        {
            var lastFlagsWrite = flagsWrites.OrderBy(entry => DriverReadOrder.Key(entry.Written.Tick, entry.Channel, entry.Order)).Last().Written;
            throw MmlImportException.FlagsWriteStopsEchoOnlyAtEnd(lastFlagsWrite.Line);
        }

        echoRuns.WriteToSong();
    }

    // Refuses an $F1 or an $FA $04 whose echo delay, or an $F1 whose filter index, the model's range cannot hold.
    private static void RefuseEchoSettingsOutOfRange(List<(MmlEchoCommand Written, int Channel, int Order)> unitCommands, List<(MmlEchoDelay Written, int Channel, int Order)> echoDelays)
    {
        foreach (var (delayCommand, _, _) in echoDelays)
        {
            RefuseEchoDelayOutOfRange("$FA $04", delayCommand.Delay, delayCommand.Line);
        }

        foreach (var (unitCommand, _, _) in unitCommands)
        {
            RefuseEchoDelayOutOfRange("$F1", unitCommand.FirstArgument, unitCommand.Line);
            if (unitCommand.ThirdArgument < SnesEcho.MinFilter || unitCommand.ThirdArgument > SnesEcho.MaxFilter)
            {
                throw MmlImportException.EchoFilterPastPresets(unitCommand.ThirdArgument, unitCommand.Line);
            }
        }
    }

    // Refuses an echo delay outside the driver's own range, naming the command that wrote it.
    private static void RefuseEchoDelayOutOfRange(string commandName, int delay, int line)
    {
        if (!SnesEcho.DelayRange.Contains(delay))
        {
            throw MmlImportException.EchoDelayOutOfRange(commandName, delay, line);
        }
    }

    // The fade one echo volume side's divider holds at a tick: its length and target, whether it is held for a fade the driver goes on running rather than one
    // armed there, whether it stops at the end of the volume's range the driver would carry the side past, and whether the driver plays it exactly.
    private readonly record struct EchoSideFade(int Ticks, int To, bool IsHeld, bool IsCapped, bool IsExact);

    // One command or write that reaches the echo send, in the driver's order: an $EF or $F0, a $F2 fade, a write to an echo volume, or a $F4 $03
    // toggle where it holds none of the three.
    private readonly record struct EchoSendEvent(int Tick, int Channel, int Order, MmlEchoCommand? Command, MmlEchoFade? Fade, MmlDspWrite? VolumeWrite);
}
