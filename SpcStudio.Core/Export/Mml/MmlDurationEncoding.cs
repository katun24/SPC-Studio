using SpcStudio.Model;

namespace SpcStudio.Export;

// Converts between tick counts and the MML duration suffixes AddmusicK reads.
public static class MmlDurationEncoding
{
    public const int MaxTicksWithoutAutoTies = FirstAutoTiedRunTicks - 1;
    public const int TicksPerAutoTiedPiece = 0x60;
    public const int NoLengthDivider = 1;
    internal const int NoDurationTicks = -1;
    private const int FirstAutoTiedRunTicks = 0x80;
    private const int MaxDotsInDurationSuffix = 2;
    private const int SplitRunDurationByteCount = 2;

    private static readonly int[] NoteNumbersDividingWholeNoteExactly = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 192];

    // Splits a run of ticks into the tick lengths of the note and tie bytes AddmusicK writes for it, under the divider its lengths were divided by.
    public static IReadOnlyList<int> SplitRunIntoTiedPieces(int runTicks, int lengthDivider = NoLengthDivider)
    {
        if (runTicks <= MaxTicksWithoutAutoTiesUnder(lengthDivider))
        {
            return [runTicks];
        }

        int wholePieceTicks = TicksPerAutoTiedPieceUnder(lengthDivider);
        int lastPieceTicks = LastTiedPieceTicks(runTicks, lengthDivider);
        int wholePieceCountBeforeLastPiece = (runTicks - lastPieceTicks) / wholePieceTicks;
        var pieceTicks = new int[wholePieceCountBeforeLastPiece + 1];
        for (int pieceIndex = 0; pieceIndex < wholePieceCountBeforeLastPiece; pieceIndex++)
        {
            pieceTicks[pieceIndex] = wholePieceTicks;
        }

        pieceTicks[wholePieceCountBeforeLastPiece] = lastPieceTicks;

        return pieceTicks;
    }

    // An estimate of a run's bytes inline: the note, rest or tie byte, the duration byte where the length is not the one in force, and for a run too long for one byte the split AddmusicK makes.
    internal static int EstimatedRunBytes(int runTicks, ref int durationTicksInForce)
    {
        var pieceTicks = SplitRunIntoTiedPieces(runTicks);
        int durationBytes = pieceTicks.Count > 1 ? SplitRunDurationByteCount * SongDataSize.DurationByteBytes : runTicks == durationTicksInForce ? 0 : SongDataSize.DurationByteBytes;

        durationTicksInForce = pieceTicks[^1];
        return pieceTicks.Count * SongDataSize.NoteByteBytes + durationBytes;
    }

    // An estimate of runs laid end to end inline, each starting on a tick and the last ending on the end tick, the first stating its duration.
    internal static int EstimatedRunsBytes(IReadOnlyList<int> runStartTicks, int endTick)
    {
        int durationTicksInForce = NoDurationTicks;
        int bytes = 0;
        for (int runIndex = 0; runIndex < runStartTicks.Count; runIndex++)
        {
            int runEndTick = runIndex + 1 < runStartTicks.Count ? runStartTicks[runIndex + 1] : endTick;
            bytes += EstimatedRunBytes(runEndTick - runStartTicks[runIndex], ref durationTicksInForce);
        }

        return bytes;
    }

    // The tick length of the last note or tie byte AddmusicK writes for a run of ticks under the divider its lengths were divided by, which is the whole
    // run where it fits in one.
    public static int LastTiedPieceTicks(int runTicks, int lengthDivider = NoLengthDivider)
    {
        if (runTicks <= MaxTicksWithoutAutoTiesUnder(lengthDivider))
        {
            return runTicks;
        }

        int wholePieceTicks = TicksPerAutoTiedPieceUnder(lengthDivider);
        return (runTicks - wholePieceTicks - 1) % wholePieceTicks + 1;
    }

    // Whether a divider divides both the run length AddmusicK starts splitting at and the piece length it splits into without a remainder.
    public static bool CanSplitRunsUnder(int lengthDivider)
    {
        return FirstAutoTiedRunTicks % lengthDivider == 0 && TicksPerAutoTiedPiece % lengthDivider == 0;
    }

    // The longest run AddmusicK writes as one byte once its lengths are divided by the divider.
    private static int MaxTicksWithoutAutoTiesUnder(int lengthDivider)
    {
        return FirstAutoTiedRunTicks / lengthDivider - 1;
    }

    // The piece length AddmusicK splits a longer run into once its lengths are divided by the divider.
    private static int TicksPerAutoTiedPieceUnder(int lengthDivider)
    {
        return TicksPerAutoTiedPiece / lengthDivider;
    }

    // Writes a tick count as a plain length if one fits, else a dotted one, else a raw tick count.
    public static string DurationSuffixForTicks(int ticks)
    {
        for (int dotCount = 0; dotCount <= MaxDotsInDurationSuffix; dotCount++)
        {
            foreach (int noteNumber in NoteNumbersDividingWholeNoteExactly)
            {
                if (TicksForNoteNumber(noteNumber, dotCount) == ticks)
                {
                    return noteNumber + new string('.', dotCount);
                }
            }
        }

        return $"={ticks}";
    }

    // The ticks AddmusicK's parser makes of a note number with that many dots.
    public static int TicksForNoteNumber(int noteNumber, int dotCount)
    {
        return ApplyDotsToTicks(SnesTiming.TicksPerWholeNote / noteNumber, dotCount);
    }

    // Adds that many dots to a tick count, each dot adding half of what the one before it added, rounded down.
    public static int ApplyDotsToTicks(int ticks, int dotCount)
    {
        int ticksAddedByLastDot = ticks;
        for (int dotIndex = 0; dotIndex < dotCount; dotIndex++)
        {
            ticksAddedByLastDot /= 2;
            ticks += ticksAddedByLastDot;
        }

        return ticks;
    }
}
