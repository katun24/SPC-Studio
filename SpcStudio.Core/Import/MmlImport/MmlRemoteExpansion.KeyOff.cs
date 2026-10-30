using SpcStudio.Model;

namespace SpcStudio.Import;

internal static partial class MmlRemoteExpansion
{
    // A note a type 3 or 6 arm reaches, rewritten so that what sounds is what the driver would have sounded, with the block's events placed at the ticks the skipped key-off would have been.
    private static MmlNote NoteUnderKeyOffArm(
        MmlChannel channel, int noteIndex, MmlNote note, bool isLegatoBitSet, KeyOffArming keyOffArming, Func<int, int> waitTimeAt, RestSplits restSplits,
        IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        var noteKeyOffTriggers = KeyOffTriggersOf(channel, noteIndex, note, waitTimeAt);
        int noteEndTick = note.EndTick;
        int? followingRestTicks = FollowingRestTicks(channel, noteIndex, noteEndTick);
        int lastByteStartTick = note.Tick + noteKeyOffTriggers.LeadTicks;

        // The two legato bits as the note's own cuts find them and as the rest after it starts.
        var legatoBits = LegatoBits.AtCutsOf(channel.Events, noteIndex, noteEndTick, isLegatoBitSet);

        bool areCutsAbandoned = legatoBits.DoesAbandonCuts;
        if (areCutsAbandoned && followingRestTicks is null)
        {
            return note;
        }

        var readAheadStop = ReadAheadStopAfterNote(channel, noteIndex, noteEndTick, restSplits);
        bool isHeldPastEnd = readAheadStop == ReadAheadStop.Tie || !noteKeyOffTriggers.DoesGateFire && !noteKeyOffTriggers.DoesWaitFire;
        bool isRestNext = readAheadStop == ReadAheadStop.Rest;
        if (!areCutsAbandoned && !isHeldPastEnd)
        {
            var fireTicks = KeyOffFireTicks(noteKeyOffTriggers, lastByteStartTick, readAheadStop);
            if (!isRestNext && fireTicks.Count == 0)
            {
                return note;
            }

            if (!isRestNext && noteKeyOffTriggers.GateTicks > noteKeyOffTriggers.WaitTicks)
            {
                if (IsEnvelopeOnly(body))
                {
                    output.AddRange(StampedAt(body, noteEndTick));
                    return note;
                }

                output.AddRange(StampedAt(body, lastByteStartTick + noteKeyOffTriggers.GateTicks));
                int ticksBeforeWaitTimeCut = noteKeyOffTriggers.LeadTicks + noteKeyOffTriggers.WaitTicks;
                return note.ShortenedTo(ticksBeforeWaitTimeCut).HeldFor(ticksBeforeWaitTimeCut);
            }

            foreach (int fireTick in fireTicks)
            {
                output.AddRange(StampedAt(body, fireTick));
            }
        }

        // Where the voice actually stops: the wait time's own tick where its cuts were made, and otherwise whatever the byte played next lets it go at.
        var (stopTick, ringEnd) = areCutsAbandoned || isHeldPastEnd || isRestNext
            ? followingRestTicks is not null
                ? RingThroughRest(channel, noteIndex, noteEndTick, Note.GateOfQuantization(note.Quantization), legatoBits, keyOffArming, waitTimeAt, restSplits)
                : (noteEndTick, RingEnd.KeyedOver)
            : (lastByteStartTick + noteKeyOffTriggers.WaitTicks, RingEnd.Cut);

        if (ringEnd == RingEnd.RunsOff)
        {
            if (areCutsAbandoned)
            {
                throw MmlImportException.KeyOffArmAndLegatoAtChannelEnd(channel.Number, note.Tick, note.Line);
            }

            stopTick = channel.EndTick;
        }

        if (ringEnd == RingEnd.Binds)
        {
            return note with { LengthTicks = stopTick - note.Tick };
        }

        int soundingTicks = stopTick - note.Tick;
        if (areCutsAbandoned)
        {
            return note.HeldFor(soundingTicks);
        }

        return note.RewrittenToSound(soundingTicks, FurthestGrowthTick(channel.Events, noteIndex, noteEndTick, channel.EndTick), waitTimeAt);
    }

    // The ticks a type 3 arm runs its block at in one byte, given its cuts and what its read-ahead finds.
    private static List<int> KeyOffFireTicks(SnesTiming.KeyOffTriggers triggers, int byteStartTick, ReadAheadStop readAheadStop)
    {
        if (readAheadStop == ReadAheadStop.Tie)
        {
            return [];
        }

        bool isRest = readAheadStop == ReadAheadStop.Rest;
        List<int> fireTicks = [];
        if (triggers.DoesGateFire && (isRest || triggers.GateTicks != triggers.WaitTicks))
        {
            fireTicks.Add(byteStartTick + triggers.GateTicks);
        }

        if (isRest && triggers.DoesWaitFire)
        {
            fireTicks.Add(byteStartTick + triggers.WaitTicks);
        }

        return [.. fireTicks.Distinct().Order()];
    }

    // Places the block where a type 3 arm's cuts run it in a tie the readers read as a rest, straight after a note a loop's edge keyed off, refusing the
    // tie where the voice may still be sounding when it is fetched.
    private static void PlaceSilentTieFires(
        MmlChannel channel, int nextEventIndex, MmlCutTie tie, int? restGate, bool isLegatoBitSet, int lastSoundedEndTick, Func<int, int> waitTimeAt,
        RestSplits restSplits, IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        if (restGate is { } gate)
        {
            if (isLegatoBitSet || lastSoundedEndTick >= tie.Tick)
            {
                throw MmlImportException.TieAtLoopEdgeUnderRemoteCode(tie.Line);
            }

            PlaceTieByteFires(channel, nextEventIndex, tie.Tick, tie.EndTick, gate, waitTimeAt, restSplits, body, output);
            return;
        }

        if (tie.Quantization is not { } ownQuantization)
        {
            throw MmlImportException.TieAtLoopEdgeUnderRemoteCode(tie.Line);
        }

        var tieByteEndTicks = new SortedSet<int>(channel.RestTies.Concat(channel.RestBytes)
            .Concat(channel.CutTies.Select(cutTie => cutTie.Tick))
            .Concat(channel.LoopEdges.Select(edge => edge.Tick))
            .Concat(channel.Events.OfType<MmlNote>().Select(note => note.Tick))
            .Append(tie.EndTick)
            .Append(channel.EndTick));

        for (int byteStartTick = tie.Tick; byteStartTick < channel.EndTick;)
        {
            int tieByteEndTick = tieByteEndTicks.GetViewBetween(byteStartTick + 1, channel.EndTick).Min;
            var readAheadStop = PlaceTieByteFires(
                channel, nextEventIndex, byteStartTick, tieByteEndTick, Note.GateOfQuantization(ownQuantization), waitTimeAt, restSplits, body, output);
            if (readAheadStop != ReadAheadStop.Tie || channel.CutTies.Any(cutTie => cutTie.Tick == tieByteEndTick))
            {
                return;
            }

            byteStartTick = tieByteEndTick;
        }
    }

    // Places the block where a type 3 arm's cuts run it in one tie byte fetched with the voice keyed off, and answers what the byte's read-ahead found.
    private static ReadAheadStop PlaceTieByteFires(
        MmlChannel channel, int nextEventIndex, int byteStartTick, int byteEndTick, int gate, Func<int, int> waitTimeAt, RestSplits restSplits,
        IReadOnlyList<MmlEvent> body, List<MmlEvent> output)
    {
        var readAheadStop = channel.CutTies.Any(cutTie => cutTie.Tick == byteEndTick) || channel.RestTies.Contains(byteEndTick)
            ? ReadAheadStop.Tie
            : restSplits.EdgeReadAheadStopAt(byteEndTick) ?? ReadAheadStopAt(channel, nextEventIndex, byteEndTick);
        var tieByteKeyOffTriggers = SnesTiming.KeyOffTriggers.For(byteEndTick - byteStartTick, gate, null, offsetTicks => waitTimeAt(byteStartTick + offsetTicks));
        foreach (int fireTick in KeyOffFireTicks(tieByteKeyOffTriggers, byteStartTick, readAheadStop))
        {
            output.AddRange(StampedAt(body, fireTick));
        }

        return readAheadStop;
    }

    // What the read-ahead finds at a tick where no loop edge stands: a note starting there, the channel's own key-off at its end, and otherwise a rest.
    private static ReadAheadStop ReadAheadStopAt(MmlChannel channel, int fromEventIndex, int tick)
    {
        for (int eventIndex = fromEventIndex; eventIndex < channel.Events.Count && channel.Events[eventIndex].Tick <= tick; eventIndex++)
        {
            if (channel.Events[eventIndex] is MmlNote note && note.Tick == tick)
            {
                return ReadAheadStop.Note;
            }
        }

        return tick >= channel.EndTick ? ReadAheadStop.KeyOff : ReadAheadStop.Rest;
    }

    // The voice a type 3 or 6 arm has left sounding past the end of a note, followed through the rest after it: where it stops, and how.
    private static (int Tick, RingEnd How) RingThroughRest(
        MmlChannel channel, int noteIndex, int noteEndTick, int gate, LegatoBits legatoBits, KeyOffArming keyOffArming, Func<int, int> waitTimeAt,
        RestSplits restSplits)
    {
        var events = channel.Events;
        int eventIndex = noteIndex + 1;
        while (eventIndex < events.Count && events[eventIndex].Tick < noteEndTick)
        {
            eventIndex++;
        }

        int byteStartTick = noteEndTick;
        while (true)
        {
            for (; eventIndex < events.Count && events[eventIndex].Tick == byteStartTick && events[eventIndex] is not MmlNote; eventIndex++)
            {
                if (events[eventIndex] is MmlLegato)
                {
                    legatoBits = legatoBits.Toggle();
                }
                else if (events[eventIndex] is MmlRemoteCall call)
                {
                    keyOffArming = keyOffArming.After(call);
                }
            }

            if (eventIndex < events.Count && events[eventIndex] is MmlNote nextNote && nextNote.Tick == byteStartTick)
            {
                return (byteStartTick, legatoBits.DoesAbandonCuts ? RingEnd.Binds : RingEnd.KeyedOver);
            }

            int restByteEndTick = restSplits.NextByteBoundary(byteStartTick, eventIndex < events.Count ? events[eventIndex].Tick : channel.EndTick);
            if (restByteEndTick <= byteStartTick)
            {
                return (byteStartTick, RingEnd.RunsOff);
            }

            if (restSplits.TieAfterNoteLine(byteStartTick) is { } tieLine)
            {
                throw MmlImportException.TieAtLoopEdgeUnderRemoteCode(tieLine);
            }

            if (!keyOffArming.IsArmed && !restSplits.DoesStartTie(byteStartTick))
            {
                return (byteStartTick, RingEnd.Cut);
            }

            if (!legatoBits.DoesAbandonCuts && !restSplits.IsHeldAt(restByteEndTick))
            {
                int cutStartTick = byteStartTick;
                var restByteKeyOffCuts = SnesTiming.KeyOffTriggers.For(restByteEndTick - byteStartTick, gate, null, offsetTicks => waitTimeAt(cutStartTick + offsetTicks));
                if (restByteKeyOffCuts.DoesGateFire || restByteKeyOffCuts.DoesWaitFire)
                {
                    return (byteStartTick + restByteKeyOffCuts.LeadTicks + Math.Min(restByteKeyOffCuts.GateTicks, restByteKeyOffCuts.WaitTicks), RingEnd.Cut);
                }
            }

            byteStartTick = restByteEndTick;
        }
    }

    // What the read-ahead from a note's last byte finds: what a loop's edge makes it where one stands there, and otherwise a rest where one follows the note and a note where none does.
    private static ReadAheadStop ReadAheadStopAfterNote(MmlChannel channel, int noteIndex, int noteEndTick, RestSplits restSplits)
    {
        return restSplits.EdgeReadAheadStopAt(noteEndTick) ?? (FollowingRestTicks(channel, noteIndex, noteEndTick) is null ? ReadAheadStop.Note : ReadAheadStop.Rest);
    }

    // How long the rest straight after a note is, or null where a note follows it directly or the channel ends there.
    private static int? FollowingRestTicks(MmlChannel channel, int noteIndex, int noteEndTick)
    {
        for (int laterIndex = noteIndex + 1; laterIndex < channel.Events.Count; laterIndex++)
        {
            var laterEvent = channel.Events[laterIndex];
            if (laterEvent.Tick > noteEndTick)
            {
                return laterEvent.Tick - noteEndTick;
            }

            if (laterEvent is MmlNote)
            {
                return null;
            }
        }

        return channel.EndTick > noteEndTick ? channel.EndTick - noteEndTick : null;
    }

    // A note's key-off triggers, the gate's and the wait time's, at the gate its own `q` sets.
    private static SnesTiming.KeyOffTriggers KeyOffTriggersOf(MmlChannel channel, int noteIndex, MmlNote note, Func<int, int> waitTimeAt)
    {
        return SnesTiming.KeyOffTriggers.For(note.LengthTicks, Note.GateOfQuantization(note.Quantization), LastByteTicks(channel, noteIndex, note), offsetTicks => waitTimeAt(note.Tick + offsetTicks));
    }

    // How a voice a type 3 arm has left sounding into a rest comes to an end.
    private enum RingEnd
    {
        Cut,
        Binds,
        KeyedOver,
        RunsOff,
    }
}
