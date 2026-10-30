using SpcStudio.Model;

namespace SpcStudio.Export;

// Converts between tick counts and the MML duration suffixes AddmusicK reads.
public static class MmlDurationEncoding
{
    public const int MaxTicksWithoutAutoTies = 0x7F;
    public const int TicksPerAutoTiedPiece = 0x60;
    private const int MaxDotsInDurationSuffix = 2;

    private static readonly int[] NoteNumbersDividingWholeNoteExactly = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 192];

    // Splits a run of ticks into the tick lengths of the note and tie bytes AddmusicK writes for it.
    public static IReadOnlyList<int> SplitRunIntoTiedPieces(int runTicks)
    {
        if (runTicks <= MaxTicksWithoutAutoTies)
        {
            return [runTicks];
        }

        int lastPieceTicks = LastTiedPieceTicks(runTicks);
        int wholePieceCountBeforeLastPiece = (runTicks - lastPieceTicks) / TicksPerAutoTiedPiece;
        var pieceTicks = new int[wholePieceCountBeforeLastPiece + 1];
        for (int pieceIndex = 0; pieceIndex < wholePieceCountBeforeLastPiece; pieceIndex++)
        {
            pieceTicks[pieceIndex] = TicksPerAutoTiedPiece;
        }

        pieceTicks[wholePieceCountBeforeLastPiece] = lastPieceTicks;

        return pieceTicks;
    }

    // The tick length of the last note or tie byte AddmusicK writes for a run of ticks, which is the whole run where it fits in one.
    public static int LastTiedPieceTicks(int runTicks)
    {
        if (runTicks <= MaxTicksWithoutAutoTies)
        {
            return runTicks;
        }

        return (runTicks - TicksPerAutoTiedPiece - 1) % TicksPerAutoTiedPiece + 1;
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
