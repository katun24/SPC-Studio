using SpcStudio.Model;

namespace SpcStudio.Import;

internal static partial class MmlRemoteExpansion
{
    // One channel's stream walked in order, its remote code calls replaced by what they do, holding the driver's two remote slots and the timer as it goes.
    private sealed class ChannelWalk
    {
        private readonly MmlChannel _channel;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<MmlEvent>> _blocks;
        private readonly bool _doesLoop;
        private readonly Func<int, int> _waitTimeAt;
        private readonly Action<string>? _warn;
        private readonly RestSplits _restSplits;
        private readonly List<MmlEvent> _output = [];
        private readonly List<MmlEvent> _pendingTimerFires = [];
        private RemoteSlots _slots;
        private int? _loopTick;
        private RemoteSlots _slotsAtLoop;
        private SlotSinceLoop _keyOnSlotSinceLoop;
        private SlotSinceLoop _ordinarySlotSinceLoop;
        private int _cursorTick;
        private int? _restGate;
        private bool _isLegatoBitSet;
        private int? _timerTicksLeft = 0;
        private int? _timerTicksLeftAtLoop;
        private bool _isTimerUnreloadedSinceLoop;
        private int? _timerCountedSinceLoopLine;
        private bool _didLastNoteRing;
        private int _lastSoundedEndTick;

        // A walk at the channel's start, both slots clear and the timer at the boot clear's zero.
        public ChannelWalk(
            MmlChannel channel, IReadOnlyDictionary<int, IReadOnlyList<MmlEvent>> blocks, bool doesLoop, Func<int, int> waitTimeAt, Action<string>? warn)
        {
            _channel = channel;
            _blocks = blocks;
            _doesLoop = doesLoop;
            _waitTimeAt = waitTimeAt;
            _warn = warn;
            _restSplits = RestSplits.ForChannel(channel);
        }

        // Whether the channel's last note is still sounding where the channel ends.
        private bool DoesLastNoteRingOnToEnd
        {
            get
            {
                return _lastSoundedEndTick >= _channel.EndTick;
            }
        }

        // The channel with its calls replaced, refused where the timer is counted across the loop point from a value the second pass does not start at.
        public MmlChannel Run()
        {
            for (int eventIndex = 0; eventIndex < _channel.Events.Count; eventIndex++)
            {
                var channelEvent = _channel.Events[eventIndex];
                FlushFiresBefore(channelEvent.Tick);
                if (channelEvent.Tick > _cursorTick)
                {
                    CountRestUnderTimer(_cursorTick, channelEvent.Tick, eventIndex, channelEvent.Line);
                    PlaceRestFires(_cursorTick, channelEvent.Tick, eventIndex, channelEvent.Line);
                    _cursorTick = channelEvent.Tick;
                }

                if (_loopTick is { } loopTick && channelEvent.Tick > loopTick)
                {
                    _keyOnSlotSinceLoop = _keyOnSlotSinceLoop.RestedFrom(loopTick);
                    _ordinarySlotSinceLoop = _ordinarySlotSinceLoop.RestedFrom(loopTick);
                }

                switch (channelEvent)
                {
                    case MmlLoopMark:
                        MarkLoop(channelEvent.Tick);
                        _output.Add(channelEvent);
                        break;

                    case MmlLegato:
                        _isLegatoBitSet = !_isLegatoBitSet;
                        _output.Add(channelEvent);
                        break;

                    case MmlRemoteCall call:
                        ReadCall(eventIndex, call);
                        break;

                    case MmlNote note:
                        ReadNote(eventIndex, note);
                        break;

                    default:
                        _output.Add(channelEvent);
                        break;
                }
            }

            FlushFiresBefore(int.MaxValue);
            if (_channel.EndTick > _cursorTick)
            {
                CountRestUnderTimer(_cursorTick, _channel.EndTick, _channel.Events.Count, _channel.Events[^1].Line);
                PlaceRestFires(_cursorTick, _channel.EndTick, _channel.Events.Count, _channel.Events[^1].Line);
            }

            RefuseTimerCountedAcrossLoop();
            WarnSlotsChangedAcrossLoop();
            return _channel with { Events = _output };
        }

        // Adds the held-back fires that land before a tick.
        private void FlushFiresBefore(int tick)
        {
            var dueFires = _pendingTimerFires.Where(fire => fire.Tick < tick).OrderBy(fire => fire.Tick).ToList();
            _output.AddRange(dueFires);
            _pendingTimerFires.RemoveAll(dueFires.Contains);
        }

        // Counts the timer down through a rest under a type 1 arm.
        private void CountRestUnderTimer(int fromTick, int toTick, int nextEventIndex, int line)
        {
            if (_slots.Ordinary.TriggerType != MmlRemoteCall.Timed)
            {
                return;
            }

            CountTimerThrough([.. RestPieces(_channel, nextEventIndex - 1, _restSplits, fromTick, toTick)], line);
        }

        // Counts the timer down through bytes fetched under a type 1 arm, or forgets its value where those bytes are not known.
        private void CountTimerThrough(IReadOnlyList<int>? pieces, int line)
        {
            if (_timerTicksLeft is not { } ticksLeft)
            {
                return;
            }

            if (pieces is null)
            {
                _timerTicksLeft = null;
                return;
            }

            _timerTicksLeft = TimerCountedDown(ticksLeft, pieces);
            if (_isTimerUnreloadedSinceLoop)
            {
                _timerCountedSinceLoopLine ??= line;
            }
        }

        // Places what an arm does in a rest: a type 2 or 5 fires in each rest byte, and a type 3 or 6 runs in a tie read as a rest straight after a note.
        private void PlaceRestFires(int fromTick, int toTick, int nextEventIndex, int line)
        {
            if (_slots.BeforeKeyOffArmLabel is { } beforeKeyOffLabel && _restGate is { } gate)
            {
                if (_slots.KeyOffArmLabel is not null && TiesAfterNoteIn(fromTick, toTick).Any())
                {
                    throw MmlImportException.TieAtLoopEdgeUnderRemoteCode(TiesAfterNoteIn(fromTick, toTick).First().Line);
                }

                foreach (var (byteStartTick, byteEndTick) in _restSplits.ByteRuns(fromTick, toTick))
                {
                    if (!_restSplits.IsHeldAt(byteEndTick))
                    {
                        var restByteKeyOffTriggers = SnesTiming.KeyOffTriggers.For(byteEndTick - byteStartTick, gate, null, offsetTicks => _waitTimeAt(byteStartTick + offsetTicks));
                        PlaceBlockAt(BeforeKeyOffTick(byteStartTick, restByteKeyOffTriggers, _slots.Ordinary.TimeTicks), BlockBody(_blocks, beforeKeyOffLabel, line), _output);
                    }
                }
            }

            if (_slots.KeyOffArmLabel is { } keyOffLabel)
            {
                foreach (var tie in TiesAfterNoteIn(fromTick, toTick))
                {
                    PlaceSilentTieFires(
                        _channel, nextEventIndex, tie, _restGate, _isLegatoBitSet, _lastSoundedEndTick, _waitTimeAt, _restSplits, BlockBody(_blocks, keyOffLabel, line),
                        _output);
                }
            }
        }

        // The ties read as a rest that start in a stretch of ticks with no rest byte fetched in front of them.
        private IEnumerable<MmlCutTie> TiesAfterNoteIn(int fromTick, int toTick)
        {
            return _channel.CutTies.Where(tie => !tie.IsAfterRest && tie.Tick >= fromTick && tie.Tick < toTick);
        }

        // The loop point: what both slots and the timer hold there, which the second pass is compared against.
        private void MarkLoop(int loopTick)
        {
            _loopTick = loopTick;
            _slotsAtLoop = _slots;
            _timerTicksLeftAtLoop = _timerTicksLeft;
            _isTimerUnreloadedSinceLoop = true;
        }

        // A remote code call: the slots it writes, an immediate call's events, and a type 1 arm's stale fires.
        private void ReadCall(int callIndex, MmlRemoteCall call)
        {
            if (call.DoesWriteKeyOnSlot)
            {
                _keyOnSlotSinceLoop = _keyOnSlotSinceLoop.Armed(_loopTick is not null);
            }

            if (call.DoesWriteOrdinarySlot)
            {
                _ordinarySlotSinceLoop = _ordinarySlotSinceLoop.Armed(_loopTick is not null);
            }

            switch (call.TriggerType)
            {
                case MmlRemoteCall.Immediate:
                    _output.AddRange(StampedAt(BlockBody(_blocks, call), call.Tick));
                    break;

                case MmlRemoteCall.KeyOff or MmlRemoteCall.Timed or MmlRemoteCall.BeforeKeyOff or MmlRemoteCall.BeforeKeyOffRestoring:
                    ReadTimerAtOrdinaryArm(callIndex, call);
                    break;

                case MmlRemoteCall.ClearBoth or MmlRemoteCall.ClearOrdinary:
                    ZeroTimer();
                    break;
            }

            _slots = _slots.After(call);
        }

        // What an arm of the ordinary slot does to the timer: a type 1's fires before the key-on that reloads it, and the value it leaves, a type 1 restating the slot in force being nothing at all.
        private void ReadTimerAtOrdinaryArm(int callIndex, MmlRemoteCall call)
        {
            if (IsTimerArmRestated(call, _slots.Ordinary.TriggerType, _slots.Ordinary.BlockLabel, _slots.Ordinary.TimeTicks))
            {
                return;
            }

            var noteAroundArm = call.Tick < _cursorTick ? LastNoteBefore(_channel, callIndex) : null;
            if (_slots.Ordinary.TriggerType == MmlRemoteCall.Timed && noteAroundArm is not null)
            {
                _timerTicksLeft = null;
            }

            if (call.TriggerType != MmlRemoteCall.Timed)
            {
                return;
            }

            PlaceStaleTimerFires(_channel, callIndex, call, _timerTicksLeft, noteAroundArm, _didLastNoteRing, _restSplits, BlockBody(_blocks, call), _pendingTimerFires);
            if (noteAroundArm is not null)
            {
                CountTimerThrough(noteAroundArm.PiecesBetween(call.Tick - noteAroundArm.Tick, noteAroundArm.LengthTicks), call.Line);
            }
        }

        // The timer zeroed by a clear of the ordinary slot.
        private void ZeroTimer()
        {
            _timerTicksLeft = 0;
            _isTimerUnreloadedSinceLoop = false;
        }

        // The two slots as a note's last byte is fetched, which is where its cuts ask them: every call inside the note written by then.
        private RemoteSlots SlotsAtLastByte(int noteIndex, MmlNote note)
        {
            var slots = _slots;
            foreach (var call in EventsInsideNote(_channel, noteIndex, note).OfType<MmlRemoteCall>())
            {
                slots = slots.After(call);
            }

            return slots;
        }

        // A note: the restore and the key-on block's events in front of it, a type 3 or 6 arm's rewrite of it, and the fires of the arms the timer drives.
        private void ReadNote(int noteIndex, MmlNote note)
        {
            if (_loopTick is not null)
            {
                _keyOnSlotSinceLoop = _keyOnSlotSinceLoop.ReachedAt(note.Tick);
                _ordinarySlotSinceLoop = _ordinarySlotSinceLoop.ReachedAt(note.Tick);
            }

            var slotsAtLastByte = SlotsAtLastByte(noteIndex, note);
            _didLastNoteRing = slotsAtLastByte.KeyOffArmLabel is not null;
            if (_slots.DoesRestoreAtKeyOn)
            {
                _output.Add(new MmlKeyOnRestore(note.Tick, note.Line));
            }

            if (_slots.KeyOn.TriggerType == MmlRemoteCall.KeyOn && _slots.KeyOn.BlockLabel is { } keyOnLabel)
            {
                _output.AddRange(StampedAt(BlockBody(_blocks, keyOnLabel, note.Line), note.Tick));
            }

            // The note as written, or as a type 3 or 6 arm rewrites it.
            var placedNote = slotsAtLastByte.KeyOffArmLabel is { } keyOffLabel
                ? NoteUnderKeyOffArm(
                    _channel, noteIndex, note, _isLegatoBitSet, slotsAtLastByte.KeyOffArming, _waitTimeAt, _restSplits, BlockBody(_blocks, keyOffLabel, note.Line), _output)
                : note;
            _output.Add(placedNote);

            if (_slots.Ordinary.TriggerType == MmlRemoteCall.Timed && _slots.Ordinary.BlockLabel is { } timedLabel)
            {
                PlaceTimerFiresFromKeyOn(
                    _channel, noteIndex, note, timedLabel, _slots.Ordinary.TimeTicks, _restSplits, BlockBody(_blocks, timedLabel, note.Line), _pendingTimerFires);
                _timerTicksLeft = TimerLeftAfterNote(note, _slots.Ordinary.TimeTicks);
                _isTimerUnreloadedSinceLoop = false;
            }

            if (slotsAtLastByte.BeforeKeyOffArmLabel is { } beforeKeyOffLabel && !_restSplits.IsHeldAt(note.EndTick))
            {
                var keyOffArmBody = slotsAtLastByte.KeyOffArmLabel is { } takingOverLabel ? BlockBody(_blocks, takingOverLabel, note.Line) : null;
                PlaceBeforeKeyOffFire(
                    _channel, noteIndex, note, slotsAtLastByte.Ordinary, _isLegatoBitSet, _waitTimeAt, _restSplits, BlockBody(_blocks, beforeKeyOffLabel, note.Line),
                    keyOffArmBody, _output);
            }

            _cursorTick = note.EndTick;
            _lastSoundedEndTick = placedNote.Tick + placedNote.SoundedTicks(Note.GateOfQuantization(placedNote.Quantization), null, _waitTimeAt);
            _restGate = Note.GateOfQuantization(note.Quantization);
        }

        // Refuses a timer count made after the loop point that holds for the first pass alone.
        private void RefuseTimerCountedAcrossLoop()
        {
            if (_doesLoop && _timerCountedSinceLoopLine is { } countedLine && (_timerTicksLeftAtLoop is null || _timerTicksLeft != _timerTicksLeftAtLoop))
            {
                throw MmlImportException.TimerDiffersAfterLoop(_channel.Number, countedLine);
            }
        }

        // Warns where the channel ends holding either slot otherwise than the loop point found it and the second pass reaches it before writing it again.
        private void WarnSlotsChangedAcrossLoop()
        {
            if (DoesOrdinaryArmActInOpeningRest())
            {
                _ordinarySlotSinceLoop = _ordinarySlotSinceLoop.ReachedInOpeningRest();
            }

            if (DoesKeyOnArmActInOpeningRest())
            {
                _keyOnSlotSinceLoop = _keyOnSlotSinceLoop.ReachedInOpeningRest();
            }

            // Each slot the channel ends holding otherwise than the loop point found it, where a note or a rest reaches it again.
            var staleSlots = new[]
                {
                    _slots.KeyOn != _slotsAtLoop.KeyOn ? _keyOnSlotSinceLoop : default,
                    _slots.Ordinary != _slotsAtLoop.Ordinary ? _ordinarySlotSinceLoop : default,
                }
                .Where(slot => _loopTick is not null && slot.FirstReachedTick is not null)
                .ToList();

            if (staleSlots.Count == 0)
            {
                return;
            }

            _warn?.Invoke(ImportWarnings.RemoteCodeChangedAcrossLoop(_channel.Number));
        }

        // Whether the ordinary slot's arm either pass meets in a rest the loop section opens on can act in it before the first note.
        private bool DoesOrdinaryArmActInOpeningRest()
        {
            bool doesCountInRests = DoesArmCountInRests(_slots.Ordinary);
            bool doesOrdinaryKeyOffArmRingOnToEnd = _slots.KeyOffArming.IsOrdinarySlotArmed && DoesLastNoteRingOnToEnd;
            return (_slots.Ordinary.BlockLabel is not null && (doesCountInRests || doesOrdinaryKeyOffArmRingOnToEnd))
                || (_slotsAtLoop.Ordinary.BlockLabel is not null && DoesArmCountInRests(_slotsAtLoop.Ordinary));
        }

        // Whether the key-on slot's arm the second pass meets in a rest the loop section opens on can act in it, which only a type 6 the song's last note rings on to the channel's end under can.
        private bool DoesKeyOnArmActInOpeningRest()
        {
            return _slots.KeyOffArming.IsKeyOnSlotArmed && DoesLastNoteRingOnToEnd;
        }

        // Whether an arm counts and fires in rests, which the timer's type and the types fired before the gate's cut do.
        private static bool DoesArmCountInRests(RemoteSlot slot)
        {
            return slot.TriggerType == MmlRemoteCall.Timed || MmlRemoteCall.IsBeforeKeyOffType(slot.TriggerType);
        }
    }

    // One remote slot: the block it arms, its trigger type and its tick count, all three clear together.
    private readonly record struct RemoteSlot(int? BlockLabel, int TriggerType, int TimeTicks);

    // A channel's two remote slots together, the key-on slot and the ordinary one, and what the arms they hold run.
    private readonly record struct RemoteSlots(RemoteSlot KeyOn, RemoteSlot Ordinary)
    {
        // The label of the block run instead of the key-off, a type 6 in the key-on slot taking it over from a type 3 in the ordinary one, or null where neither is armed.
        public int? KeyOffArmLabel
        {
            get
            {
                if (KeyOffArming.IsKeyOnSlotArmed)
                {
                    return KeyOn.BlockLabel;
                }

                return KeyOffArming.IsOrdinarySlotArmed ? Ordinary.BlockLabel : null;
            }
        }

        // Which of the two slots holds an arm run instead of the key-off.
        public KeyOffArming KeyOffArming
        {
            get
            {
                return new KeyOffArming(MmlRemoteCall.IsKeyOffType(Ordinary.TriggerType), MmlRemoteCall.IsKeyOffType(KeyOn.TriggerType));
            }
        }

        // The label of the block a type 2 or 5 arm fires before the gate's cut, or null where neither is armed.
        public int? BeforeKeyOffArmLabel
        {
            get
            {
                return MmlRemoteCall.IsBeforeKeyOffType(Ordinary.TriggerType) ? Ordinary.BlockLabel : null;
            }
        }

        // Whether a note keys on with the voice's envelope put back from the backup table, which a type 5 or a type 6 arm does.
        public bool DoesRestoreAtKeyOn
        {
            get
            {
                return MmlRemoteCall.IsRestoringType(Ordinary.TriggerType) || MmlRemoteCall.IsRestoringType(KeyOn.TriggerType);
            }
        }

        // The slots a call leaves, each slot it writes holding its arm, or nothing where it clears.
        public RemoteSlots After(MmlRemoteCall call)
        {
            var writtenSlot = call.DoesClear ? default : new RemoteSlot(call.BlockLabel, call.TriggerType, call.TimeTicks);
            return new RemoteSlots(call.DoesWriteKeyOnSlot ? writtenSlot with { TimeTicks = 0 } : KeyOn, call.DoesWriteOrdinarySlot ? writtenSlot : Ordinary);
        }
    }

    // What one remote slot has seen since the loop point: the first tick a note reached it at, whether a call has written it, and the loop point where a
    // rest opened the loop section before either.
    private readonly record struct SlotSinceLoop(int? FirstReachedTick, bool IsWritten, int? RestedTick = null)
    {
        // The slot met by a rest the loop section opens on, which counts only where nothing has written or reached the slot since the loop point.
        public SlotSinceLoop RestedFrom(int loopTick)
        {
            return IsWritten || FirstReachedTick is not null ? this : this with { RestedTick = RestedTick ?? loopTick };
        }

        // The slot reached by the rest the loop section opens on, where one did meet it.
        public SlotSinceLoop ReachedInOpeningRest()
        {
            return RestedTick is { } restedTick ? this with { FirstReachedTick = restedTick } : this;
        }

        // The slot written by a call, which counts as written since the loop point only once the loop point has been passed.
        public SlotSinceLoop Armed(bool isPastLoop)
        {
            return this with { IsWritten = IsWritten || isPastLoop };
        }

        // The slot reached by a note at a tick, which is the first reach only where nothing has written the slot since the loop point.
        public SlotSinceLoop ReachedAt(int tick)
        {
            return IsWritten ? this : this with { FirstReachedTick = FirstReachedTick ?? tick };
        }
    }
}
