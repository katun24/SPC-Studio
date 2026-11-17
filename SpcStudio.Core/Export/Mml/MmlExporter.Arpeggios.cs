using SpcStudio.Import;
using SpcStudio.Model;

namespace SpcStudio.Export;

// Turns a song into an AddmusicK MML file. This part holds the runs of notes written as one base note under an $FB: finding them, and keeping the ones
// the import reads back as the same notes.
public static partial class MmlExporter
{
    // A run of notes an arpeggio or a trill plays, written as one base note: where it starts and ends, the base note's semitone, the $FB in front of
    // it, and whether a run straight after it with the same $FB can play under this one's.
    private sealed record ArpeggioRun(int StartTick, int EndTick, int BaseSemitone, string Command, bool CanNextRunReuseCommand);

    // Each channel's runs of notes worth writing under an $FB, kept only where the import reads them back as the notes they replace.
    private static class ArpeggioRuns
    {
        private const int MinStepCount = 2;
        private const int TrillPeriod = 2;
        private const int MaxStepTicks = byte.MaxValue;
        private const int MaxNoteListLength = MmlArpeggio.FirstSpecialForm - 1;
        private const int NoChannel = -1;
        private const int MinSubloopRepeatCount = 2;

        private static readonly int OffBytes = WrittenTokens.CompiledBytesOf(ArpOffName.Write());

        // No run on any channel.
        public static IReadOnlyList<ArpeggioRun>[] NoneOnAnyChannel()
        {
            return [.. Enumerable.Range(0, Song.DriverChannelCount).Select(_ => (IReadOnlyList<ArpeggioRun>)[])];
        }

        // Whether a note starts inside one of the runs: the first step a run's base note is written for, or a step under it.
        public static bool IsInAnyRun(IReadOnlyList<ArpeggioRun> runs, Note note)
        {
            return runs.Any(run => run.StartTick <= note.StartTick && note.StartTick < run.EndTick);
        }

        // Every channel's runs worth writing that the import reads back as the notes the plain export plays, under the options' held note shape.
        public static IReadOnlyList<ArpeggioRun>[] ForEachChannel(Song song, MmlExportOptions options)
        {
            var runsByChannel = Enumerable.Range(0, Song.DriverChannelCount).Select(channel => Found(song, channel, options)).ToArray();
            if (runsByChannel.All(runs => runs.Count == 0))
            {
                return runsByChannel;
            }

            var checkOptions = options with { Loops = false, KeyOnRemoteCode = false, RepeatedMidNoteCommandsAsRemoteCode = false };
            if (ReadBacks.ReadBack(Build(song, checkOptions, NoneOnAnyChannel())) is not { } plainReadBack)
            {
                return NoneOnAnyChannel();
            }

            while (runsByChannel.Any(runs => runs.Count > 0))
            {
                if (ReadBacks.ReadBack(Build(song, checkOptions, runsByChannel)) is not { } readBackWithRuns)
                {
                    return NoneOnAnyChannel();
                }

                if (!DropMisreadRuns(runsByChannel, plainReadBack, readBackWithRuns))
                {
                    break;
                }
            }

            return runsByChannel;
        }

        // The runs on one channel that save bytes: every stretch of notes that could be steps cut into runs, each chain of runs touching one another
        // kept where it saves, under the options.
        private static List<ArpeggioRun> Found(Song song, int channel, MmlExportOptions options)
        {
            var notes = song.NotesInsideSong(channel).ToList();
            var slots = ChannelSlots.For(song, channel, options.HeldNoteShapeWritten());
            int markerTick = LoopMarkerTick(song);
            var commandTicks = ChannelCommands(song, channel, options).Select(command => command.Tick).ToHashSet();
            var runs = new List<ArpeggioRun>();

            for (int firstIndex = 0; firstIndex < notes.Count;)
            {
                int stepCount = StepCountFrom(song, notes, firstIndex, slots, markerTick);
                if (stepCount < MinStepCount)
                {
                    firstIndex++;
                    continue;
                }

                foreach (var chain in Chains(RunsCovering(notes.GetRange(firstIndex, stepCount), commandTicks)))
                {
                    runs.AddRange(PayingPart(chain, commandTicks, markerTick).Select(candidate => candidate.Run));
                }

                firstIndex += stepCount;
            }

            return runs;
        }

        // Whether a run plays under the $FB of the run before it rather than loading its own: the one before ends where it starts, away from the loop
        // marker, with the same command, which a trill leaves at the phase it starts at only after an even number of steps.
        public static bool DoesPlayUnderPrevious(ArpeggioRun run, ArpeggioRun? previousRun, int markerTick)
        {
            return previousRun is not null && previousRun.EndTick == run.StartTick && run.StartTick != markerTick && previousRun.Command == run.Command && previousRun.CanNextRunReuseCommand;
        }

        // How many notes from one on could be steps of one run: touching, of the first one's length but for the last, which may be as much as the wait
        // time longer, and sharing its instrument, velocity and fine tune, none at the loop marker but the first.
        private static int StepCountFrom(Song song, List<Note> notes, int firstIndex, ChannelSlots slots, int markerTick)
        {
            var firstStep = notes[firstIndex];
            if (!CanBeStep(song, firstStep, slots))
            {
                return 0;
            }

            int stepCount = 1;
            while (firstIndex + stepCount < notes.Count)
            {
                var previousStep = notes[firstIndex + stepCount - 1];
                var note = notes[firstIndex + stepCount];
                bool doesJoin = previousStep.LengthTicks == firstStep.LengthTicks
                                && note.StartTick == previousStep.EndTick()
                                && note.StartTick != markerTick
                                && note.LengthTicks <= firstStep.LengthTicks + SnesTiming.DefaultWaitTime
                                && note.InstrumentId == firstStep.InstrumentId
                                && note.Velocity == firstStep.Velocity
                                && note.FineTune == firstStep.FineTune
                                && CanBeStep(song, note, slots);
                if (!doesJoin)
                {
                    break;
                }

                stepCount++;
            }

            return stepCount;
        }

        // Whether a note can be a step at all: played on a pitched instrument that is not noise, at the full gate, with no bend, not held or bound, and
        // keying on with the legato's bit clear and neither remote slot armed.
        private static bool CanBeStep(Song song, Note note, ChannelSlots slots)
        {
            var instrument = song.InstrumentOf(note);
            return instrument is not null
                   && song.IsNotePlayable(note)
                   && !instrument.IsPercussion
                   && !instrument.IsNoise
                   && note.Gate == SnesTiming.DefaultGate
                   && note.LengthTicks <= MaxStepTicks
                   && note.Bends.IsEmpty
                   && slots.KeysOnPlainly(note);
        }

        // A stretch of steps cut into runs from its first step on, each the run saving the most bytes from where it starts, a step no repeat starts at
        // being left as a note.
        private static List<RunCandidate> RunsCovering(List<Note> steps, HashSet<int> commandTicks)
        {
            var candidates = new List<RunCandidate>();

            for (int firstIndex = 0; firstIndex < steps.Count;)
            {
                if (MostSavingRunFrom(steps, firstIndex, commandTicks) is not { } candidate)
                {
                    firstIndex++;
                    continue;
                }

                candidates.Add(candidate.Run);
                firstIndex += candidate.StepCount;
            }

            return candidates;
        }

        // Of the runs from one step on whose pitches repeat a period at least once past it, the one saving the most bytes on its own - its steps with no
        // $FB against its own $FB and base note - the shortest period of those that tie, and how many steps it takes, or null where no period repeats.
        private static (RunCandidate Run, int StepCount)? MostSavingRunFrom(List<Note> steps, int firstIndex, HashSet<int> commandTicks)
        {
            (RunCandidate Run, int StepCount)? mostSaving = null;
            int mostBytesSaved = int.MinValue;

            for (int period = 1; period <= Math.Min(MaxNoteListLength, steps.Count - firstIndex - 1); period++)
            {
                int stepCount = RepeatingStepCount(steps, firstIndex, period);
                if (stepCount == period)
                {
                    continue;
                }

                var runSteps = steps.GetRange(firstIndex, stepCount);
                var candidate = new RunCandidate(RunOf(runSteps, period), BytesWithoutFb(runSteps));
                int bytesSaved = candidate.BytesWithoutFb - WrittenTokens.CompiledBytesOf(candidate.Run.Command) - BaseNoteBytes(candidate.Run, commandTicks);
                if (bytesSaved > mostBytesSaved)
                {
                    mostBytesSaved = bytesSaved;
                    mostSaving = (candidate, stepCount);
                }
            }

            return mostSaving;
        }

        // How many steps from one on repeat the pitches of the period's first ones.
        private static int RepeatingStepCount(List<Note> steps, int firstIndex, int period)
        {
            int stepCount = period;
            while (firstIndex + stepCount < steps.Count && steps[firstIndex + stepCount].Semitone == steps[firstIndex + stepCount % period].Semitone)
            {
                stepCount++;
            }

            return stepCount;
        }

        // The run its steps are written as under a period: two pitches as the trill, from the second step's pitch, and any other number as a note list
        // from the first step's.
        private static ArpeggioRun RunOf(List<Note> steps, int period)
        {
            int stepTicks = steps[0].LengthTicks;
            if (period == TrillPeriod)
            {
                int baseSemitone = steps[1].Semitone;
                string trill = ArpName.Write(MmlArpeggio.TrillForm, stepTicks, DeltaByte(steps[0].Semitone - baseSemitone));
                return new ArpeggioRun(steps[0].StartTick, steps[^1].EndTick(), baseSemitone, trill, steps.Count % TrillPeriod == 0);
            }

            var deltaBytes = steps.Take(period).Select(step => DeltaByte(step.Semitone - steps[0].Semitone));
            return new ArpeggioRun(steps[0].StartTick, steps[^1].EndTick(), steps[0].Semitone, ArpName.Write([period, stepTicks, .. deltaBytes]), true);
        }

        // A semitone delta as the signed byte the driver adds.
        private static int DeltaByte(int deltaSemitones)
        {
            return deltaSemitones & byte.MaxValue;
        }

        // A guess at the bytes a run's steps take with no $FB: as notes, or where the shortest period their pitches repeat is played at least twice, as
        // the subloop the loops could write them as, the steps past the last whole repeat behind it, whichever is fewer.
        private static int BytesWithoutFb(List<Note> steps)
        {
            int noteBytes = NoteBytes(steps);
            int period = Enumerable.Range(1, steps.Count).First(period => RepeatingStepCount(steps, 0, period) == steps.Count);
            int wholeRepeatCount = steps.Count / period;
            if (wholeRepeatCount < MinSubloopRepeatCount)
            {
                return noteBytes;
            }

            int repeatedStepCount = wholeRepeatCount * period;
            int subloopBytes = LoopCompression.SubloopBytesOver(NoteBytes(steps.GetRange(0, period))) + NoteBytes(steps.GetRange(repeatedStepCount, steps.Count - repeatedStepCount));
            return Math.Min(noteBytes, subloopBytes);
        }

        // A guess at the bytes notes take written one after another.
        private static int NoteBytes(List<Note> notes)
        {
            int durationTicksInForce = MmlDurationEncoding.NoDurationTicks;
            return notes.Sum(note => MmlDurationEncoding.EstimatedRunBytes(note.LengthTicks, ref durationTicksInForce));
        }

        // The runs in order, parted into chains of runs each starting where the one before it ends.
        private static List<List<RunCandidate>> Chains(List<RunCandidate> candidates)
        {
            var chains = new List<List<RunCandidate>>();
            foreach (var candidate in candidates)
            {
                if (chains.Count == 0 || chains[^1][^1].Run.EndTick != candidate.Run.StartTick)
                {
                    chains.Add([]);
                }

                chains[^1].Add(candidate);
            }

            return chains;
        }

        // The longest part of a chain that saves bytes, found by taking off the run at whichever end leaves the more saved until what is left saves any.
        private static List<RunCandidate> PayingPart(List<RunCandidate> chain, HashSet<int> commandTicks, int markerTick)
        {
            while (chain.Count > 0 && BytesSaved(chain, commandTicks, markerTick) <= 0)
            {
                var withoutFirst = chain.GetRange(1, chain.Count - 1);
                var withoutLast = chain.GetRange(0, chain.Count - 1);
                chain = BytesSaved(withoutLast, commandTicks, markerTick) > BytesSaved(withoutFirst, commandTicks, markerTick) ? withoutLast : withoutFirst;
            }

            return chain;
        }

        // A guess at the bytes a chain saves over its steps written with no $FB, erring low: each run's $FB where it loads one and its base note broken at
        // every command inside it, and behind the chain the switch off and the duration the next note has to state again.
        private static int BytesSaved(List<RunCandidate> chain, HashSet<int> commandTicks, int markerTick)
        {
            if (chain.Count == 0)
            {
                return 0;
            }

            int runBytes = OffBytes + SongDataSize.DurationByteBytes;
            for (int runIndex = 0; runIndex < chain.Count; runIndex++)
            {
                var run = chain[runIndex].Run;
                var previousRun = runIndex > 0 ? chain[runIndex - 1].Run : null;
                runBytes += BaseNoteBytes(run, commandTicks) + (DoesPlayUnderPrevious(run, previousRun, markerTick) ? 0 : WrittenTokens.CompiledBytesOf(run.Command));
            }

            return chain.Sum(candidate => candidate.BytesWithoutFb) - runBytes;
        }

        // A guess at the bytes a run's base note takes, broken at every command inside it.
        private static int BaseNoteBytes(ArpeggioRun run, HashSet<int> commandTicks)
        {
            List<int> pieceStartTicks = [run.StartTick, .. commandTicks.Where(tick => tick > run.StartTick && tick < run.EndTick).Order()];
            return MmlDurationEncoding.EstimatedRunsBytes(pieceStartTicks, run.EndTick);
        }

        // Drops the runs the read-back shows playing other than the plain export - every run of a channel an arpeggio warning names, every run where
        // another warning comes with them, and on a channel whose notes part, the last run starting at or before the first note apart, or all of that
        // channel's where none does - and answers whether any was dropped.
        private static bool DropMisreadRuns(List<ArpeggioRun>[] runsByChannel, StreamsReadBack plainReadBack, StreamsReadBack readBackWithRuns)
        {
            bool isAnyDropped = false;
            foreach (string warning in ReadBacks.NewWarnings(plainReadBack, readBackWithRuns))
            {
                int channel = Enumerable.Range(0, runsByChannel.Length).FirstOrDefault(number => IsArpeggioWarningFor(warning, number), NoChannel);
                if (channel == NoChannel)
                {
                    return DropAll(runsByChannel);
                }

                runsByChannel[channel].Clear();
                isAnyDropped = true;
            }

            foreach (var (channel, firstTickApart) in ReadBacks.FirstTicksApartByChannel(plainReadBack, readBackWithRuns))
            {
                var runs = runsByChannel[channel];
                if (firstTickApart is not int tickApart)
                {
                    continue;
                }

                if (runs.Count == 0)
                {
                    return DropAll(runsByChannel);
                }

                int runIndex = runs.FindLastIndex(run => run.StartTick <= tickApart);
                if (runIndex < 0)
                {
                    runs.Clear();
                }
                else
                {
                    runs.RemoveAt(runIndex);
                }

                isAnyDropped = true;
            }

            return isAnyDropped;
        }

        // Drops every run on every channel, and answers that runs were dropped.
        private static bool DropAll(List<ArpeggioRun>[] runsByChannel)
        {
            foreach (var runs in runsByChannel)
            {
                runs.Clear();
            }

            return true;
        }

        // Whether a warning is one of the two the arpeggio expansion gives about a channel.
        private static bool IsArpeggioWarningFor(string warning, int channel)
        {
            return warning == ImportWarnings.ArpeggioTimingFromOtherChannel(channel) || warning == ImportWarnings.ArpeggioRunningAtSongEnd(channel);
        }

        // A run found and a guess at the bytes its steps take with no $FB.
        private sealed record RunCandidate(ArpeggioRun Run, int BytesWithoutFb);
    }
}
