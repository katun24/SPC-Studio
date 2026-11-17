namespace SpcStudio.Import;

// Whether a song's rests skip the key-off they make when they are read, which a hot patch decides for the whole driver.
internal static class RestKeyOffPatch
{
    // The hot patch the song's rests skip their key-off under, or null where they make it, refused where a patch after the song's start switches it the
    // other way.
    public static MmlIgnored? SkippingPatch(MmlFile file)
    {
        var channelHotPatches = SongHotPatches.InChannels(file);
        var startPatch = channelHotPatches.LastOrDefault(patch => patch.Tick == 0);
        bool doesStartSkipping = startPatch?.DoesRestSkipKeyOff ?? false;
        var laterPatches = channelHotPatches.Where(patch => patch.Tick > 0).Concat(SongHotPatches.InRemoteBlocks(file));
        if (laterPatches.FirstOrDefault(patch => patch.DoesRestSkipKeyOff != doesStartSkipping) is { } changingPatch)
        {
            throw startPatch is { IsAddMusicFourPreset: true }
                ? MmlImportException.RestKeyOffChangedInAddMusicFourSong(changingPatch.Line)
                : MmlImportException.RestKeyOffChangedByHotPatch(changingPatch.Line);
        }

        return doesStartSkipping ? startPatch : null;
    }
}
