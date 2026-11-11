using System.Collections.Immutable;
using SpcStudio.Audio;
using SpcStudio.Model;

namespace SpcStudio.Import;

// What an .spc plays, written out as a song: every sample the trace heard, an instrument for every sample and envelope heard together, a channel of notes
// per DSP voice placed through the song's own measured rhythm, the master volumes on the song volume lane, each voice's levels on the channel volume and
// pan lanes, and the echo off the registers.
public static class SpcTranscribe
{
    public const int StageCount = 6;
    private const int LevelsReadUnderMasterVolume = SnesVolume.MaxMasterVolume;
    private const int ReleaseOverhangFrames = 128;
    private const int MinWalkSteps = 2;
    private const int MaxWalkSemitones = 24;
    private const int MinBendsApartTicks = 2;
    private const int MaxSweepStartAfterNoteTicks = 1;
    private const int MaxTuningShiftOctaves = 16;
    private const double MaxAlignSemitones = 0.5;
    private const int ChannelVolumeFadeTolerance = 1;
    private const int PanFadeTolerance = 1;
    private const int EchoVolumeFadeTolerance = 1;
    private const int MasterVolumeFadeTolerance = 1;
    private const string SamplesMeasuredStage = "samples measured";
    private const string BuildingInstrumentsStage = "Building the instruments";
    private const string RhythmWindowsStage = "rhythm windows";
    private const string VoicesReadStage = "voices read";
    private const string PlacingNotesStage = "Placing the notes";
    private const string ReadingLevelsStage = "Reading the levels";

    private static readonly int VelocityByte = SnesVolume.VelocityByteFor(SnesVolume.DefaultVelocity, usesSmwRow: false);

    // A traced .spc's sounds as a song under the file's tags, a sample the trace could not read being left out with every note that played it.
    public static Song ToSong(SpcFile file, SpcTrace trace, Action<string>? warn = null, Action<string, int, int>? progress = null, CancellationToken cancel = default)
    {
        var song = new Song
        {
            SpcTags = new SpcTags(file.Title, file.Artist, file.Game, file.Comment),
        };

        var traceWithSrcnsMerged = trace.WithSrcnsMerged(MergedSrcnOf(trace));
        var (samplesBySrcn, skippedCount) = AddHeardSamples(song, traceWithSrcnsMerged, progress, cancel);
        progress?.Invoke(BuildingInstrumentsStage, 0, 0);
        var instruments = AddInstruments(song, traceWithSrcnsMerged, samplesBySrcn);
        if (skippedCount > 0)
        {
            warn?.Invoke(SpcTranscribeWarnings.SamplesUnreadable(skippedCount));
        }

        var grid = PlaceNotes(song, traceWithSrcnsMerged, instruments, warn, progress, cancel);
        progress?.Invoke(ReadingLevelsStage, 0, 0);
        WriteLevels(song, traceWithSrcnsMerged, grid, warn, cancel);
        cancel.ThrowIfCancellationRequested();
        WriteEcho(song, traceWithSrcnsMerged, grid);
        cancel.ThrowIfCancellationRequested();
        SharedNoteValues.MoveMostPlayedValuesOntoInstruments(song);
        return song;
    }

    // Each heard sample number with the lowest heard number whose sample the trace read holds the very same bytes, loop included, a number it could not read keeping its own.
    private static Dictionary<int, int> MergedSrcnOf(SpcTrace trace)
    {
        var mergedSrcnOf = new Dictionary<int, int>();
        var keptSamples = new List<(int Srcn, byte[] FileBytes)>();
        foreach (int srcn in trace.HeardSrcns)
        {
            mergedSrcnOf[srcn] = srcn;
            if (!trace.BrrFileBytesBySrcn.TryGetValue(srcn, out var fileBytes))
            {
                continue;
            }

            int sameBytesKeptIndex = keptSamples.FindIndex(kept => kept.FileBytes.AsSpan().SequenceEqual(fileBytes));
            if (sameBytesKeptIndex >= 0)
            {
                mergedSrcnOf[srcn] = keptSamples[sameBytesKeptIndex].Srcn;
                continue;
            }

            keptSamples.Add((srcn, fileBytes));
        }

        return mergedSrcnOf;
    }

    // Every sample the trace heard added to the song by its directory number, and how many could not be read out of the dump.
    private static (Dictionary<int, Sample> SamplesBySrcn, int SkippedCount) AddHeardSamples(Song song, SpcTrace trace, Action<string, int, int>? progress, CancellationToken cancel)
    {
        var samplesBySrcn = new Dictionary<int, Sample>();
        int skippedCount = 0;
        int measuredCount = 0;
        progress?.Invoke(SamplesMeasuredStage, 0, trace.HeardSrcns.Count);
        foreach (int srcn in trace.HeardSrcns)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(SamplesMeasuredStage, measuredCount++, trace.HeardSrcns.Count);
            if (!trace.BrrFileBytesBySrcn.TryGetValue(srcn, out var fileBytes))
            {
                skippedCount++;
                continue;
            }

            Sample sample;
            try
            {
                sample = Sample.FromBytes(song.FreeSampleName(), fileBytes);
            }
            catch (Exception)
            {
                skippedCount++;
                continue;
            }

            samplesBySrcn[srcn] = song.AddSample(sample);
        }

        progress?.Invoke(SamplesMeasuredStage, measuredCount, trace.HeardSrcns.Count);
        return (samplesBySrcn, skippedCount);
    }

    // A numbered instrument for every sample and audible envelope the trace heard together, in the order it first heard each, on the envelope it first heard and keyed by the pair.
    private static Dictionary<(int Srcn, EnvelopeState AudibleEnvelope), Instrument> AddInstruments(Song song, SpcTrace trace, Dictionary<int, Sample> samplesBySrcn)
    {
        var instruments = new Dictionary<(int Srcn, EnvelopeState AudibleEnvelope), Instrument>();
        var tunings = TuningsBySrcn(trace, samplesBySrcn);
        foreach (var block in FirstBlockOfEachInstrument(trace))
        {
            if (!samplesBySrcn.TryGetValue(block.Srcn, out var sample))
            {
                continue;
            }

            var instrument = song.CreateNumberedInstrument(sample);
            instrument.TuningMultiplier = tunings[block.Srcn];
            instrument.Envelope = block.KeyOnEnvelope;
            instruments[InstrumentKeyOf(block)] = instrument;
        }

        return instruments;
    }

    // Every key-on placed as notes, one channel per DSP voice, through the rhythm the song's own onsets were measured to have - which is returned, the
    // markers, the tempo lane and the levels all being laid out on it.
    private static SpcGrid PlaceNotes(
        Song song, SpcTrace trace, IReadOnlyDictionary<(int Srcn, EnvelopeState AudibleEnvelope), Instrument> instruments, Action<string>? warn,
        Action<string, int, int>? progress, CancellationToken cancel)
    {
        var grid = SpcGridFit.FindGrid(OnsetsOf(trace, instruments), trace.LengthFrames, (done, total) => progress?.Invoke(RhythmWindowsStage, done, total), cancel);
        if (SnapSize.IsValid(grid.StepTicks))
        {
            song.SnapTicks = grid.StepTicks;
        }

        if (grid.FitScore < SpcGridFit.MinBelievableFitScore)
        {
            warn?.Invoke(SpcTranscribeWarnings.NoClearRhythm());
        }

        var sweepSpeeds = new SweepSpeeds(CycleFramesByInstrument(trace, instruments, block => block.Vibratos), CycleFramesByInstrument(trace, instruments, block => block.Tremolos));
        var placed = new List<(int Channel, Note Note)>();
        int outOfRangeCount = 0;
        int unfittedCount = 0;
        int bendCount = 0;
        progress?.Invoke(VoicesReadStage, 0, SnesDspRegister.VoiceCount);
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Invoke(VoicesReadStage, voice, SnesDspRegister.VoiceCount);
            var sounded = SoundedNotesOf(trace, voice, instruments, sweepSpeeds);
            for (int at = 0; at < sounded.Count; at++)
            {
                long nextStartFrame = at + 1 < sounded.Count ? sounded[at + 1].Note.Start : trace.LengthFrames;
                if (WrittenNoteFor(sounded[at], nextStartFrame, grid) is not { } written)
                {
                    continue;
                }

                outOfRangeCount += written.IsOutOfRange ? 1 : 0;
                unfittedCount += written.IsUnfitted ? 1 : 0;
                bendCount += written.Note.Bends.Length;
                placed.Add((voice, written.Note with { Envelopes = EnvelopeRowsFor(sounded[at], grid, written.Note) }));
            }
        }

        progress?.Invoke(VoicesReadStage, SnesDspRegister.VoiceCount, SnesDspRegister.VoiceCount);
        progress?.Invoke(PlacingNotesStage, 0, 0);
        song.PlaceNotes(SpcCloseChains.Unified(placed));
        song.SetEndTick(grid.TidiedTickAt(trace.LengthFrames));
        song.IsLooping = trace.LoopFrame is not null;
        if (trace.LoopFrame is { } loopFrame)
        {
            song.SetLoopTick(grid.TidiedTickAt(loopFrame));
        }

        WriteTempos(song, grid);
        WarnAboutNotes(warn, outOfRangeCount, unfittedCount, bendCount);
        return grid;
    }

    // The speed each instrument's swings of one sweep go at, read from all of them together.
    private static Dictionary<int, double> CycleFramesByInstrument(
        SpcTrace trace, IReadOnlyDictionary<(int Srcn, EnvelopeState AudibleEnvelope), Instrument> instruments, Func<SpcTraceBlock, ImmutableArray<SpcTraceSwing>> swingsOf)
    {
        return SpcSwingSpeeds.CycleFramesByInstrument(
            trace.Blocks.Where(block => instruments.ContainsKey(InstrumentKeyOf(block)))
                .SelectMany(block => swingsOf(block).Select(swing => (instruments[InstrumentKeyOf(block)].Id, swing))));
    }

    // When every note an instrument was built for keyed on.
    private static List<long> OnsetsOf(SpcTrace trace, IReadOnlyDictionary<(int Srcn, EnvelopeState AudibleEnvelope), Instrument> instruments)
    {
        var onsets = new List<long>();
        foreach (var block in trace.Blocks)
        {
            if (!instruments.ContainsKey(InstrumentKeyOf(block)))
            {
                continue;
            }

            foreach (var note in block.Notes)
            {
                onsets.Add(note.Start);
            }
        }

        return onsets;
    }

    // A voice's notes in the order it played them, each with its instrument, whether it is its block's last, whether the voice was heard to fall silent after it, whether it
    // sounds on into the loop, the slides, vibratos and tremolos that start inside it - each vibrato and tremolo at its instrument's speed where it was read close to it - and
    // where its block keyed on and moved its envelope to.
    private static List<SoundedNote> SoundedNotesOf(
        SpcTrace trace, int voice, IReadOnlyDictionary<(int Srcn, EnvelopeState AudibleEnvelope), Instrument> instruments, SweepSpeeds speeds)
    {
        var voiceBlocks = trace.Blocks.Where(block => block.Voice == voice).ToList();
        var sounded = new List<SoundedNote>();
        for (int blockIndex = 0; blockIndex < voiceBlocks.Count; blockIndex++)
        {
            var block = voiceBlocks[blockIndex];
            if (!instruments.TryGetValue(InstrumentKeyOf(block), out var instrument))
            {
                continue;
            }

            bool isBlockReleased = block.End < trace.LengthFrames && (blockIndex + 1 >= voiceBlocks.Count || voiceBlocks[blockIndex + 1].Start > block.End);
            bool isBlockHeldIntoLoop = trace.LoopFrame is not null && block.IsHeardPastTraceEnd;
            var walks = SpcPitchPath.WalksOf(block);
            var envelopeMoves = HeardEnvelopeMoves(block);
            for (int noteIndex = 0; noteIndex < block.Notes.Length; noteIndex++)
            {
                bool isBlockLastNote = noteIndex == block.Notes.Length - 1;
                var note = block.Notes[noteIndex];
                var walksInside = walks.Where(walk => walk.Start >= note.Start && walk.Start < note.End).ToImmutableArray();
                var vibratosInside = SwingsInside(block.Vibratos, note, speeds.VibratoCycleFrames, instrument.Id);
                var tremolosInside = SwingsInside(block.Tremolos, note, speeds.TremoloCycleFrames, instrument.Id);
                sounded.Add(new SoundedNote(
                    note, instrument, isBlockLastNote, isBlockLastNote && isBlockReleased, isBlockLastNote && isBlockHeldIntoLoop, walksInside, vibratosInside, tremolosInside,
                    block.Start, envelopeMoves));
            }
        }

        return sounded;
    }

    // The swings of one sweep that start inside a note, each at its instrument's speed where it was read close to it.
    private static ImmutableArray<SpcTraceSwing> SwingsInside(
        ImmutableArray<SpcTraceSwing> swings, SpcTraceNote note, IReadOnlyDictionary<int, double> cycleFramesByInstrument, int instrumentId)
    {
        return
        [
            .. swings
                .Where(swing => swing.Start >= note.Start && swing.Start < note.End)
                .Select(swing => SpcSwingSpeeds.Steadied(swing, cycleFramesByInstrument, instrumentId)),
        ];
    }

    // Where a block moved its envelope after its key-on and was still heard a poll later, a move the voice fell silent straight after being the note's
    // own release rather than a change to how it sounds.
    private static ImmutableArray<SpcTraceEnvelope> HeardEnvelopeMoves(SpcTraceBlock block)
    {
        return [.. block.Envelopes.Skip(1).Where(move => move.Frame + SpcTrace.PollFrames < block.End)];
    }

    // A sounded note as the note written on the roll, held off where the next one starts, or null where it has nowhere left to sound.
    private static WrittenNote? WrittenNoteFor(SoundedNote sounded, long nextStartFrame, SpcGrid grid)
    {
        var note = sounded.Note;
        int startTick = grid.TidiedTickAt(note.Start);
        int ceilingTick = grid.TidiedTickAt(nextStartFrame);
        int heardEndTick = Math.Min(Math.Max(grid.TidiedTickAt(note.End), startTick + 1), ceilingTick);
        if (heardEndTick <= startTick)
        {
            return null;
        }

        int lengthTicks = heardEndTick - startTick;
        int gate = SnesTiming.MaxGate;
        bool isUnfitted = false;
        if (sounded.IsReleased)
        {
            if (GatedLength(note, grid, startTick, ceilingTick) is { } gated)
            {
                (lengthTicks, gate) = gated;
            }
            else
            {
                isUnfitted = true;
            }
        }

        int noteValue = SnesPitch.NoteValueForRegister(note.Pitch, sounded.Instrument.TuningMultiplier, out bool isOutOfRange);
        var writtenNote = new Note(startTick, lengthTicks, noteValue - SnesPitch.C4NoteValue, sounded.Instrument.Id, Gate: gate, IsLegato: !sounded.IsLast, IsHeld: sounded.IsHeldIntoLoop)
        {
            Bends = BendsForWalks(sounded.Walks, grid, startTick, lengthTicks, sounded.Instrument, noteValue),
            Vibratos = VibratosFor(sounded.Vibratos, note.End, grid, startTick, lengthTicks),
            Tremolos = TremolosFor(sounded.Tremolos, note.End, grid, startTick, lengthTicks),
        };

        return new WrittenNote(writtenNote, isOutOfRange, isUnfitted);
    }

    // The length a released note is written at and the gate it plays under, where one of the gates explains the span it was heard for in whole steps of
    // the stretch it starts in, or null where none does.
    private static (int LengthTicks, int Gate)? GatedLength(SpcTraceNote note, SpcGrid grid, int startTick, int ceilingTick)
    {
        double soundingTicks = Math.Max(grid.ExactTicksAt(note.End - ReleaseOverhangFrames) - startTick, 0);
        int stepTicks = grid.StepTicksAt(note.Start);
        return SnesTiming.GatedLength(soundingTicks, stepTicks, ceilingTick - startTick, stepTicks * SpcGrid.TidyWithinSteps);
    }

    // The envelope rows a written note sounds under: the one it keys on under, then one from each move of its block, no move taking effect before the tick
    // after the key-on and a move the DSP plays as its instrument's own envelope written as that.
    private static ImmutableArray<EnvelopeRow> EnvelopeRowsFor(SoundedNote sounded, SpcGrid grid, Note note)
    {
        var instrumentEnvelope = sounded.Instrument.Envelope;
        var rows = ImmutableArray.CreateBuilder<EnvelopeRow>();
        rows.Add(new EnvelopeRow(OffsetRows.FirstOffsetTicks, instrumentEnvelope));
        int earliestMoveTick = grid.TidiedTickAt(sounded.KeyOnFrame) + 1;
        foreach (var move in sounded.EnvelopeMoves)
        {
            int moveTick = Math.Max(grid.TidiedTickAt(move.Frame), earliestMoveTick);
            if (moveTick >= note.EndTick())
            {
                break;
            }

            var writtenEnvelope = move.State.Audible() == instrumentEnvelope.Audible() ? instrumentEnvelope : move.State;
            int moveOffsetTicks = Math.Max(moveTick - note.StartTick, OffsetRows.FirstOffsetTicks);
            if (moveOffsetTicks == rows[^1].OffsetTicks)
            {
                rows[^1] = rows[^1] with { State = writtenEnvelope };
                if (rows.Count > 1 && rows[^2].State == writtenEnvelope)
                {
                    rows.RemoveAt(rows.Count - 1);
                }

                continue;
            }

            if (writtenEnvelope != rows[^1].State)
            {
                rows.Add(new EnvelopeRow(moveOffsetTicks, writtenEnvelope));
            }
        }

        return rows.ToImmutable();
    }

    // Says how many notes sit at a row they did not play at, keep a length off the grid, and carry a bend.
    private static void WarnAboutNotes(Action<string>? warn, int outOfRangeCount, int unfittedCount, int bendCount)
    {
        if (outOfRangeCount > 0)
        {
            warn?.Invoke(SpcTranscribeWarnings.PitchesOutOfRange(outOfRangeCount));
        }

        if (unfittedCount > 0)
        {
            warn?.Invoke(SpcTranscribeWarnings.NotesOffGrid(unfittedCount));
        }

        if (bendCount > 0)
        {
            warn?.Invoke(SpcTranscribeWarnings.SlidesAsStraightBends(bendCount));
        }
    }

    // A note's slides as its $DD chain, one straight ramp each, aimed at the row the pitch arrived at counted from the row the chain has reached, a walk
    // past the widest slide taken only where it was the value a vibrato swung about that moved.
    private static ImmutableArray<PitchBend> BendsForWalks(
        ImmutableArray<SpcTraceWalk> walks, SpcGrid grid, int startTick, int lengthTicks, Instrument instrument, int noteValue)
    {
        if (walks.IsEmpty)
        {
            return [];
        }

        var bends = ImmutableArray.CreateBuilder<PitchBend>();
        var chain = new NoteChain(grid, startTick, lengthTicks);
        int previousArmTick = 0;
        int reachedNoteValue = noteValue;
        foreach (var walk in walks)
        {
            if (walk.StepCount < MinWalkSteps)
            {
                continue;
            }

            int targetNoteValue = SnesPitch.NoteValueForRegister(walk.To, instrument.TuningMultiplier, out _);
            int offset = targetNoteValue - reachedNoteValue;
            if (offset == 0 || (!walk.IsUnderSwing && Math.Abs(offset) > MaxWalkSemitones))
            {
                continue;
            }

            int armTick = chain.StartTickAt(walk.Start);
            if (armTick >= chain.EndTick)
            {
                break;
            }

            if (bends.Count > 0 && (armTick - previousArmTick < MinBendsApartTicks || chain.EndTick - armTick < MinBendsApartTicks))
            {
                continue;
            }

            int rampTicks = PitchBend.LengthTicksRange.Clamp(grid.TidiedTickAt(walk.End) - armTick);
            bends.Add(new PitchBend(chain.Place(armTick, armTick + rampTicks), rampTicks, offset));
            previousArmTick = armTick;
            reachedNoteValue = targetNoteValue;
        }

        return bends.ToImmutable();
    }

    // A note's vibratos as its p chain, each at the extent and fade it was heard at.
    private static ImmutableArray<NoteVibrato> VibratosFor(ImmutableArray<SpcTraceSwing> vibratos, long heardEndFrame, SpcGrid grid, int startTick, int lengthTicks)
    {
        return SnesVibrato.Sanitized(SweepsFor(vibratos, heardEndFrame, grid, startTick, lengthTicks, (vibrato, span) =>
        {
            int fadeToFullExtentTicks = Math.Max(grid.TidiedTickAt(vibrato.FullWidthFrame) - span.StartTick, SnesVibrato.MinFadeTicks);
            return new NoteVibrato(span.DelayTicks, span.LengthTicks, span.Rate, SnesVibrato.ExtentReaching(vibrato.Peak), fadeToFullExtentTicks);
        }));
    }

    // A note's tremolos as its $E5 chain, each at the amount that ducks as deep as it was heard to.
    private static ImmutableArray<NoteTremolo> TremolosFor(ImmutableArray<SpcTraceSwing> tremolos, long heardEndFrame, SpcGrid grid, int startTick, int lengthTicks)
    {
        return SnesTremolo.Sanitized(SweepsFor(tremolos, heardEndFrame, grid, startTick, lengthTicks, (tremolo, span) =>
            new NoteTremolo(span.DelayTicks, span.LengthTicks, span.Rate, SnesTremolo.AmountDucking(SpcLevelSwings.DepthOf(tremolo)))));
    }

    // A note's swings of one sweep as its chain, each stretch running from where its sweep set off to where it stopped, or to the note's written end where
    // it swung until the note went quiet, at the rate whose cycle takes as many ticks as it was heard to, and made a stretch of by what the sweep adds.
    private static ImmutableArray<TStretch> SweepsFor<TStretch>(
        ImmutableArray<SpcTraceSwing> swings, long heardEndFrame, SpcGrid grid, int startTick, int lengthTicks, Func<SpcTraceSwing, SweepSpan, TStretch> stretchOf)
    {
        var stretches = ImmutableArray.CreateBuilder<TStretch>();
        var chain = new NoteChain(grid, startTick, lengthTicks);
        foreach (var swing in swings)
        {
            int readStartTick = chain.StartTickAt(swing.Start);
            bool isReadJustAfterNoteStart = chain.PreviousEndTick == startTick && readStartTick - startTick <= MaxSweepStartAfterNoteTicks;
            int sweepStartTick = isReadJustAfterNoteStart ? startTick : readStartTick;
            int sweepEndTick = swing.End >= heardEndFrame ? chain.EndTick : Math.Min(grid.TidiedTickAt(swing.End), chain.EndTick);
            if (sweepEndTick <= sweepStartTick)
            {
                continue;
            }

            double swingCycleTicks = grid.ExactTicksAt(swing.Start + swing.CycleFrames) - grid.ExactTicksAt(swing.Start);
            int rate = SnesSweep.RateForPeriodTicks(swingCycleTicks);
            stretches.Add(stretchOf(swing, new SweepSpan(chain.Place(sweepStartTick, sweepEndTick), sweepStartTick, sweepEndTick - sweepStartTick, rate)));
        }

        return stretches.ToImmutable();
    }

    // The rhythm's stretches on the tempo lane: the first as the song's own tempo and each after it as a divider where it starts, flat or fading.
    private static void WriteTempos(Song song, SpcGrid grid)
    {
        song.BaseTempo = grid.BaseTempo;
        int playingTempo = grid.BaseTempo;
        foreach (var stretch in grid.Stretches)
        {
            if (!stretch.DoesFade && stretch.StartTempo == playingTempo)
            {
                continue;
            }

            song.SetTempoChange(stretch.StartTick, stretch.StartTempo, stretch.DoesFade, stretch.EndTempo);
            playingTempo = stretch.EndTempo;
        }
    }

    // How loud each voice played and how far over written onto the channel volume and pan lanes, taken down by one factor where it played louder than the
    // driver's chain reaches, and the master volumes written onto the song volume lane lifted to give that factor back as far as the lane's top allows.
    private static void WriteLevels(Song song, SpcTrace trace, SpcGrid grid, Action<string>? warn, CancellationToken cancel)
    {
        double scale = LevelScale(trace);
        double playedShare = PlayedShare(trace, scale);
        WriteMasterVolumes(song, trace, grid, playedShare / scale);
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            cancel.ThrowIfCancellationRequested();
            WriteVoiceLevels(song, trace, grid, voice, scale);
        }

        if (playedShare < 1)
        {
            warn?.Invoke(SpcTranscribeWarnings.VoicesTurnedDown(playedShare));
        }
    }

    // One voice's levels in the order it played them, each inside a tremolo at the level the tremolo ducks from, read back as the fewest sets and fades on
    // the channel volume and pan lanes, the first stating the channel's base.
    private static void WriteVoiceLevels(Song song, SpcTrace trace, SpcGrid grid, int voice, double scale)
    {
        var volumePoints = new List<LanePoint<int>>();
        var panPoints = new List<LanePoint<SnesSurround>>();
        foreach (var block in trace.Blocks)
        {
            if (block.Voice != voice)
            {
                continue;
            }

            foreach (var level in SpcLevelSwings.TopLevels(block))
            {
                var heardVolume = HeardThroughMasterBalance(trace, level);
                var mix = SnesVolume.Decompose(Scaled(heardVolume, scale, scale), LevelsReadUnderMasterVolume, VelocityByte);
                int tick = grid.TidiedTickAt(level.Frame);
                volumePoints.Add(new LanePoint<int>(tick, [mix.ChannelVolume], 0));
                panPoints.Add(new LanePoint<SnesSurround>(tick, [mix.PanIndex], mix.Surround));
            }
        }

        LaneFadeReader.WriteBaseThenChanges(
            LaneFadeReader.Read(volumePoints, ChannelVolumeFadeTolerance, song.LoopTick, song.RollLengthTicks),
            change => song.SetBaseChannelVolume(voice, change.Values[0]),
            change => song.SetChannelVolumeChange(voice, change.Tick, change.Values[0], change.Fades, change.FadeTo[0]));
        LaneFadeReader.WriteBaseThenChanges(
            LaneFadeReader.Read(panPoints, PanFadeTolerance, song.LoopTick, song.RollLengthTicks),
            change =>
            {
                song.SetBasePan(voice, change.Values[0]);
                song.SetBaseSurround(voice, change.Key);
            },
            change => song.SetPanChange(voice, change.Tick, change.Values[0], change.Fades, change.FadeTo[0], change.Key));
    }

    // The share of the level it played at the song keeps once the voices are taken down by the level scale and the song volume lifts them back up,
    // which is whole wherever the lift the scale asks for keeps every master volume within the song volume's top.
    private static double PlayedShare(SpcTrace trace, double scale)
    {
        int loudestMasterVolume = trace.MasterVolumePath.Max(LouderMasterVolume);
        if (loudestMasterVolume == 0)
        {
            return 1;
        }

        return Math.Min(1, scale * SnesDspRegister.DefaultMasterVolume / loudestMasterVolume);
    }

    // The master volumes, lifted by a factor, read back as the fewest sets and fades on the song volume lane, the first stating the base.
    private static void WriteMasterVolumes(Song song, SpcTrace trace, SpcGrid grid, double lift)
    {
        var globalVolumePoints = trace.MasterVolumePath.Select(
            masterVolume => new LanePoint<int>(grid.TidiedTickAt(masterVolume.Frame), [GlobalVolumeFor(masterVolume, lift)], 0));
        LaneFadeReader.WriteBaseThenChanges(
            LaneFadeReader.Read(globalVolumePoints, MasterVolumeFadeTolerance, song.LoopTick, song.RollLengthTicks),
            change => song.BaseMasterVolume = change.Values[0],
            change => song.SetVolumeChange(change.Tick, change.Values[0], change.Fades, change.FadeTo[0]));
    }

    // The song volume whose squaring takes the voices to the louder master volume's share of the one the driver loads, lifted by a factor.
    private static int GlobalVolumeFor(SpcTraceMasterVolume masterVolume, double lift)
    {
        double liftedShare = lift * LouderMasterVolume(masterVolume) / SnesDspRegister.DefaultMasterVolume;
        return Math.Min((int)Math.Round(SnesVolume.MaxMasterVolume * Math.Sqrt(liftedShare)), SnesVolume.MaxMasterVolume);
    }

    // The size of the louder of the two master volumes.
    private static int LouderMasterVolume(SpcTraceMasterVolume masterVolume)
    {
        return Math.Max(Math.Abs(masterVolume.Left), Math.Abs(masterVolume.Right));
    }

    // A voice's VOL pair as it is heard through the master volumes in force where it was taken up: each side by its master volume's signed share of the louder one,
    // which is the balance the song volume cannot carry, and whole where both are silent.
    private static VoiceVolume HeardThroughMasterBalance(SpcTrace trace, SpcTraceLevel level)
    {
        var masterVolume = trace.MasterVolumeInForceAt(level.Frame);
        int louderMasterVolume = LouderMasterVolume(masterVolume);
        if (louderMasterVolume == 0)
        {
            return level.Volume;
        }

        return Scaled(level.Volume, (double)masterVolume.Left / louderMasterVolume, (double)masterVolume.Right / louderMasterVolume);
    }

    // The one factor the whole song's levels are taken down by so that its loudest moment is reachable at all.
    private static double LevelScale(SpcTrace trace)
    {
        double scale = 1;
        foreach (var block in trace.Blocks)
        {
            foreach (var level in block.Levels)
            {
                var heardVolume = HeardThroughMasterBalance(trace, level);
                var mix = SnesVolume.Decompose(heardVolume, LevelsReadUnderMasterVolume, VelocityByte);
                var loudest = SnesVolume.RegistersFor(LevelsReadUnderMasterVolume, SnesVolume.MaxChannelVolume, VelocityByte, mix.PanIndex);
                scale = Math.Min(scale, ReachableFraction(loudest.Left, heardVolume.Left));
                scale = Math.Min(scale, ReachableFraction(loudest.Right, heardVolume.Right));
            }
        }

        return scale;
    }

    // How far one side has to come down to be reachable, as a fraction of itself, a side the pan puts at nothing asking for nothing.
    private static double ReachableFraction(int reach, int played)
    {
        return reach <= 0 || Math.Abs(played) <= reach ? 1 : (double)reach / Math.Abs(played);
    }

    // One pair with each side taken by a factor of its own.
    private static VoiceVolume Scaled(VoiceVolume volume, double leftScale, double rightScale)
    {
        return new VoiceVolume((int)Math.Round(volume.Left * leftScale), (int)Math.Round(volume.Right * rightScale));
    }

    // The echo as the song changed it, where it runs and can be heard anywhere: a run wherever the echo writes under one delay, feedback and filter, the echo
    // volumes read back as the fewest sets and fades, and each channel's send where it moved.
    private static void WriteEcho(Song song, SpcTrace trace, SpcGrid grid)
    {
        if (!trace.EchoPath.Any(echo => echo.IsEnabled && (echo.VolumeLeft != 0 || echo.VolumeRight != 0)))
        {
            return;
        }

        var echoRuns = new EchoRunsBuilder(song);
        foreach (var echo in trace.EchoPath)
        {
            echoRuns.Reach(grid.TidiedTickAt(echo.Frame), echo.IsEnabled, echo.Delay, echo.Feedback, echo.Coefficients);
        }

        echoRuns.WriteToSong();
        WriteEchoVolumes(song, trace, grid);
        for (int voice = 0; voice < SnesDspRegister.VoiceCount; voice++)
        {
            WriteChannelEchoSends(song, trace, grid, voice);
        }
    }

    // The two echo volumes at every move of the echo or the master volumes, each side raised against its master volume, read back as the fewest sets and fades,
    // both sides on one divider, the first stating the bases.
    private static void WriteEchoVolumes(Song song, SpcTrace trace, SpcGrid grid)
    {
        var echoVolumePoints = EchoOrMasterVolumeMoves(trace).Select(frame =>
        {
            var echo = trace.EchoInForceAt(frame);
            var masterVolume = trace.MasterVolumeInForceAt(frame);
            return new LanePoint<int>(
                grid.TidiedTickAt(frame), [RaisedEchoVolume(echo.VolumeLeft, masterVolume.Left), RaisedEchoVolume(echo.VolumeRight, masterVolume.Right)], 0);
        });
        LaneFadeReader.WriteBaseThenChanges(
            LaneFadeReader.Read(echoVolumePoints, EchoVolumeFadeTolerance, song.LoopTick, song.RollLengthTicks),
            change =>
            {
                song.SetBaseEchoVolume(EchoSetting.VolumeLeft, change.Values[0]);
                song.SetBaseEchoVolume(EchoSetting.VolumeRight, change.Values[1]);
            },
            change => song.SetEchoVolumeChange(change.Tick, change.Values[0], change.Values[1], change.Fades, change.FadeTo[0], change.FadeTo[1]));
    }

    // Every frame the echo or the master volumes moved at, in order.
    private static IEnumerable<long> EchoOrMasterVolumeMoves(SpcTrace trace)
    {
        return trace.EchoPath.Select(echo => echo.Frame).Union(trace.MasterVolumePath.Select(masterVolume => masterVolume.Frame)).Order();
    }

    // An echo volume byte raised by as far as the master volume on its side is below the one the driver loads, held to the signed register.
    private static int RaisedEchoVolume(int echoVolumeByte, int masterVolume)
    {
        int echoVolume = SnesEcho.SignedValueOf(echoVolumeByte);
        if (echoVolume == 0)
        {
            return echoVolumeByte;
        }

        double raisedEchoVolume = (double)echoVolume * SnesDspRegister.DefaultMasterVolume / masterVolume;
        return (byte)(sbyte)Math.Clamp(Math.Round(raisedEchoVolume), sbyte.MinValue, sbyte.MaxValue);
    }

    // One channel's echo send, the first point stating its base and a change wherever its bit moved after.
    private static void WriteChannelEchoSends(Song song, SpcTrace trace, SpcGrid grid, int voice)
    {
        var echoBitPoints = trace.EchoPath.Select(echo => new LanePoint<int>(
            grid.TidiedTickAt(echo.Frame), [SnesDspRegister.HasVoiceBit(echo.Sends, voice) ? Song.ChannelEchoOn : Song.ChannelEchoOff], 0));
        LaneFadeReader.WriteBaseThenChanges(
            LaneFadeReader.Moves(echoBitPoints),
            move => song.SetBaseChannelEchoOn(voice, move.Values[0] == Song.ChannelEchoOn),
            move => song.SetChannelEchoChange(voice, move.Tick, move.Values[0]));
    }

    // The tuning multiplier each sample's instruments are built at: its measurement, or for an unpitched sample the tuning that plays its most-played register on C4, moved by whole octaves where that
    // brings the notes played on it onto the roll, and then by less than a semitone so the registers it was played at land on rows.
    private static Dictionary<int, double> TuningsBySrcn(SpcTrace trace, IReadOnlyDictionary<int, Sample> samplesBySrcn)
    {
        var registersBySrcn = new Dictionary<int, List<int>>();
        foreach (var block in trace.Blocks)
        {
            if (!samplesBySrcn.ContainsKey(block.Srcn))
            {
                continue;
            }

            if (!registersBySrcn.TryGetValue(block.Srcn, out var played))
            {
                registersBySrcn[block.Srcn] = played = [];
            }

            foreach (var note in block.Notes)
            {
                played.Add(note.Pitch);
            }
        }

        var tunings = new Dictionary<int, double>();
        foreach (var (srcn, sample) in samplesBySrcn)
        {
            if (!registersBySrcn.TryGetValue(srcn, out var played))
            {
                tunings[srcn] = ShiftedByOctaves(sample.Pitch.TuningMultiplier, 0);
                continue;
            }

            double startingTuning = sample.Pitch.IsPitched ? sample.Pitch.TuningMultiplier : TuningPlayingOnC4(MostPlayedRegister(played));
            tunings[srcn] = AlignedToRows(played, OctaveOntoTheRoll(played, startingTuning, ShiftedByOctaves(startingTuning, 0)));
        }

        return tunings;
    }

    // The tuning multiplier at which C4 plays a PITCH register.
    private static double TuningPlayingOnC4(int register)
    {
        return SnesPitch.TuningForRate(SnesPitch.C4NoteValue, SnesPitch.RateOf(register));
    }

    // The register a sample was played at most often, the lower of any tied.
    private static int MostPlayedRegister(List<int> registers)
    {
        return CountByRegister(registers).OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).First().Key;
    }

    // How many times each register a sample was played at was played.
    private static Dictionary<int, int> CountByRegister(List<int> registers)
    {
        var countByRegister = new Dictionary<int, int>();
        foreach (int register in registers)
        {
            countByRegister[register] = countByRegister.GetValueOrDefault(register) + 1;
        }

        return countByRegister;
    }

    // The tuning moved by the fewest whole octaves that leaves the fewest registers off the roll, nearest first and both ways at each distance.
    private static double OctaveOntoTheRoll(List<int> registers, double measuredTuning, double snappedTuning)
    {
        double chosen = snappedTuning;
        int fewestOff = CountOffTheRoll(registers, snappedTuning);
        for (int octaves = 1; fewestOff > 0 && octaves <= MaxTuningShiftOctaves; octaves++)
        {
            foreach (int direction in new[] { 1, -1 })
            {
                double candidate = ShiftedByOctaves(measuredTuning, direction * octaves);
                if (candidate <= 0)
                {
                    continue;
                }

                int off = CountOffTheRoll(registers, candidate);
                if (off >= fewestOff)
                {
                    continue;
                }

                fewestOff = off;
                chosen = candidate;
            }
        }

        return chosen;
    }

    // A tuning moved by a fraction of a semitone either way, to the 8.8 value at which the registers a sample was played at land nearest the rows their notes
    // are placed on, never taking a note off the roll.
    private static double AlignedToRows(List<int> registers, double tuning)
    {
        var countByRegister = CountByRegister(registers);

        // How many notes a candidate tuning takes off the roll, and how far the rest sit from their rows, weighted by how often each was played.
        (int OffCount, double Detuning) OffCountAndDetuning(double candidate)
        {
            int offCount = 0;
            double detuning = 0;
            foreach (var (register, count) in countByRegister)
            {
                int noteValue = SnesPitch.NoteValueForRegister(register, candidate, out bool isOutOfRange);
                if (isOutOfRange)
                {
                    offCount += count;
                    continue;
                }

                double row = SnesPitch.PitchRegister(noteValue, candidate);
                detuning += count * Math.Abs(Math.Log(register / row));
            }

            return (offCount, detuning);
        }

        double tuningSteps = Instrument.TuningLowStepsPerUnit;
        int tuningInSteps = (int)Math.Round(tuning * tuningSteps);
        int lowestStep = Math.Max((int)Math.Ceiling(tuning * SnesPitch.FrequencyRatioForSemitones(-MaxAlignSemitones) * tuningSteps), 1);
        int highestStep = Math.Min((int)Math.Floor(tuning * SnesPitch.FrequencyRatioForSemitones(MaxAlignSemitones) * tuningSteps), (int)(Instrument.MaxTuningMultiplier * tuningSteps));
        var (fewestOff, bestDetuning) = OffCountAndDetuning(tuning);
        double aligned = tuning;
        for (int distance = 1; tuningInSteps - distance >= lowestStep || tuningInSteps + distance <= highestStep; distance++)
        {
            foreach (int raised in new[] { tuningInSteps - distance, tuningInSteps + distance })
            {
                if (raised < lowestStep || raised > highestStep)
                {
                    continue;
                }

                double candidate = raised / tuningSteps;
                var (offCount, detuning) = OffCountAndDetuning(candidate);
                if (offCount > fewestOff || detuning >= bestDetuning)
                {
                    continue;
                }

                bestDetuning = detuning;
                aligned = candidate;
            }
        }

        return aligned;
    }

    // A tuning raised by a number of octaves and snapped to the 8.8 pair an instrument holds it as, or zero where the pair cannot hold it at all.
    private static double ShiftedByOctaves(double tuning, int octaves)
    {
        double tuningSteps = Instrument.TuningLowStepsPerUnit;
        double raisedTuningSteps = Math.Round(tuning * Math.Pow(2, octaves) * tuningSteps);
        return raisedTuningSteps < 1 || raisedTuningSteps > Instrument.MaxTuningMultiplier * tuningSteps ? 0 : raisedTuningSteps / tuningSteps;
    }

    // How many of the registers a sample was played at ask for a note the roll has no row for at a tuning.
    private static int CountOffTheRoll(List<int> registers, double tuning)
    {
        int offCount = 0;
        foreach (int register in registers)
        {
            SnesPitch.NoteValueForRegister(register, tuning, out bool isOutOfRange);
            if (isOutOfRange)
            {
                offCount++;
            }
        }

        return offCount;
    }

    // The first block the trace heard under each distinct instrument key, in the order it heard them.
    private static List<SpcTraceBlock> FirstBlockOfEachInstrument(SpcTrace trace)
    {
        var seen = new HashSet<(int, EnvelopeState)>();
        return [.. trace.Blocks.Where(block => seen.Add(InstrumentKeyOf(block)))];
    }

    // The instrument a block plays: its sample and the envelope it keyed on with, as the DSP plays that envelope.
    private static (int Srcn, EnvelopeState AudibleEnvelope) InstrumentKeyOf(SpcTraceBlock block)
    {
        return (block.Srcn, block.KeyOnEnvelope.Audible());
    }

    // One note a voice sounded: its instrument, whether it is its block's last, whether the voice was heard to fall silent after it, whether it sounds on into
    // the loop, its slides, vibratos and tremolos, and the frame its block keyed on at and the envelope moves the block was heard through.
    private readonly record struct SoundedNote(
        SpcTraceNote Note, Instrument Instrument, bool IsLast, bool IsReleased, bool IsHeldIntoLoop, ImmutableArray<SpcTraceWalk> Walks,
        ImmutableArray<SpcTraceSwing> Vibratos, ImmutableArray<SpcTraceSwing> Tremolos, long KeyOnFrame, ImmutableArray<SpcTraceEnvelope> EnvelopeMoves);

    // A sounded note as written on the roll, with whether its pitch had no row and whether no gate explained how long it was heard.
    private readonly record struct WrittenNote(Note Note, bool IsOutOfRange, bool IsUnfitted);

    // The speed each instrument's vibratos and tremolos go at, keyed by the instrument's id.
    private readonly record struct SweepSpeeds(IReadOnlyDictionary<int, double> VibratoCycleFrames, IReadOnlyDictionary<int, double> TremoloCycleFrames);

    // Where one stretch of a sweep goes in its note's chain: how long it waits after the one in front of it, the tick it starts at, how long it lasts, and the rate its cycle is heard at.
    private readonly record struct SweepSpan(int DelayTicks, int StartTick, int LengthTicks, int Rate);

    // A note's chain of stretches as it is laid down nose to tail: where the note ends, and where the stretch in front of the next one ended.
    private sealed class NoteChain(SpcGrid grid, int startTick, int lengthTicks)
    {
        // The tick the note ends at, which no stretch reaches past.
        public int EndTick { get; } = startTick + lengthTicks;

        // Where the stretch laid down last ended, the note's own start before any is.
        public int PreviousEndTick { get; private set; } = startTick;

        // The tick a stretch heard to start at a frame goes at, no earlier than where the one in front of it ended.
        public int StartTickAt(long frame)
        {
            return Math.Max(grid.TidiedTickAt(frame), PreviousEndTick);
        }

        // Lays a stretch down from one tick to another, answering how long it waits after the one in front of it.
        public int Place(int stretchStartTick, int stretchEndTick)
        {
            int delayTicks = stretchStartTick - PreviousEndTick;
            PreviousEndTick = stretchEndTick;
            return delayTicks;
        }
    }
}
