using System.Collections.Immutable;
using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// Turns a channel's $FB arpeggios into the notes they sound as.
internal static class MmlArpeggioExpansion
{
    private const int TrillArpType = 1;
    private const int GlissandoArpType = 2;
    private const int SpecialNoteCount = 2;
    private const int LoopPointDelta = 0x80;
    private const int DefaultQuantization = 0x00;

    // Every channel with its arpeggios walked out into notes, warning where an effect runs past the song's end or a step's timing is another channel's.
    public static IReadOnlyList<MmlChannel> ExpandChannels(MmlFile file, Action<string>? warn)
    {
        if (!file.Channels.Any(channel => channel.Events.Any(e => e is MmlArpeggio)))
        {
            return file.Channels;
        }

        RefuseHotPatched(file);
        var waitTimeAt = MmlRemoteExpansion.WaitTimeLookup(file);
        var keyOffCheckTicksByChannel = file.Channels.ToDictionary(channel => channel.Number, channel => KeyOffCheckTicks(channel, waitTimeAt));

        // Whether a channel other than this one checks after this channel's own check at one tick and before its arpeggio step at another, the channels being walked from #0 up within a tick.
        bool DoOtherChannelsCheck(int channelNumber, int fromTick, int toTick)
        {
            foreach (var (otherNumber, otherTicks) in keyOffCheckTicksByChannel)
            {
                if (otherNumber == channelNumber)
                {
                    continue;
                }

                for (int tick = Math.Max(fromTick, 0); tick <= toTick; tick++)
                {
                    if (otherTicks.Contains(tick) && (tick > fromTick || otherNumber > channelNumber) && (tick < toTick || otherNumber < channelNumber))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return [.. file.Channels.Select(channel => ExpandChannel(
            channel, file.DoesLoop, waitTimeAt, (fromTick, toTick) => DoOtherChannelsCheck(channel.Number, fromTick, toTick), warn))];
    }

    // Refuses a song with an $FB inside a remote code block.
    public static void RefuseInRemoteCode(MmlFile file)
    {
        foreach (var block in file.RemoteBlocks.Values)
        {
            foreach (var blockEvent in block)
            {
                if (blockEvent is MmlArpeggio arpeggio)
                {
                    throw MmlImportException.ArpeggioInsideRemoteBlock(arpeggio.Line);
                }
            }
        }
    }

    // Refuses the remote code an arpeggio's key-off check would disagree with: a type 2 or 5 anywhere in the song, or a type 3 or 6 armed on the arpeggio's own channel while it runs.
    public static void RefuseBesideRemoteCode(MmlFile file)
    {
        if (!file.Channels.Any(channel => channel.Events.Any(e => e is MmlArpeggio { Form: not MmlArpeggio.OffForm })))
        {
            return;
        }

        var beforeKeyOffCall = file.Channels
            .SelectMany(channel => channel.Events)
            .OfType<MmlRemoteCall>()
            .FirstOrDefault(call => MmlRemoteCall.IsBeforeKeyOffType(call.TriggerType));
        if (beforeKeyOffCall is not null)
        {
            throw MmlImportException.ArpeggioWithRemoteType(beforeKeyOffCall.TriggerType, beforeKeyOffCall.Line);
        }

        foreach (var channel in file.Channels)
        {
            bool isArpeggioRunning = false;
            var keyOffArming = default(KeyOffArming);
            foreach (var channelEvent in channel.Events)
            {
                if (channelEvent is MmlArpeggio arpeggio)
                {
                    isArpeggioRunning = arpeggio.IsRunning;
                }

                if (channelEvent is MmlRemoteCall call)
                {
                    keyOffArming = keyOffArming.After(call);
                }

                if (isArpeggioRunning && keyOffArming.IsArmed)
                {
                    throw MmlImportException.ArpeggioUnderKeyOffArm(channel.Number, channelEvent.Line);
                }
            }
        }
    }

    // Refuses a song that both uses $FB and hot-patches the driver.
    private static void RefuseHotPatched(MmlFile file)
    {
        var hotPatch = file.Channels
            .SelectMany(channel => channel.Events)
            .OfType<MmlIgnored>()
            .FirstOrDefault(ignored => ignored.Command is IgnoredCommand.HotPatchPreset or IgnoredCommand.HotPatchBits or IgnoredCommand.ReadAheadHotPatch);
        if (hotPatch is not null)
        {
            throw MmlImportException.ArpeggioWithHotPatch(hotPatch.Line);
        }
    }

    // One channel's stream with its arpeggios replaced by what they sound as, warning where the song cannot carry what the driver does.
    private static MmlChannel ExpandChannel(MmlChannel channel, bool doesLoop, Func<int, int> waitTimeAt, Func<int, int, bool> doOtherChannelsCheck, Action<string>? warn)
    {
        if (!channel.Events.Any(e => e is MmlArpeggio))
        {
            return channel;
        }

        var walk = new ChannelWalk(channel, waitTimeAt, doOtherChannelsCheck);
        var durationBytes = DurationBytes(channel);
        for (int byteIndex = 0; byteIndex < durationBytes.Count; byteIndex++)
        {
            bool isNextByteTie = byteIndex + 1 < durationBytes.Count && durationBytes[byteIndex + 1].Kind == DurationByteKind.Tie;
            walk.FetchByte(durationBytes[byteIndex]);
            walk.PlayByte(durationBytes[byteIndex], isNextByteTie);
        }

        walk.CloseStep(channel.EndTick);
        if (walk.SharedReadTick is not null)
        {
            warn?.Invoke(ImportWarnings.ArpeggioTimingFromOtherChannel(channel.Number));
        }

        if (doesLoop && walk.Registers.NoteCount != 0)
        {
            warn?.Invoke(ImportWarnings.ArpeggioRunningAtSongEnd(channel.Number));
        }

        return channel with { Events = EventsWithSteps(channel, walk.Steps, walk.SilencedNoteTicks, walk.ToggleTicks, waitTimeAt) };
    }

    // The note value a step sounds at: the base note's own plus the delta as the signed byte the driver adds at key-on.
    private static int SoundedNoteValue(MmlNote baseNote, int delta)
    {
        return baseNote.NoteValue + (sbyte)delta;
    }

    // The channel's events with the arpeggio commands gone, the notes the effect silenced gone, and its steps in their place as notes that sound for exactly their key-on.
    private static List<MmlEvent> EventsWithSteps(
        MmlChannel channel, List<Step> steps, HashSet<int> silencedNoteTicks, IReadOnlyList<int> toggleTicks, Func<int, int> waitTimeAt)
    {
        List<(MmlEvent Event, bool IsStep)> unsortedEvents = [];
        foreach (var channelEvent in channel.Events)
        {
            if (channelEvent is MmlArpeggio || (channelEvent is MmlNote silencedNote && silencedNoteTicks.Contains(silencedNote.Tick)))
            {
                continue;
            }

            unsortedEvents.Add((channelEvent, false));
        }

        foreach (var step in steps)
        {
            int quantization = Note.QuantizationOf(SnesTiming.DefaultGate, Note.VelocityOfQuantization(step.BaseNote.Quantization));
            var stepNote = new MmlNote(step.Tick, step.NoteValue, step.KeyOnTicks, quantization, step.BaseNote.Line) { FineTune = step.BaseNote.FineTune };
            unsortedEvents.Add((stepNote, true));
        }

        var sortedEvents = unsortedEvents.OrderBy(entry => entry.Event.Tick).ThenBy(entry => entry.Event is MmlNote ? 1 : 0).ToList();
        var events = sortedEvents.Select(entry => entry.Event).ToList();
        var noteIndices = Enumerable.Range(0, events.Count).Where(eventIndex => events[eventIndex] is MmlNote).ToList();
        var (isBitSetUnder, isBound) = LegatoBits.WalkNotes(
            [.. noteIndices.Select(eventIndex => (events[eventIndex].Tick, events[eventIndex].Tick + ((MmlNote)events[eventIndex]).LengthTicks))],
            toggleTicks);
        for (int noteNumber = 0; noteNumber < noteIndices.Count; noteNumber++)
        {
            int eventIndex = noteIndices[noteNumber];
            if (!sortedEvents[eventIndex].IsStep)
            {
                continue;
            }

            var stepNote = (MmlNote)events[eventIndex];
            int stepEndTick = stepNote.EndTick;
            bool isLastNote = noteNumber + 1 == noteIndices.Count;
            if (!isBound[noteNumber] && (isLastNote || !isBound[noteNumber + 1]))
            {
                int? reachTick = !isLastNote && isBitSetUnder[noteNumber + 1]
                    ? MmlRemoteExpansion.FurthestGrowthTickUnderLegato(events, eventIndex, stepEndTick, channel.EndTick, events[noteIndices[noteNumber + 1]].Tick)
                    : MmlRemoteExpansion.FurthestGrowthTick(events, eventIndex, stepEndTick, channel.EndTick);
                stepNote = stepNote.RewrittenToSound(stepNote.LengthTicks, reachTick, waitTimeAt);
            }

            events[eventIndex] = stepNote with { Pieces = [.. MmlDurationEncoding.SplitRunIntoTiedPieces(stepNote.LengthTicks)] };
        }

        return events;
    }

    // The channel's stream as the driver's duration bytes, in tick order: the notes, the ties their lengths are split into, and the rests between them.
    private static List<DurationByte> DurationBytes(MmlChannel channel)
    {
        List<DurationByte> durationBytes = [];
        int cursorTick = 0;
        bool isArpeggioRunning = false;

        // Lays a run of bytes from a tick, the first of the kind given and the rest ties.
        void LayBytes(int startTick, IReadOnlyList<int> byteLengths, DurationByteKind firstKind, MmlNote? note)
        {
            int byteTick = startTick;
            foreach (int byteTicks in byteLengths)
            {
                durationBytes.Add(new DurationByte(byteTick, byteTicks, byteTick == startTick ? firstKind : DurationByteKind.Tie, note));
                byteTick += byteTicks;
            }
        }

        foreach (var channelEvent in channel.Events)
        {
            if (channelEvent.Tick > cursorTick)
            {
                LayBytes(cursorTick, MmlDurationEncoding.SplitRunIntoTiedPieces(channelEvent.Tick - cursorTick), DurationByteKind.Rest, null);
                cursorTick = channelEvent.Tick;
            }

            if (channelEvent is MmlArpeggio arpeggio)
            {
                isArpeggioRunning = arpeggio.IsRunning;
            }

            if (channelEvent is not MmlNote note)
            {
                continue;
            }

            if (isArpeggioRunning && !note.Bends.IsDefaultOrEmpty)
            {
                throw MmlImportException.ArpeggioNoteWithBend(channel.Number, note.Tick, note.Line);
            }

            LayBytes(note.Tick, note.Pieces, DurationByteKind.Note, note);
            cursorTick = note.EndTick;
        }

        if (channel.EndTick > cursorTick)
        {
            LayBytes(cursorTick, MmlDurationEncoding.SplitRunIntoTiedPieces(channel.EndTick - cursorTick), DurationByteKind.Rest, null);
        }

        return durationBytes;
    }

    // Whether the driver checks for a key-off that many ticks into a byte: where the q gate's counter runs out, and where the duration counter meets the
    // wait time - never on the tick that fetched the byte.
    private static bool DoesCheckKeyOff(int ticksIntoByte, int gateTicks, int ticksLeftInByte, int waitTime)
    {
        return ticksIntoByte > 0 && (ticksIntoByte == gateTicks || ticksLeftInByte == waitTime);
    }

    // The tick of a byte's q gate cut, counted from its fetch, under the rate the last note set.
    private static int GateTicksOf(DurationByte durationByte, MmlNote? lastNote)
    {
        return SnesTiming.GateCounterTicks(durationByte.LengthTicks, Note.GateOfQuantization(lastNote?.Quantization ?? DefaultQuantization));
    }

    // Every tick a channel checks for a key-off on.
    private static HashSet<int> KeyOffCheckTicks(MmlChannel channel, Func<int, int> waitTimeAt)
    {
        HashSet<int> checkTicks = [];
        MmlNote? lastNote = null;
        foreach (var durationByte in DurationBytes(channel))
        {
            if (durationByte.Kind == DurationByteKind.Note)
            {
                lastNote = durationByte.Note;
            }

            int gateTicks = GateTicksOf(durationByte, lastNote);
            for (int ticksIntoByte = 1; ticksIntoByte < durationByte.LengthTicks; ticksIntoByte++)
            {
                if (DoesCheckKeyOff(ticksIntoByte, gateTicks, durationByte.LengthTicks - ticksIntoByte, waitTimeAt(durationByte.Tick + ticksIntoByte)))
                {
                    checkTicks.Add(durationByte.Tick + ticksIntoByte);
                }
            }
        }

        return checkTicks;
    }

    // The line of the channel's first arpeggio, for a failure that belongs to no one note.
    private static int FirstArpeggioLine(MmlChannel channel)
    {
        return channel.Events.OfType<MmlArpeggio>().FirstOrDefault()?.Line ?? 0;
    }

    // What a duration byte covers, and which of the three kinds of byte it is.
    private readonly record struct DurationByte(int Tick, int LengthTicks, DurationByteKind Kind, MmlNote? Note);

    // The three kinds of duration byte, of which only a note restarts the effect.
    private enum DurationByteKind
    {
        Note,
        Tie,
        Rest,
    }

    // One step the effect sounded: where it keyed on, at what pitch, for how long, and the note it was worked out from.
    private readonly record struct Step(int Tick, int NoteValue, int KeyOnTicks, MmlNote BaseNote);

    // The driver's seven arpeggio registers, and the two things that change them: a command, and a step.
    private sealed class ArpeggioRegisters
    {
        public int ArpType { get; private set; }
        public int StepTicks { get; private set; }
        public int TimeLeft { get; set; }
        public int Special { get; private set; }
        public int NoteIndex { get; set; }
        public int CurrentDelta { get; set; }
        public int NoteCount { get; set; }
        public ImmutableArray<int> Deltas { get; private set; } = [];

        // An $FB's own writes, and nothing else.
        public void Load(MmlArpeggio command)
        {
            StepTicks = command.StepTicks;
            TimeLeft = command.StepTicks;
            CurrentDelta = 0;
            if (command.Form >= MmlArpeggio.FirstSpecialForm)
            {
                ArpType = command.ArpType;
                NoteCount = SpecialNoteCount;
                Special = command.Deltas[0];
                return;
            }

            ArpType = MmlArpeggio.NoteListArpType;
            NoteCount = command.Form;
            Special = 0;
            NoteIndex = 0;
            Deltas = command.Deltas;
        }

        // The delta the next step plays at: set from the note list, toggled by the trill, or added by the glissando.
        public void AdvanceDelta(MmlChannel channel, int failureLine)
        {
            if (ArpType == TrillArpType)
            {
                CurrentDelta ^= Special;
                return;
            }

            if (ArpType != MmlArpeggio.NoteListArpType)
            {
                CurrentDelta = (CurrentDelta + Special) & byte.MaxValue;
                return;
            }

            for (int guard = 0; guard <= Deltas.Length; guard++)
            {
                int nextIndex = NoteIndex + 1;
                if (nextIndex == NoteCount)
                {
                    nextIndex = Special;
                }

                NoteIndex = nextIndex;
                if (nextIndex < 0 || nextIndex >= Deltas.Length)
                {
                    throw MmlImportException.ArpeggioListShort(channel.Number, failureLine);
                }

                if (Deltas[nextIndex] != LoopPointDelta)
                {
                    CurrentDelta = Deltas[nextIndex];
                    return;
                }

                Special = nextIndex + 1;
            }

            throw MmlImportException.ArpeggioListOnlyLoopPoints(channel.Number, failureLine);
        }
    }

    // One channel walked a duration byte and a tick at a time, as the driver plays its arpeggios, collecting the steps they sound.
    private sealed class ChannelWalk
    {
        private readonly MmlChannel _channel;
        private readonly Func<int, int> _waitTimeAt;
        private readonly Func<int, int, bool> _doOtherChannelsCheck;
        private readonly Dictionary<int, List<MmlArpeggio>> _arpeggiosByTick;
        private MmlNote? _lastNote;
        private int _gateTicks;
        private bool _doesLastCheckPause = true;
        private int _lastCheckTick = -1;
        private int? _openStepTick;
        private int _openStepNoteValue;
        private MmlNote? _openStepBaseNote;
        private int? _openStepOffTick;
        private LegatoBits _legatoBits;
        private int _nextToggleIndex;

        // A walk at the channel's start, every register at the song load's zero.
        public ChannelWalk(MmlChannel channel, Func<int, int> waitTimeAt, Func<int, int, bool> doOtherChannelsCheck)
        {
            _channel = channel;
            _waitTimeAt = waitTimeAt;
            _doOtherChannelsCheck = doOtherChannelsCheck;
            _arpeggiosByTick = channel.Events.OfType<MmlArpeggio>().GroupBy(arpeggio => arpeggio.Tick).ToDictionary(group => group.Key, group => group.ToList());
            ToggleTicks = LegatoBits.ToggleTicks(channel.Events);
        }

        public ArpeggioRegisters Registers { get; } = new();
        public List<int> ToggleTicks { get; }
        public List<Step> Steps { get; } = [];
        public HashSet<int> SilencedNoteTicks { get; } = [];
        public int? SharedReadTick { get; private set; }

        // Ends the step still sounding at a tick, or at the tick it was keyed off at if it has been.
        public void CloseStep(int endTick)
        {
            if (_openStepTick is { } stepTick && _openStepBaseNote is { } baseNote)
            {
                Steps.Add(new Step(stepTick, _openStepNoteValue, (_openStepOffTick ?? endTick) - stepTick, baseNote));
            }

            _openStepTick = null;
            _openStepOffTick = null;
        }

        // A duration byte's fetch: the commands read on its tick, the toggles before it, a rest's key-off, and a note's interruption of the effect.
        public void FetchByte(DurationByte durationByte)
        {
            if (_arpeggiosByTick.TryGetValue(durationByte.Tick, out var commands))
            {
                foreach (var command in commands)
                {
                    Registers.Load(command);
                }
            }

            for (; _nextToggleIndex < ToggleTicks.Count && ToggleTicks[_nextToggleIndex] <= durationByte.Tick; _nextToggleIndex++)
            {
                _legatoBits = _legatoBits.Toggle();
            }

            if (durationByte.Kind == DurationByteKind.Rest)
            {
                if (_openStepTick is not null)
                {
                    _openStepOffTick ??= durationByte.Tick;
                }

                _legatoBits = _legatoBits.FetchRest();
            }

            if (durationByte.Kind == DurationByteKind.Note)
            {
                FetchNote(durationByte);
            }

            _gateTicks = GateTicksOf(durationByte, _lastNote);
        }

        // Every tick of a duration byte after its fetch: the key-off checks, and the effect's countdown to its next step.
        public void PlayByte(DurationByte durationByte, bool isNextByteTie)
        {
            for (int ticksIntoByte = 0; ticksIntoByte < durationByte.LengthTicks; ticksIntoByte++)
            {
                PlayTick(durationByte.Tick + ticksIntoByte, ticksIntoByte, durationByte.LengthTicks - ticksIntoByte, isNextByteTie);
            }
        }

        // A note byte's fetch, which the effect takes over - silencing the note - unless it has run out, and which otherwise keys on over the step left.
        private void FetchNote(DurationByte durationByte)
        {
            _lastNote = durationByte.Note;
            bool doesNotePlay = true;
            if (Registers.NoteCount != 0)
            {
                Registers.TimeLeft = 1;
                Registers.NoteIndex = -1;
                if (Registers.ArpType == GlissandoArpType && --Registers.NoteCount == 0)
                {
                    Registers.CurrentDelta = 0;
                    CloseStep(durationByte.Tick);
                }
                else
                {
                    SilencedNoteTicks.Add(durationByte.Tick);
                    doesNotePlay = false;
                }
            }

            if (doesNotePlay)
            {
                CloseStep(durationByte.Tick);
                _legatoBits = _legatoBits.KeyOn();
            }
        }

        // One tick inside a byte: a key-off check where one falls, and the effect's countdown, keying the next step on where it runs out.
        private void PlayTick(int tick, int ticksIntoByte, int ticksLeftInByte, bool isNextByteTie)
        {
            int waitTime = _waitTimeAt(tick);
            if (DoesCheckKeyOff(ticksIntoByte, _gateTicks, ticksLeftInByte, waitTime))
            {
                bool doesKeyOff = !isNextByteTie && !_legatoBits.DoesAbandonCuts;
                _doesLastCheckPause = doesKeyOff;
                _lastCheckTick = tick;
                if (_openStepTick is not null && doesKeyOff)
                {
                    _openStepOffTick ??= tick;
                }
            }

            if (Registers.NoteCount == 0)
            {
                return;
            }

            if (waitTime < Registers.StepTicks && waitTime >= ticksLeftInByte)
            {
                if (_lastCheckTick != tick && _doOtherChannelsCheck(_lastCheckTick, tick))
                {
                    SharedReadTick ??= tick;
                }

                if (_doesLastCheckPause)
                {
                    return;
                }
            }

            if (--Registers.TimeLeft == 0)
            {
                KeyOnStep(tick);
            }
            else if (Registers.TimeLeft == waitTime && !_legatoBits.DoesAbandonCuts)
            {
                _openStepOffTick ??= tick;
            }
        }

        // The effect's countdown running out: the step sounding ends, and the next one keys on at the delta the registers step to.
        private void KeyOnStep(int tick)
        {
            CloseStep(tick);
            Registers.AdvanceDelta(_channel, _lastNote?.Line ?? 0);
            Registers.TimeLeft = Registers.StepTicks;
            var baseNote = _lastNote ?? throw MmlImportException.ArpeggioBeforeFirstNote(_channel.Number, FirstArpeggioLine(_channel));
            _openStepTick = tick;
            _openStepOffTick = null;
            _openStepBaseNote = baseNote;
            _openStepNoteValue = SoundedNoteValue(baseNote, Registers.CurrentDelta);
            _legatoBits = _legatoBits.KeyOn();
        }
    }
}
