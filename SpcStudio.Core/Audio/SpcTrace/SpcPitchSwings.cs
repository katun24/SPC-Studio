using System.Collections.Immutable;
using SpcStudio.Model;

namespace SpcStudio.Audio;

// A block's PITCH values read for its vibratos: the swings in semitones, rising from the note first, and the note a value inside one stands for.
public static class SpcPitchSwings
{
    private const double MaxSwingSemitones = 2;
    private const double MaxCenterDriftSemitones = 0.35;
    private const double MinWidthToleranceSemitones = 0.06;
    private const double FirstTurnCycles = 0.25;
    private const int KeyOnSidePeaks = 0;

    // The vibrato's scale: semitones, a jump being a move to another note.
    public static readonly SpcSwingScale Scale = new(
        SemitonesOf, RegisterOf, SpcPitchPath.AreDifferentNotes, MaxSwingSemitones, MaxCenterDriftSemitones, MinWidthToleranceSemitones, FirstTurnCycles,
        IsFirstTurnPeak: true, KeyOnSidePeaks);

    // The vibrato as a block is read for it: off the pitch path, a block given one having its notes read again with it.
    public static readonly SpcSwingKind VibratoKind = new(
        Scale, block => block.Path, block => block.Vibratos, (block, vibrato) => block with { Vibratos = [vibrato], Notes = SpcPitchPath.NotesOf(block.Path, block.End, [vibrato]) });

    // A block's pitch path as the stretches it swung evenly about one value at one width, in the order it played them.
    public static ImmutableArray<SpcTraceSwing> VibratosOf(IReadOnlyList<SpcTracePoint> path, long end)
    {
        return SpcSwings.SwingsOf(path, end, Scale);
    }

    // The pitch a value at a frame stands for: the one the pitch swung about where it lies inside a stretch that swung, and its own otherwise.
    public static int PitchAt(ImmutableArray<SpcTraceSwing> vibratos, long frame, int pitch)
    {
        return SpcSwings.SwingAt(vibratos, frame)?.Center ?? pitch;
    }

    // The PITCH register a value in semitones above the register's own unit stands for.
    internal static int RegisterOf(double semitones)
    {
        return (int)Math.Round(SnesPitch.FrequencyRatioForSemitones(semitones));
    }

    // A PITCH register in semitones above the register's own unit.
    internal static double SemitonesOf(int pitch)
    {
        return SnesPitch.SemitonesForFrequencyRatio(Math.Max(pitch, 1));
    }
}
