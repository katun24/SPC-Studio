using SpcStudio.Model;

namespace SpcStudio.Export;

// Shortens the song's loop for the export where the driver plays the same either way: a loop that plays one stretch several times over cut to one
// repeat, and the loop marker and the end moved earlier together where the intro ends as the loop does.
public static class SongLoopShortening
{
    // The song the export writes under the options: the song itself, or a copy of it with its markers moved where the loop shortens.
    public static Song Shortened(Song song, MmlExportOptions options)
    {
        if (!song.IsLooping || (!options.ShortenSongLoopToOneRepeat && !options.FuseSongLoopEndWithIntro))
        {
            return song;
        }

        var isCleanCut = SongStretches.CleanCuts(song);
        if (!isCleanCut[song.LoopTick] || !isCleanCut[song.EndTick])
        {
            return song;
        }

        int endTick = options.ShortenSongLoopToOneRepeat ? OneRepeatEndTick(song, isCleanCut) : song.EndTick;
        int fusedTicks = options.FuseSongLoopEndWithIntro ? FusedTicks(song, endTick, isCleanCut) : 0;
        if (endTick == song.EndTick && fusedTicks == 0)
        {
            return song;
        }

        var shortened = song.Copy();
        shortened.SetMarkers(endTick - fusedTicks, song.LoopTick - fusedTicks);
        return shortened;
    }

    // Where the loop ends cut to its shortest repeat that it plays over and over whole, or where it ends now where it plays no stretch twice.
    private static int OneRepeatEndTick(Song song, bool[] isCleanCut)
    {
        int loopTick = song.LoopTick;
        int loopTicks = song.EndTick - loopTick;

        for (int repeatTicks = 1; repeatTicks <= loopTicks / 2; repeatTicks++)
        {
            bool isWholeRepeat = loopTicks % repeatTicks == 0 && isCleanCut[loopTick + repeatTicks] && isCleanCut[song.EndTick - repeatTicks];
            if (isWholeRepeat && SongStretches.IsSamePlayed(song, loopTick, loopTick + repeatTicks, loopTicks - repeatTicks))
            {
                return loopTick + repeatTicks;
            }
        }

        return song.EndTick;
    }

    // How far the loop marker and the end move earlier together: the most ticks the intro ends as the loop up to an end does, both cut cleanly there.
    private static int FusedTicks(Song song, int endTick, bool[] isCleanCut)
    {
        int loopTick = song.LoopTick;
        var cleanFusedTicks = Enumerable.Range(1, loopTick).Where(fusedTicks => isCleanCut[loopTick - fusedTicks] && isCleanCut[endTick - fusedTicks]).ToList();

        // How many of the moves, shortest first, play the same.
        int samePlayedCount = SortedSearch.CountLeading(cleanFusedTicks, fusedTicks => SongStretches.IsSamePlayed(song, loopTick - fusedTicks, endTick - fusedTicks, fusedTicks));
        return samePlayedCount == 0 ? 0 : cleanFusedTicks[samePlayedCount - 1];
    }
}
