using SpcStudio.Model;

namespace SpcStudio.Import;

internal static partial class MmlRemoteExpansion
{
    // Places the fires a type 1 arm gets out of one note's key-on - the wait, and one more every time the timer wraps - inside the note and in the rest
    // behind it, up to whatever the driver answers for next.
    private static void PlaceTimerFiresFromKeyOn(
        MmlChannel channel, int noteIndex, MmlNote note, int blockLabel, int timeTicks, RestSplits restSplits, ArpeggioSteps? arpeggioSteps,
        IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        int reachTick = ArmReachTick(channel, noteIndex, note.Tick + 1, blockLabel, timeTicks, arpeggioSteps);
        int waitTicks = FirstFireWaitTicks(timeTicks);
        if (PiecesToReach(channel, noteIndex, note.Tick, note, reachTick, restSplits, arpeggioSteps) is { } countedPieces)
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

    // Places the fires a type 1 arm gets out of an arpeggio step's reload, counted through the channel's bytes from the step up to whatever the driver answers for next.
    private static void PlaceTimerFiresFromStep(
        MmlChannel channel, int lastReadIndex, int stepTick, int blockLabel, int timeTicks, ArpeggioSteps arpeggioSteps, IReadOnlyList<MmlEvent> body,
        List<MmlEvent> output)
    {
        int reachTick = ArmReachTick(channel, lastReadIndex, stepTick + 1, blockLabel, timeTicks, arpeggioSteps);
        PlaceTimerFires(stepTick, arpeggioSteps.PiecesBetween(stepTick, reachTick), FirstFireWaitTicks(timeTicks), body, output);
    }

    // The counted ticks from a reload to the first fire, an argument of zero being a whole period.
    private static int FirstFireWaitTicks(int timeTicks)
    {
        return timeTicks == 0 ? MmlRemoteCall.TimerPeriod : timeTicks;
    }

    // Places a type 1 block's events at every tick its timer runs out on, counting from a fetch through the pieces with that many counted ticks to go,
    // and answers the first threshold the pieces do not reach.
    private static int PlaceTimerFires(int startTick, IReadOnlyList<int> pieces, int waitTicks, IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        while (SnesTiming.TickAfterCountedTicks(startTick, pieces, waitTicks) is { } fireTick)
        {
            output.AddRange(StampedAt(body, fireTick));
            waitTicks += MmlRemoteCall.TimerPeriod;
        }

        return waitTicks;
    }

    // Places the fires a type 1 arm gets out of the stale timer before the key-on that reloads it, and refuses the arm where the tick they land on cannot be named.
    private static void PlaceStaleTimerFires(
        MmlChannel channel, int callIndex, MmlRemoteCall call, int? timerTicksLeft, MmlNote? noteAroundArm, bool didLastNoteRing, RestSplits restSplits,
        ArpeggioSteps? arpeggioSteps, IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        int reachTick = ArmReachTick(channel, callIndex, call.Tick, call.BlockLabel, call.TimeTicks, arpeggioSteps);
        int staleTimerLastingTicks = timerTicksLeft switch
        {
            null => 1,
            0 => MmlRemoteCall.TimerPeriod,
            { } ticksLeft => ticksLeft,
        };

        if (timerTicksLeft is not null && PiecesToReach(channel, callIndex, call.Tick, noteAroundArm, reachTick, restSplits, arpeggioSteps) is { } countedPieces)
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
    // in one, then the rest's, or null where that note's bytes are not known - every byte's fetch, where an arpeggio's steps can reach the count to a tick
    // inside one.
    private static IReadOnlyList<int>? PiecesToReach(MmlChannel channel, int eventIndex, int fromTick, MmlNote? noteAtTick, int reachTick, RestSplits restSplits, ArpeggioSteps? arpeggioSteps)
    {
        if (arpeggioSteps is not null)
        {
            return arpeggioSteps.PiecesBetween(fromTick, reachTick);
        }

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

    // How far forward an arm or a reload reaches before something the driver answers for takes over: the next key-on - a note the arpeggio leaves sounding,
    // or a step from the tick given on - or the next call writing the ordinary slot that is not this same type 1 arm restated.
    private static int ArmReachTick(MmlChannel channel, int lastReadIndex, int stepsFromTick, int? blockLabel, int timeTicks, ArpeggioSteps? arpeggioSteps)
    {
        int stepReachTick = arpeggioSteps?.StepReachFrom(stepsFromTick) ?? int.MaxValue;
        for (int laterIndex = lastReadIndex + 1; laterIndex < channel.Events.Count; laterIndex++)
        {
            var laterEvent = channel.Events[laterIndex];
            if (laterEvent is MmlNote note && arpeggioSteps?.IsSilenced(note.Tick) != true)
            {
                return Math.Min(laterEvent.Tick, stepReachTick);
            }

            if (laterEvent is MmlRemoteCall call && call.DoesWriteOrdinarySlot && !IsTimerArmRestated(call, MmlRemoteCall.Timed, blockLabel, timeTicks))
            {
                return Math.Min(laterEvent.Tick, stepReachTick);
            }
        }

        return Math.Min(channel.EndTick, stepReachTick);
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
