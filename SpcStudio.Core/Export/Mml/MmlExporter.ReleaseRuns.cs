using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the runs of notes whose release a key-off remote block writes in place of each note's own:
// finding them, and what they change in a channel's notes and commands.
public static partial class MmlExporter
{
    // A note of a run as it is written: its start, the length and gate it is written under, the tick its release starts at, and the tick the rest
    // behind it is cut at, null where it is not.
    private sealed record ReleaseNote(int StartTick, int LengthTicks, int Gate, int ReleaseTick, int? RestCutTick);

    // A run of notes one key-off block writes the release of: the tick it is armed at and the tick it is cleared at, its notes, the block's commands, and
    // the bytes it saves once its arm, its clear and the q around it are paid for.
    private sealed record ReleaseRun(int ArmTick, int ClearTick, IReadOnlyList<ReleaseNote> Notes, string BlockText, int NetBytesSaved) : IBlockRun;

    // Each channel's runs of notes whose release a key-off block writes, and what they change in the channel's notes and commands.
    private static class ReleaseRuns
    {
        private const string ArmPlaceholderOpening = "(!0,3,";
        private const string ArmPlaceholderClosing = ")";
        private const int RunEdgeCount = 2;

        private static readonly int TieBytes = SongDataSize.NoteByteBytes + SongDataSize.DurationByteBytes;
        private static readonly int RunEdgeBytes = RunEdgeCount * (SongDataSize.RemoteCallBytes + SongDataSize.QByteBytes + SongDataSize.DurationByteBytes);

        // No run on any channel.
        public static IReadOnlyList<ReleaseRun>[] NoneOnAnyChannel()
        {
            return [.. Enumerable.Range(0, Song.DriverChannelCount).Select(_ => (IReadOnlyList<ReleaseRun>)[])];
        }

        // Every channel's runs worth arming under the options, a block whose runs together do not pay for writing it once being dropped with them.
        public static IReadOnlyList<ReleaseRun>[] ForEachChannel(Song song, MmlExportOptions options, IReadOnlyList<ArpeggioRun>[] arpeggioRunsByChannel)
        {
            var runsByChannel = Enumerable.Range(0, Song.DriverChannelCount).Select(channel => Found(song, channel, options, arpeggioRunsByChannel[channel]));
            return [.. RunsOfPayingBlocks(runsByChannel)];
        }

        // The blocks the runs arm, each once, in the order the channels first arm them.
        public static List<string> BlocksOf(IReadOnlyList<ReleaseRun>[] runsByChannel)
        {
            return [.. runsByChannel.SelectMany(runs => runs).Select(run => run.BlockText).Distinct()];
        }

        // What stands for the arm of the block at an index of the blocks until the block's number is known.
        public static string ArmPlaceholder(int blockIndex)
        {
            return ArmPlaceholderOpening + blockIndex + ArmPlaceholderClosing;
        }

        // Whether a token stands for the arm of a block until its number is known.
        public static bool IsArmPlaceholder(string token)
        {
            return token.StartsWith(ArmPlaceholderOpening, StringComparison.Ordinal);
        }

        // A note as a channel's runs write it: at the length and gate its run gives it, or as it is where it is in none.
        public static Note WrittenNote(Note note, IReadOnlyList<ReleaseRun> runs)
        {
            foreach (var run in runs)
            {
                foreach (var releaseNote in run.Notes)
                {
                    if (releaseNote.StartTick == note.StartTick)
                    {
                        return note with { LengthTicks = releaseNote.LengthTicks, Gate = releaseNote.Gate };
                    }
                }
            }

            return note;
        }

        // A channel's commands with its runs in place: each run's notes' own release taken out, and the block armed where the run starts and cleared where
        // it ends, in tick order and in the fixed order within a tick.
        public static IEnumerable<TimedCommand> CommandsUnder(IEnumerable<TimedCommand> commands, IReadOnlyList<ReleaseRun> runs, IReadOnlyList<string> blocks)
        {
            var releaseTicks = runs.SelectMany(run => run.Notes).Select(note => note.ReleaseTick).ToHashSet();
            var armsAndClears = runs.SelectMany(run => new[]
            {
                ArmCommand(run.ArmTick, run.BlockText, blocks),
                new TimedCommand(run.ClearTick, ReleaseClearOrder, HoldOffName.Write()),
            });

            return commands.Where(command => command.SortOrder != EnvelopeOrder || !releaseTicks.Contains(command.Tick))
                .Concat(armsAndClears)
                .OrderBy(command => command.Tick)
                .ThenBy(command => command.SortOrder);
        }

        // The ticks the rests behind a channel's run notes are cut at.
        public static HashSet<int> RestCutTicks(IReadOnlyList<ReleaseRun> runs)
        {
            return [.. runs.SelectMany(run => run.Notes).Select(note => note.RestCutTick).OfType<int>()];
        }

        // The arm of the block a run holds across the loop marker, written again just after it.
        public static IEnumerable<TimedCommand> ArmRestatement(IReadOnlyList<ReleaseRun> runs, IReadOnlyList<string> blocks, int loopTick)
        {
            return runs.Where(run => DoesCrossLoopMarker(run.ArmTick, run.ClearTick, loopTick)).Select(run => ArmCommand(loopTick, run.BlockText, blocks));
        }

        // The arm of a block at a tick.
        private static TimedCommand ArmCommand(int tick, string blockText, IReadOnlyList<string> blocks)
        {
            return new TimedCommand(tick, ReleaseArmOrder, ArmPlaceholder(IndexOf(blocks, blockText)));
        }

        // Whether a run armed and cleared at the ticks holds its block across the loop marker, so that its arm is written again just after it.
        private static bool DoesCrossLoopMarker(int armTick, int clearTick, int markerTick)
        {
            return armTick < markerTick && markerTick < clearTick;
        }

        // The index of a block among the blocks.
        private static int IndexOf(IReadOnlyList<string> blocks, string blockText)
        {
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                if (blocks[blockIndex] == blockText)
                {
                    return blockIndex;
                }
            }

            throw new InvalidOperationException("A release run arms a block that is not among the blocks.");
        }

        // The runs on one channel that save bytes under the options: each stretch of notes one after another that can each take the same block, the
        // notes it cannot reach included wherever they stand in it, kept where it saves once the arm it writes again behind the loop marker is paid for.
        private static List<ReleaseRun> Found(Song song, int channel, MmlExportOptions options, IReadOnlyList<ArpeggioRun> arpeggioRuns)
        {
            var notes = song.NotesInsideSong(channel).ToList();
            var candidates = Candidates(song, channel, options, notes, arpeggioRuns);
            int markerTick = LoopMarkerTick(song);
            var runs = new List<ReleaseRun>();

            for (int firstIndex = 0; firstIndex < notes.Count;)
            {
                if (candidates[firstIndex] is not { Note: { } firstNote, BlockText: { } blockText })
                {
                    firstIndex++;
                    continue;
                }

                int lastIndex = firstIndex;
                int lastReleaseIndex = firstIndex;
                while (lastIndex + 1 < notes.Count && candidates[lastIndex + 1] is { } nextCandidate && (nextCandidate.BlockText ?? blockText) == blockText)
                {
                    lastIndex++;
                    lastReleaseIndex = nextCandidate.Note is null ? lastReleaseIndex : lastIndex;
                }

                // Whether a note another block's release can start a run with follows the stretch, so the run ends behind the notes it cannot reach
                // rather than in front of them.
                bool isAnotherReleaseNext = lastIndex + 1 < notes.Count && candidates[lastIndex + 1] is { Note: not null };
                lastIndex = isAnotherReleaseNext ? lastIndex : lastReleaseIndex;

                var runNotes = candidates[firstIndex..(lastIndex + 1)].Select(candidate => candidate!.Note).OfType<ReleaseNote>().ToList();
                int clearTick = notes[lastIndex + 1].StartTick;
                int restatementBytes = DoesCrossLoopMarker(firstNote.StartTick, clearTick, markerTick) ? SongDataSize.RemoteCallBytes : 0;
                int netBytesSaved = candidates[firstIndex..(lastIndex + 1)].Sum(candidate => candidate!.BytesSaved) - RunEdgeBytes - restatementBytes;
                if (netBytesSaved > 0)
                {
                    runs.Add(new ReleaseRun(firstNote.StartTick, clearTick, runNotes, blockText, netBytesSaved));
                }

                firstIndex = lastIndex + 1;
            }

            return runs;
        }

        // Each note's release written by a key-off block under the options, a note the block cannot run on at all as one with no release, or null where
        // the note cannot join a run.
        private static ReleaseCandidate?[] Candidates(Song song, int channel, MmlExportOptions options, List<Note> notes, IReadOnlyList<ArpeggioRun> arpeggioRuns)
        {
            var shape = options.HeldNoteShapeWritten();
            var commands = ChannelCommands(song, channel, options).ToList();
            int markerTick = LoopMarkerTick(song);
            var breakTicks = commands.Select(command => command.Tick).Append(markerTick).ToHashSet();
            var slots = ChannelSlots.For(song, channel, shape);
            var staccato = ChangeMap.ForStaccato(song);
            var envelope = EnvelopeMap.For(song, channel);

            var candidates = new ReleaseCandidate?[notes.Count];
            for (int noteIndex = 0; noteIndex + 1 < notes.Count; noteIndex++)
            {
                var note = notes[noteIndex];
                int nextStartTick = notes[noteIndex + 1].StartTick;
                if (IsAbandonedByLegato(song, note, nextStartTick, slots, arpeggioRuns))
                {
                    candidates[noteIndex] = new ReleaseCandidate(null, null, 0);
                    continue;
                }

                if (!CanJoinRun(song, notes, noteIndex, slots, arpeggioRuns))
                {
                    continue;
                }

                // Where the stretch the note's ring is worked out over ends: the loop marker where it falls in the rest behind the note, whose first
                // piece the ring then ends at, and the next note otherwise.
                bool isMarkerInRestBehind = note.EndTick() <= markerTick && markerTick < nextStartTick;
                int spanEndTick = isMarkerInRestBehind ? markerTick : nextStartTick;

                // The latest tick the note can be written to end at: short of the marker, which only a rest may reach, or the next note.
                int latestEndTick = isMarkerInRestBehind ? markerTick - 1 : nextStartTick;

                var waitTimes = SnesTiming.WaitTimesFrom(staccato, note.StartTick);
                var ticksInside = breakTicks.Where(tick => tick > note.StartTick && tick < spanEndTick).ToList();
                if (ticksInside.Count == 0 && note.EndTick() == nextStartTick && IsOutOfBlockReach(note, waitTimes))
                {
                    candidates[noteIndex] = new ReleaseCandidate(null, null, 0);
                    continue;
                }

                if (ticksInside.Count != 1 || ticksInside[0] >= note.EndTick())
                {
                    continue;
                }

                int releaseTick = ticksInside[0];
                var releaseCommands = commands.Where(command => command.Tick == releaseTick).ToList();
                if (releaseCommands.Any(command => command.SortOrder != EnvelopeOrder))
                {
                    continue;
                }

                if (WrittenUnderBlock(note, releaseTick, spanEndTick, latestEndTick, waitTimes, options) is not { } written)
                {
                    continue;
                }

                int bytesSaved = releaseCommands.Sum(command => WrittenTokens.CompiledBytesOf(command.Text)) + TieBytes - RestCutBytes(written.RestStartTick, written.RestCutTick, spanEndTick);
                var releaseNote = new ReleaseNote(note.StartTick, written.LengthTicks, written.Gate, releaseTick, written.RestCutTick);
                string blockText = BlockTextFor(envelope.StateAt(note.StartTick), envelope.StateAt(releaseTick), releaseCommands);
                candidates[noteIndex] = new ReleaseCandidate(releaseNote, blockText, bytesSaved);
            }

            return candidates;
        }

        // The commands a release is written as in a block: the GAIN form where it leaves the voice what the plain export's commands do from the
        // envelope the note keys on under, and otherwise those commands themselves.
        private static string BlockTextFor(EnvelopeState keyOnEnvelope, EnvelopeState releaseEnvelope, List<TimedCommand> releaseCommands)
        {
            if (keyOnEnvelope.WithGainWritten(releaseEnvelope.Gain) == releaseEnvelope)
            {
                return GainName.Write(releaseEnvelope.Gain);
            }

            return string.Join(' ', releaseCommands.Select(command => command.Text));
        }

        // Whether the note at an index can have its release written by a key-off block: played and keying on plainly, on no bend and in no arpeggio run,
        // with no legato toggle before the next note and not bound to it.
        private static bool CanJoinRun(Song song, List<Note> notes, int noteIndex, ChannelSlots slots, IReadOnlyList<ArpeggioRun> arpeggioRuns)
        {
            var note = notes[noteIndex];
            var nextNote = notes[noteIndex + 1];
            return song.IsNotePlayable(note)
                   && slots.KeysOnPlainly(note)
                   && note.Bends.IsEmpty
                   && !LegatoToggles.DoesBindNext(notes, noteIndex)
                   && !slots.Legato.ToggleTicks.Any(tick => tick > note.StartTick && tick < nextNote.StartTick)
                   && !ArpeggioRuns.IsInAnyRun(arpeggioRuns, note);
        }

        // Whether a note reaches none of its cuts: played under the legato's bit with the next note straight behind it, outside every arpeggio run and
        // every held note's block.
        private static bool IsAbandonedByLegato(Song song, Note note, int nextStartTick, ChannelSlots slots, IReadOnlyList<ArpeggioRun> arpeggioRuns)
        {
            return song.IsNotePlayable(note)
                   && slots.Legato.IsBitSetAt(note.StartTick)
                   && !slots.Hold.IsArmedAt(note.StartTick)
                   && note.EndTick() == nextStartTick
                   && !ArpeggioRuns.IsInAnyRun(arpeggioRuns, note);
        }

        // Whether a note written as it is, with the next note straight behind it, gives an armed block no tick to run at, its voice keyed off where the
        // plain export keys it off: the gate's cut falling on the wait time's or on neither.
        private static bool IsOutOfBlockReach(Note note, Func<int, int> waitTimes)
        {
            var triggers = SnesTiming.KeyOffTriggers.For(note.LengthTicks, note.Gate, null, waitTimes);
            return triggers.KeyOffBlockOffsets(false).Count == 0 && triggers.LeadTicks + triggers.WaitTicks == SnesTiming.KeyOnTicks(note.LengthTicks, note.Gate, null, waitTimes);
        }

        // How a note is written for an armed key-off block to start its release at the tick and for its voice to be keyed off where the plain export
        // keys it off, or null where no way does: its own length first, and then each from the shortest up to the latest end, the next note straight
        // behind it or a rest the voice can ring on into filling the span - read to its end, or where the options let a rest longer than one byte be cut,
        // to the end of a first piece of each length one byte holds.
        private static WrittenRunNote? WrittenUnderBlock(Note note, int releaseTick, int spanEndTick, int latestEndTick, Func<int, int> waitTimes, MmlExportOptions options)
        {
            int releaseOffset = releaseTick - note.StartTick;
            int plainKeyOnTicks = SnesTiming.KeyOnTicks(note.LengthTicks, note.Gate, note.LengthTicks - releaseOffset, waitTimes);
            int spanTicks = spanEndTick - note.StartTick;

            var lengths = Enumerable.Range(releaseOffset + 1, latestEndTick - releaseTick).OrderBy(lengthTicks => lengthTicks == note.LengthTicks ? 0 : 1);

            foreach (int lengthTicks in lengths)
            {
                foreach (int ringRestTicks in RingRestLengths(spanTicks - lengthTicks, options))
                {
                    if (GateUnderBlock(lengthTicks, releaseOffset, plainKeyOnTicks, ringRestTicks, waitTimes) is not int gate)
                    {
                        continue;
                    }

                    int restStartTick = note.StartTick + lengthTicks;
                    int? restCutTick = ringRestTicks < spanTicks - lengthTicks ? restStartTick + ringRestTicks : null;
                    return new WrittenRunNote(lengthTicks, gate, restStartTick, restCutTick);
                }
            }

            return null;
        }

        // The lengths of the rest a voice can ring on into behind a note, the rest given being the whole of what follows the note: the whole rest, none
        // where a note is next, and then, where the options let a rest longer than one byte be cut, each first piece one byte holds.
        private static IEnumerable<int> RingRestLengths(int restTicks, MmlExportOptions options)
        {
            yield return restTicks;

            if (!options.AreLongNotesAndRestsWrittenAsLoops() || restTicks <= MmlDurationEncoding.MaxTicksWithoutAutoTies)
            {
                yield break;
            }

            for (int pieceTicks = 1; pieceTicks <= MmlDurationEncoding.MaxTicksWithoutAutoTies; pieceTicks++)
            {
                yield return pieceTicks;
            }
        }

        // The bytes cutting the rest from one tick to another at a tick costs over writing it whole, none where it is not cut.
        private static int RestCutBytes(int restStartTick, int? restCutTick, int restEndTick)
        {
            if (restCutTick is not int cutTick)
            {
                return 0;
            }

            return MmlDurationEncoding.EstimatedRunsBytes([restStartTick, cutTick], restEndTick) - MmlDurationEncoding.EstimatedRunsBytes([restStartTick], restEndTick);
        }

        // The gate, the full gate tried first, under which a note written at a length has its block run first at the release's offset and its voice
        // keyed off after the plain export's key-on length, or null where no gate does: with the next note behind it the wait time's cut keys it off,
        // and with a rest the voice rings on into the rest until the rest's own cut, under the same gate.
        private static int? GateUnderBlock(int lengthTicks, int releaseOffset, int plainKeyOnTicks, int ringRestTicks, Func<int, int> waitTimes)
        {
            bool isRestNext = ringRestTicks > 0;
            for (int gate = SnesTiming.MaxGate; gate >= SnesTiming.MinGate; gate--)
            {
                var triggers = SnesTiming.KeyOffTriggers.For(lengthTicks, gate, null, waitTimes);
                var blockOffsets = triggers.KeyOffBlockOffsets(isRestNext);
                if (blockOffsets.Count == 0 || triggers.LeadTicks + blockOffsets[0] != releaseOffset)
                {
                    continue;
                }

                int keyOnTicks = isRestNext
                    ? lengthTicks + SnesTiming.KeyOnTicks(ringRestTicks, gate, null, offsetTicks => waitTimes(lengthTicks + offsetTicks))
                    : triggers.LeadTicks + triggers.WaitTicks;
                bool doesBlockRunBeforeKeyOff = isRestNext || blockOffsets[0] < triggers.WaitTicks;
                if (doesBlockRunBeforeKeyOff && keyOnTicks == plainKeyOnTicks)
                {
                    return gate;
                }
            }

            return null;
        }

        // A note that can join a run, the block's commands its release is written as, and the bytes its own release and the tie it splits off cost;
        // with no note and no commands for a note the block cannot run on, which joins a run of any block and saves nothing.
        private sealed record ReleaseCandidate(ReleaseNote? Note, string? BlockText, int BytesSaved);

        // How a note of a run is written: the length and gate it is written under, the tick the rest behind it starts at, and the tick that rest is cut
        // at, null where it is not.
        private sealed record WrittenRunNote(int LengthTicks, int Gate, int RestStartTick, int? RestCutTick);
    }
}
