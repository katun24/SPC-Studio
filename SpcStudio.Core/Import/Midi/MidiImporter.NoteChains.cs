using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Import;

// The importer's chains and lanes: a note's loudness as its own volume rows, its bends and its vibrato, and each channel's pan, every one read back as
// the fewest sets and fades that play within a tolerance of what the file played.
public static partial class MidiImporter
{
    private const int VolumeFadeTolerance = 1;
    private const int BendStepsPerSemitone = 4;
    private const int BendFadeTolerance = BendStepsPerSemitone / 2;
    private const int UnbentStep = 128;
    private const int VibratoFadeTolerance = 4;
    private const int PanFadeTolerance = 1;
    private const double VibratoCyclesPerSecond = 5.5;
    private const long EndlessMidiTick = long.MaxValue;

    // A note's velocity times its channel's loudness, as its own volume rows.
    private static ImmutableArray<NoteVolumeRegion> VolumeRowsFor(NoteSpan span, MidiCurve loudness)
    {
        double velocityShare = (double)span.Played.Velocity / MidiPerformance.MaxValue;
        var points = span.PointsOf(loudness, share => (int)Math.Round(SnesVolume.MaxChannelVolume * velocityShare * share));
        return [.. LaneFadeReader.Read(points, VolumeFadeTolerance, NoLoopTick, span.LengthTicks).Select(change => new NoteVolumeRegion(change.Tick, change.Values[0], FadeToOf(change)))];
    }

    // A note's bend as its $DD chain counted from the row it was struck on, read in steps of a semitone's fraction about the middle of a byte to within
    // a tolerance of the bend played, each set a ramp of the shortest length and each fade a ramp across itself, every one landing on the whole semitone
    // nearest where it was read to.
    private static ImmutableArray<PitchBend> BendsFor(NoteSpan span, MidiCurve bend, int struckBendSemitones)
    {
        var points = span.PointsOf(
            bend, semitones => Math.Clamp(UnbentStep + (int)Math.Round((semitones - struckBendSemitones) * BendStepsPerSemitone), byte.MinValue, byte.MaxValue));
        var changes = LaneFadeReader.Read(points, BendFadeTolerance, NoLoopTick, span.LengthTicks);
        var bends = ImmutableArray.CreateBuilder<PitchBend>();
        int reachedSemitones = 0;
        int previousEndTick = 0;
        for (int changeIndex = 1; changeIndex < changes.Count; changeIndex++)
        {
            var change = changes[changeIndex];
            int targetSemitones = (int)Math.Round((double)((change.Fades ? change.FadeTo[0] : change.Values[0]) - UnbentStep) / BendStepsPerSemitone);
            if (targetSemitones == reachedSemitones)
            {
                continue;
            }

            int rampTicks = change.Fades ? Math.Min(EndTickOf(changes, changeIndex, span.LengthTicks) - change.Tick, PitchBend.MaxLengthTicks) : PitchBend.MinLengthTicks;
            bends.Add(new PitchBend(change.Tick - previousEndTick, rampTicks, targetSemitones - reachedSemitones));
            reachedSemitones = targetSemitones;
            previousEndTick = change.Tick + rampTicks;
        }

        return bends.ToImmutable();
    }

    // A note's vibrato depth as its p chain at the speed that cycles the same number of times a second at every tempo: a stretch at each depth it holds,
    // one rising from nothing widening across itself, and one moving between two depths played at the depth it reaches from where it starts.
    private static ImmutableArray<NoteVibrato> VibratosFor(NoteSpan span, MidiCurve vibrato, int tempo)
    {
        var extentBySemitones = new Dictionary<double, int>();

        // The extent that sweeps a depth either side of the note, none for no depth.
        int ExtentOf(double semitones)
        {
            if (semitones <= 0)
            {
                return SnesVibrato.MinExtent;
            }

            if (!extentBySemitones.TryGetValue(semitones, out int extent))
            {
                extentBySemitones[semitones] = extent = SnesVibrato.ExtentReaching(semitones);
            }

            return extent;
        }

        var changes = LaneFadeReader.Read(span.PointsOf(vibrato, ExtentOf), VibratoFadeTolerance, NoLoopTick, span.LengthTicks);
        var stretches = new List<(int StartTick, int EndTick, int Extent, int FadeTicks)>();
        for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
        {
            var change = changes[changeIndex];
            int extent = change.Fades ? change.FadeTo[0] : change.Values[0];
            if (extent == SnesVibrato.MinExtent)
            {
                continue;
            }

            int endTick = EndTickOf(changes, changeIndex, span.LengthTicks);
            bool doesWidenFromNothing = change.Fades && change.Values[0] == SnesVibrato.MinExtent;
            int fadeTicks = doesWidenFromNothing ? endTick - change.Tick : SnesVibrato.MinFadeTicks;
            bool doesCarryOnStretchInFront = stretches.Count > 0 && stretches[^1].EndTick == change.Tick && stretches[^1].Extent == extent && !doesWidenFromNothing;
            if (doesCarryOnStretchInFront)
            {
                stretches[^1] = stretches[^1] with { EndTick = endTick };
                continue;
            }

            stretches.Add((change.Tick, endTick, extent, fadeTicks));
        }

        int rate = SnesSweep.RateForPeriodTicks(SnesTiming.TicksPerSecond(tempo) / VibratoCyclesPerSecond);
        var vibratos = ImmutableArray.CreateBuilder<NoteVibrato>();
        int previousEndTick = 0;
        foreach (var stretch in stretches)
        {
            vibratos.Add(new NoteVibrato(stretch.StartTick - previousEndTick, stretch.EndTick - stretch.StartTick, rate, stretch.Extent, stretch.FadeTicks));
            previousEndTick = stretch.EndTick;
        }

        return SnesVibrato.Sanitized(vibratos.ToImmutable());
    }

    // Each of the song's channels' pan lane from the pan of the MIDI channel it plays, each MIDI channel's pan taking over where its first note there starts.
    private static void WritePans(Song song, MidiPerformance performance, MidiTimeBase timeBase, IReadOnlyList<IReadOnlyList<(long MidiTick, int MidiChannel)>> ownersOf)
    {
        for (int channel = 0; channel < ownersOf.Count; channel++)
        {
            int songChannel = channel;
            var owners = ownersOf[channel];
            var points = new List<LanePoint<SnesSurround>>();
            for (int ownerIndex = 0; ownerIndex < owners.Count; ownerIndex++)
            {
                long fromTick = ownerIndex == 0 ? 0 : owners[ownerIndex].MidiTick;
                long untilTick = ownerIndex + 1 < owners.Count ? owners[ownerIndex + 1].MidiTick : EndlessMidiTick;
                points.AddRange(performance.PanOf(owners[ownerIndex].MidiChannel).MovesFrom(fromTick, untilTick)
                    .Select(move => new LanePoint<SnesSurround>(timeBase.SongTickAt(move.Tick), [PanIndexOf(move.Value)], SnesSurround.None)));
            }

            LaneFadeReader.WriteBaseThenChanges(
                LaneFadeReader.Read(points, PanFadeTolerance, NoLoopTick, song.RollLengthTicks),
                change => song.SetBasePan(songChannel, change.Values[0]),
                change => song.SetPanChange(songChannel, change.Tick, change.Values[0], change.Fades, change.FadeTo[0], change.Key));
        }
    }

    // The pan table index nearest a MIDI pan, MIDI's hard left being the table's top.
    private static int PanIndexOf(double pan)
    {
        return (int)Math.Round((MidiPerformance.MaxValue - pan) * SnesVolume.MaxPanIndex / MidiPerformance.MaxValue);
    }

    // The tick a change holds or ramps up to: the next change's, or the note's end after the last.
    private static int EndTickOf<TKey>(List<WrittenLaneChange<TKey>> changes, int changeIndex, int lengthTicks)
    {
        return changeIndex + 1 < changes.Count ? changes[changeIndex + 1].Tick : lengthTicks;
    }

    // The value a change ramps to, or null where it sets one.
    private static int? FadeToOf<TKey>(WrittenLaneChange<TKey> change)
    {
        return change.Fades ? change.FadeTo[0] : null;
    }

    // One note's stretch of the file: the note as played, and where it starts and how long it lasts on the song's ticks.
    private readonly record struct NoteSpan(MidiPlayedNote Played, int StartTick, int LengthTicks, MidiTimeBase TimeBase)
    {
        // A curve's value where the note starts and at each move inside it, as points counted from the note's start.
        public List<LanePoint<int>> PointsOf(MidiCurve curve, Func<double, int> valueOf)
        {
            var points = new List<LanePoint<int>>();
            foreach (var (tick, value) in curve.MovesFrom(Played.StartTick, Played.EndTick))
            {
                int offsetTicks = Math.Clamp(TimeBase.SongTickAt(tick) - StartTick, OffsetRows.FirstOffsetTicks, LengthTicks - 1);
                points.Add(new LanePoint<int>(offsetTicks, [valueOf(value)], 0));
            }

            return points;
        }
    }
}
