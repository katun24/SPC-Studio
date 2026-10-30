using SpcStudio.Model;

namespace SpcStudio.Import;

internal static partial class MmlRemoteExpansion
{
    // Places the fires a type 1 arm gets out of one note's key-on - the wait, and one more every time the timer wraps - inside the note and in the rest
    // behind it, up to whatever the driver answers for next.
    private static void PlaceTimerFiresFromKeyOn(
        MmlChannel channel, int noteIndex, MmlNote note, int blockLabel, int timeTicks, RestSplits restSplits, IReadOnlyList<MmlEvent> body,
        List<MmlEvent> output)
    {
        int reachTick = ArmReachTick(channel, noteIndex, blockLabel, timeTicks);

        // The counted ticks to the first fire, an argument of zero being a whole period.
        int waitTicks = timeTicks == 0 ? MmlRemoteCall.TimerPeriod : timeTicks;
        if (PiecesToReach(channel, noteIndex, note.Tick, note, reachTick, restSplits) is { } countedPieces)
        {
            PlaceTimerFires(note.Tick, countedPieces, waitTicks, body, output);
            return;
        }

        List<MmlEvent> noteFires = [];
        waitTicks = PlaceTimerFires(note.Tick, note.Pieces, waitTicks, body, noteFires);
        output.AddRange(noteFires.Where(fire => fire.Tick < reachTick));

        int reachTicksPastNoteEnd = reachTick - note.EndTick;
        if (reachTicksPastNoteEnd > 0 && note.LengthTicks - note.Pieces.Length + reachTicksPastNoteEnd - 1 >= waitTicks)
        {
            throw MmlImportException.TimerBlockPastNote(channel.Number, note.Tick, note.Line);
        }
    }

    // Places a type 1 block's events at every tick its timer runs out on, counting from a fetch through the pieces with that many counted ticks to go,
    // and answers the first threshold the pieces do not reach.
    private static int PlaceTimerFires(int startTick, IReadOnlyList<int> pieces, int waitTicks, IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        while (TickAfterCountedTicks(startTick, pieces, waitTicks) is { } fireTick)
        {
            output.AddRange(StampedAt(body, fireTick));
            waitTicks += MmlRemoteCall.TimerPeriod;
        }

        return waitTicks;
    }

    // The tick that many counted ticks after a key-on, each byte losing the tick that fetches it, or null where the pieces do not hold that many.
    private static int? TickAfterCountedTicks(int startTick, IReadOnlyList<int> pieces, int waitTicks)
    {
        int pieceStartTick = startTick;
        int countedTicks = 0;
        foreach (int pieceTicks in pieces)
        {
            if (waitTicks - countedTicks < pieceTicks)
            {
                return pieceStartTick + (waitTicks - countedTicks);
            }

            countedTicks += pieceTicks - 1;
            pieceStartTick += pieceTicks;
        }

        return null;
    }

    // Places the fires a type 1 arm gets out of the stale timer before the key-on that reloads it, and refuses the arm where the tick they land on cannot be named.
    private static void PlaceStaleTimerFires(
        MmlChannel channel, int callIndex, MmlRemoteCall call, int? timerTicksLeft, MmlNote? noteAroundArm, bool didLastNoteRing, RestSplits restSplits,
        IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        int reachTick = ArmReachTick(channel, callIndex, call.BlockLabel, call.TimeTicks);
        int staleTimerLastingTicks = timerTicksLeft switch
        {
            null => 1,
            0 => MmlRemoteCall.TimerPeriod,
            { } ticksLeft => ticksLeft,
        };

        if (timerTicksLeft is not null && PiecesToReach(channel, callIndex, call.Tick, noteAroundArm, reachTick, restSplits) is { } countedPieces)
        {
            PlaceTimerFires(call.Tick, countedPieces, staleTimerLastingTicks, body, output);
            return;
        }

        if (reachTick - call.Tick <= staleTimerLastingTicks)
        {
            return;
        }

        if (IsWipedWhileSilent(channel, callIndex, call.Tick, reachTick, didLastNoteRing, body))
        {
            return;
        }

        throw MmlImportException.TimerBlockBeforeNextNote(channel.Number, call.Tick, call.Line);
    }

    // The bytes the timer counts through from a key-on or an arm to the tick the arm reaches: what is left of the note that tick stands in, where it stands
    // in one, then the rest's, or null where that note's bytes are not known.
    private static IReadOnlyList<int>? PiecesToReach(MmlChannel channel, int eventIndex, int fromTick, MmlNote? noteAtTick, int reachTick, RestSplits restSplits)
    {
        if (noteAtTick is null)
        {
            return [.. RestPieces(channel, eventIndex, restSplits, fromTick, reachTick)];
        }

        int noteCountedEndTick = Math.Min(reachTick, noteAtTick.EndTick);
        if (noteAtTick.PiecesBetween(fromTick - noteAtTick.Tick, noteCountedEndTick - noteAtTick.Tick) is not { } notePieces)
        {
            return null;
        }

        return [.. notePieces, .. RestPieces(channel, eventIndex, restSplits, noteCountedEndTick, reachTick)];
    }

    // What the timer holds at the end of a note keyed on under a type 1 arm, or null where the note's bytes are not known.
    private static int? TimerLeftAfterNote(MmlNote note, int timeTicks)
    {
        return note.HasKnownLayout ? TimerCountedDown(timeTicks, note.Pieces) : null;
    }

    // What the timer holds after counting down from a value through the pieces, each byte losing the tick that fetches it.
    private static int TimerCountedDown(int timerTicksLeft, IEnumerable<int> pieces)
    {
        int countedTicks = pieces.Sum(pieceTicks => pieceTicks - 1);
        return ((timerTicksLeft - countedTicks) % MmlRemoteCall.TimerPeriod + MmlRemoteCall.TimerPeriod) % MmlRemoteCall.TimerPeriod;
    }

    // Whether every fire of a type 1 arm is undone before anything can hear it: the voice silent from the arm to the next note, and an instrument change
    // at that note rewriting everything the block wrote.
    private static bool IsWipedWhileSilent(MmlChannel channel, int callIndex, int callTick, int reachTick, bool didLastNoteRing, IReadOnlyList<MmlEvent> body)
    {
        if (!IsEnvelopeOnly(body) || didLastNoteRing)
        {
            return false;
        }

        if (LastNoteBefore(channel, callIndex) is { } noteBefore && noteBefore.EndTick > callTick)
        {
            return false;
        }

        bool isInstrumentChangedAtReach = false;
        for (int laterIndex = callIndex + 1; laterIndex < channel.Events.Count; laterIndex++)
        {
            var laterEvent = channel.Events[laterIndex];
            if (laterEvent is MmlNote)
            {
                return laterEvent.Tick == reachTick && isInstrumentChangedAtReach;
            }

            if (laterEvent is MmlInstrumentChange && laterEvent.Tick == reachTick)
            {
                isInstrumentChangedAtReach = true;
            }
        }

        return false;
    }

    // How far forward an arm reaches before something the driver answers for takes over: the next note, or the next call writing the ordinary slot that is not this same type 1 arm restated.
    private static int ArmReachTick(MmlChannel channel, int callIndex, int? blockLabel, int timeTicks)
    {
        for (int laterIndex = callIndex + 1; laterIndex < channel.Events.Count; laterIndex++)
        {
            var laterEvent = channel.Events[laterIndex];
            if (laterEvent is MmlNote)
            {
                return laterEvent.Tick;
            }

            if (laterEvent is MmlRemoteCall call && call.DoesWriteOrdinarySlot && !IsTimerArmRestated(call, MmlRemoteCall.Timed, blockLabel, timeTicks))
            {
                return laterEvent.Tick;
            }
        }

        return channel.EndTick;
    }

    // Whether a call is a type 1 arm writing exactly the slot already in force, which leaves the timer counting as if the call were not there.
    private static bool IsTimerArmRestated(MmlRemoteCall call, int triggerTypeInForce, int? blockLabelInForce, int timeTicksInForce)
    {
        return call.TriggerType == MmlRemoteCall.Timed && triggerTypeInForce == MmlRemoteCall.Timed && call.BlockLabel == blockLabelInForce
            && call.TimeTicks == timeTicksInForce;
    }

    // Places where a type 2 or 5 arm fires inside one note, running a type 6 arm's block there instead where that arm's key-off check takes the fire
    // over, and refusing the note where legato is on under it.
    private static void PlaceBeforeKeyOffFire(
        MmlChannel channel, int noteIndex, MmlNote note, RemoteSlot arm, bool isLegatoBitSet, Func<int, int> waitTimeAt, RestSplits restSplits,
        IReadOnlyList<MmlEvent> body, IReadOnlyList<MmlEvent>? keyOffArmBody, List<MmlEvent> output)
    {
        if (isLegatoBitSet)
        {
            throw MmlImportException.RemoteArmAndLegato(channel.Number, note.Tick, arm.TriggerType, note.Line);
        }

        var noteKeyOffTriggers = KeyOffTriggersOf(channel, noteIndex, note, waitTimeAt);
        if (BeforeKeyOffTick(note.Tick, noteKeyOffTriggers, arm.TimeTicks) is not { } fireTick)
        {
            return;
        }

        bool doesFireLandOnWaitTimeTick = noteKeyOffTriggers.GateTicks - arm.TimeTicks == noteKeyOffTriggers.WaitTicks;
        bool isRestNext = ReadAheadStopAfterNote(channel, noteIndex, note.EndTick, restSplits) == ReadAheadStop.Rest;
        var firedBody = keyOffArmBody is not null && (isRestNext || !doesFireLandOnWaitTimeTick) ? keyOffArmBody : body;
        output.AddRange(StampedAt(firedBody, fireTick));
    }

    // The tick a type 2 or 5 arm's comparison against the gate counter matches on inside one byte run whose cuts are known, or null where the counter never reaches the argument.
    private static int? BeforeKeyOffTick(int startTick, SnesTiming.KeyOffTriggers triggers, int timeTicks)
    {
        if (timeTicks < 1)
        {
            return null;
        }

        return timeTicks < triggers.GateTicks ? startTick + triggers.LeadTicks + (triggers.GateTicks - timeTicks) : null;
    }
}
