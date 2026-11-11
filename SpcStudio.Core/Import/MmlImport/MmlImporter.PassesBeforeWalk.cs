using SpcStudio.Export;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's passes over the file's streams ahead of the walk.
public static partial class MmlImporter
{
    // Every note's $EE fine tuning stamped on from its channel's own fine tuning events in stream order, after which no such event is left.
    private static IReadOnlyList<MmlChannel> ResolveFineTune(IReadOnlyList<MmlChannel> channels)
    {
        if (!channels.Any(channel => channel.Events.Any(channelEvent => channelEvent is MmlFineTune)))
        {
            return channels;
        }

        List<MmlChannel> resolved = [];
        foreach (var channel in channels)
        {
            int fineTune = SnesPitch.MinFineTune;
            List<MmlEvent> events = [];
            foreach (var channelEvent in channel.Events)
            {
                switch (channelEvent)
                {
                    case MmlFineTune fineTuneSet:
                        fineTune = fineTuneSet.Value;
                        break;

                    case MmlNote note:
                        events.Add(note with { FineTune = fineTune });
                        break;

                    default:
                        events.Add(channelEvent);
                        break;
                }
            }

            resolved.Add(channel with { Events = events });
        }

        return resolved;
    }

    // Refuses a tie read with a key-off in front of it wherever remote code or an arpeggio, rather than the read-ahead or the legato, can decide whether that key-off happens.
    private static void RefuseHeldCutTies(MmlFile file)
    {
        foreach (var channel in file.Channels)
        {
            if (channel.CutTies.Count == 0)
            {
                continue;
            }

            var stretches = KeyOffDecidingStretches(channel, file.RemoteBlocks);
            if (stretches.Count == 0)
            {
                continue;
            }

            RefuseTiesReachedByStretches(channel, stretches);
            if (!file.DoesReadIntoLoops)
            {
                RefuseArpeggioIntoTieEdges(channel, stretches);
            }
        }
    }

    // Refuses each cut tie a stretch reaches anywhere from the fetch of the note in front's byte ending at the tie up to the tie's own end.
    private static void RefuseTiesReachedByStretches(MmlChannel channel, List<KeyOffStretch> stretches)
    {
        var notes = channel.Events.OfType<MmlNote>().ToList();
        foreach (var tie in channel.CutTies)
        {
            var noteInFront = notes.LastOrDefault(note => note.Tick < tie.Tick);
            int tieEndTick = tie.Tick + Math.Max(1, tie.LengthTicks);

            // Where a stretch starts reaching the tie: the fetch of the note in front's byte ending at it, or never where no note is in front.
            int reachedFromTick = noteInFront is null ? int.MaxValue : FetchTickOfByteEndingAt(noteInFront, tie.Tick);
            if (stretches.Any(stretch => stretch.StartTick < tieEndTick && stretch.EndTick > reachedFromTick))
            {
                throw MmlImportException.TieAtLoopEdgeUnderRemoteCode(tie.Line);
            }
        }
    }

    // Refuses an arpeggio running into a loop edge the read-ahead finds a tie past.
    private static void RefuseArpeggioIntoTieEdges(MmlChannel channel, List<KeyOffStretch> stretches)
    {
        foreach (int edgeTick in TieEdgeTicks(channel))
        {
            if (stretches.Any(stretch => stretch.IsArpeggio && stretch.StartTick < edgeTick && stretch.EndTick >= edgeTick))
            {
                throw MmlImportException.TieAtLoopEdgeUnderRemoteCode(channel.CutTies.FirstOrDefault(tie => tie.Tick >= edgeTick).Line);
            }
        }
    }

    // The tick the note's byte ending at a tie is fetched at - or ending at the note's own end, for a tie further on - or the note's key-on where its byte layout no longer says.
    private static int FetchTickOfByteEndingAt(MmlNote note, int tieTick)
    {
        int byteEndTicks = Math.Min(tieTick - note.Tick, note.LengthTicks);
        return note.Tick + byteEndTicks - (note.PieceTicksEndingAt(byteEndTicks) ?? byteEndTicks);
    }

    // The stretches of a channel where an arpeggio, or a remote block carrying a legato toggle of its own, can decide whether a note is keyed off.
    private static List<KeyOffStretch> KeyOffDecidingStretches(MmlChannel channel, IReadOnlyDictionary<int, IReadOnlyList<MmlEvent>> blocks)
    {
        List<KeyOffStretch> stretches = [];
        int? arpeggioStartTick = null;
        foreach (var channelEvent in channel.Events)
        {
            switch (channelEvent)
            {
                case MmlArpeggio arpeggio:
                    if (arpeggioStartTick is { } startTick)
                    {
                        stretches.Add(new KeyOffStretch(startTick, arpeggio.Tick, true));
                    }

                    arpeggioStartTick = arpeggio.IsRunning ? arpeggio.Tick : null;
                    break;

                case MmlRemoteCall call when DoesCallToggleLegato(call, blocks):
                    stretches.Add(new KeyOffStretch(call.Tick, int.MaxValue, false));
                    break;
            }
        }

        if (arpeggioStartTick is { } runningStartTick)
        {
            stretches.Add(new KeyOffStretch(runningStartTick, int.MaxValue, true));
        }

        return stretches;
    }

    // Whether a call runs a block carrying legato toggles of its own that move the bit, which a key-on block whose toggles come in pairs does not.
    private static bool DoesCallToggleLegato(MmlRemoteCall call, IReadOnlyDictionary<int, IReadOnlyList<MmlEvent>> blocks)
    {
        if (call.BlockLabel is not { } blockLabel || !blocks.TryGetValue(blockLabel, out var body))
        {
            return false;
        }

        int toggleCount = body.Count(blockEvent => blockEvent is MmlLegato);
        return toggleCount > 0 && !(call.TriggerType == MmlRemoteCall.KeyOn && toggleCount % 2 == 0);
    }

    // Every channel with each note whose own last byte plays it for other than the model's timing of its whole length rewritten to sound as that byte plays it.
    private static IReadOnlyList<MmlChannel> SoundByLastBytes(MmlFile file)
    {
        return RewriteChannelsWhere(file, HasNoteTimedApartFromItsLength, EventsSoundingByLastBytes);
    }

    // Every channel with each note the legato bit abandons both cuts of, and that neither binds nor is bound, rewritten to sound its whole length.
    private static IReadOnlyList<MmlChannel> SoundThroughLegato(MmlFile file)
    {
        return RewriteChannelsWhere(file, HasLegatoWithoutArpeggio, EventsSoundingThroughLegato);
    }

    // Every channel a test picks given the events a rewrite makes of it, the song's wait time lookup built only where some channel is picked.
    private static IReadOnlyList<MmlChannel> RewriteChannelsWhere(
        MmlFile file, Func<MmlChannel, bool> isRewritten, Func<MmlChannel, Func<int, int>, List<MmlEvent>> rewrittenEvents)
    {
        if (!file.Channels.Any(isRewritten))
        {
            return file.Channels;
        }

        var waitTimeAt = MmlRemoteExpansion.WaitTimeLookup(file);
        return [.. file.Channels.Select(channel => isRewritten(channel) ? channel with { Events = rewrittenEvents(channel, waitTimeAt) } : channel)];
    }

    // The ticks of a channel's loop edges past which the read-ahead finds a tie.
    private static IEnumerable<int> TieEdgeTicks(MmlChannel channel)
    {
        return channel.LoopEdges.Where(edge => edge.ReadAheadStop == ReadAheadStop.Tie).Select(edge => edge.Tick);
    }

    // Whether a note not yet held ends on a byte of a single tick, inside which neither of its cuts is tested.
    private static bool DoesEndOnOneTickByte(MmlNote note)
    {
        return note is { IsHeld: false, LastPieceTicks: 1 };
    }

    // Whether a note not yet held ends on a byte other than the piece the model cuts it in, the last piece AddmusicK splits its run from the last
    // command inside it the model cuts a note at into.
    private static bool DoesEndApartFromItsModelPiece(MmlChannel channel, int noteIndex, MmlNote note)
    {
        return note is { IsHeld: false, LastPieceTicks: { } lastPieceTicks } && lastPieceTicks != MmlDurationEncoding.LastTiedPieceTicks(ModelRunTicks(channel, noteIndex, note));
    }

    // The ticks of a note's last run as the model writes it: from the last command inside it the model cuts a note at to its end, or its whole length.
    private static int ModelRunTicks(MmlChannel channel, int noteIndex, MmlNote note)
    {
        return MmlRemoteExpansion.FinalPieceTicksSplitBy(channel, noteIndex, note, DoesModelCutNotesAt) ?? note.LengthTicks;
    }

    // Whether an event is one the model cuts a note at: the channel volume, the pan and their fades, the amplify, the envelope override and its gain,
    // and the loop point.
    private static bool DoesModelCutNotesAt(MmlEvent channelEvent)
    {
        return channelEvent is MmlCommand { Letter: 'v' or 'y' } or MmlAmplify or MmlEnvelope or MmlEnvelopeGain or MmlLoopMark;
    }

    // Whether a channel may hold a note its last byte times apart from its length: one ending on a loop edge past which the read-ahead finds a tie, on
    // a one-tick byte, or on a byte other than the piece the model cuts it in.
    private static bool HasNoteTimedApartFromItsLength(MmlChannel channel)
    {
        return TieEdgeTicks(channel).Any() || Enumerable.Range(0, channel.Events.Count).Any(eventIndex =>
            channel.Events[eventIndex] is MmlNote note && (DoesEndOnOneTickByte(note) || DoesEndApartFromItsModelPiece(channel, eventIndex, note)));
    }

    // A channel's events with each note its last byte times apart from its length, and that the legato has not already left uncut, rewritten to sound
    // as that byte plays it.
    private static List<MmlEvent> EventsSoundingByLastBytes(MmlChannel channel, Func<int, int> waitTimeAt)
    {
        var tieEdgeTicks = TieEdgeTicks(channel).ToHashSet();
        var isArpeggioRunningAt = MmlArpeggioExpansion.EventsWithArpeggioState(channel).Select(eventState => eventState.IsArpeggioRunning).ToList();
        var events = channel.Events.ToList();
        var isLegatoBitSetAt = LegatoBits.IsBitSetAtEach(events);
        for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            if (events[eventIndex] is not MmlNote { IsHeld: false } note)
            {
                continue;
            }

            int noteEndTick = note.EndTick;
            bool isOnTieEdge = tieEdgeTicks.Contains(noteEndTick);
            if (TicksSoundedByLastByte(channel, eventIndex, note, isOnTieEdge, isArpeggioRunningAt[eventIndex], waitTimeAt) is not { } soundedTicks)
            {
                continue;
            }

            if (LegatoBits.AtCutsOf(events, eventIndex, noteEndTick, isLegatoBitSetAt[eventIndex]).DoesAbandonCuts)
            {
                continue;
            }

            int reachTick = MmlRemoteExpansion.FurthestGrowthTick(events, eventIndex, noteEndTick, channel.EndTick);
            events[eventIndex] = note.RewrittenToSound(soundedTicks, reachTick, waitTimeAt);
        }

        return events;
    }

    // The ticks a note not yet held sounds for where its last byte times it apart from its length, or null where the model's timing of its length
    // stands: its whole length where a loop edge's tie or a one-tick byte leaves its cuts unmade, and otherwise its last byte's own cuts where that
    // byte is not the piece the model cuts it in and no noise switch inside the note, an arpeggio or a tie's own q times it already.
    private static int? TicksSoundedByLastByte(MmlChannel channel, int noteIndex, MmlNote note, bool isOnTieEdge, bool isArpeggioRunning, Func<int, int> waitTimeAt)
    {
        if (isOnTieEdge || DoesEndOnOneTickByte(note))
        {
            return note.LengthTicks;
        }

        bool hasNoiseSwitchInside = MmlRemoteExpansion.EventsInsideNote(channel, noteIndex, note).Any(insideEvent => insideEvent is MmlNoise);
        if (!DoesEndApartFromItsModelPiece(channel, noteIndex, note) || isArpeggioRunning || !note.TieGates.IsEmpty || hasNoiseSwitchInside)
        {
            return null;
        }

        return note.SoundedTicks(Note.GateOfQuantization(note.Quantization), note.LastPieceTicks, waitTimeAt);
    }

    // Whether a channel toggles the legato and runs no arpeggio.
    private static bool HasLegatoWithoutArpeggio(MmlChannel channel)
    {
        return channel.Events.Any(channelEvent => channelEvent is MmlLegato) && !channel.Events.Any(channelEvent => channelEvent is MmlArpeggio);
    }

    // A channel's events with each note keyed on under the legato bit, with no toggle inside it, that hands nothing over and is handed nothing, rewritten to sound its whole length.
    private static List<MmlEvent> EventsSoundingThroughLegato(MmlChannel channel, Func<int, int> waitTimeAt)
    {
        var events = channel.Events.ToList();
        var noteIndices = Enumerable.Range(0, events.Count).Where(eventIndex => events[eventIndex] is MmlNote).ToList();
        var toggleTicks = LegatoBits.ToggleTicks(events);

        // Whether the legato bit is set under each note, and whether each note is bound into the one before it.
        var (isBitSetUnder, isBound) = LegatoBits.WalkNotes(
            [.. noteIndices.Select(eventIndex => (events[eventIndex].Tick, events[eventIndex].Tick + ((MmlNote)events[eventIndex]).LengthTicks))], toggleTicks);
        for (int noteNumber = 0; noteNumber < noteIndices.Count; noteNumber++)
        {
            var note = (MmlNote)events[noteIndices[noteNumber]];
            int noteEndTick = note.EndTick;
            bool hasNextNote = noteNumber + 1 < noteIndices.Count;
            if (!isBitSetUnder[noteNumber] || isBound[noteNumber] || (hasNextNote && isBound[noteNumber + 1]) || note.IsHeld)
            {
                continue;
            }

            if (toggleTicks.Any(toggleTick => toggleTick > note.Tick && toggleTick < noteEndTick))
            {
                continue;
            }

            int? nextNoteTick = hasNextNote ? events[noteIndices[noteNumber + 1]].Tick : null;
            int? reachTick = MmlRemoteExpansion.FurthestGrowthTickUnderLegato(events, noteIndices[noteNumber], noteEndTick, channel.EndTick, nextNoteTick);
            events[noteIndices[noteNumber]] = note.RewrittenToSound(note.LengthTicks, reachTick, waitTimeAt);
        }

        return events;
    }

    // A stretch of a channel where an arpeggio or a remote block's legato toggle can decide whether a note is keyed off, and which of the two it is.
    private readonly record struct KeyOffStretch(int StartTick, int EndTick, bool IsArpeggio);
}
