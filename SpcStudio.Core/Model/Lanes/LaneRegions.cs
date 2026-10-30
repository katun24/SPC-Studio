namespace SpcStudio.Model;

// The lanes drawn as regions - the tempo, the song volume, each channel's own volume, its pan, its amplify, and the echo volume - as regions, and the driver commands they are written as.
public static class LaneRegions
{
    public const int BaseOpenedRegion = -1;

    // The song's tempo as regions.
    public static IReadOnlyList<LaneRegion> Tempo(Song song)
    {
        return FromBaseAndChanges(song.BaseTempo, song.TempoChanges, song.RollLengthTicks);
    }

    // The driver commands the song's tempo is written as, in tick order.
    public static IEnumerable<LaneCommand> TempoCommands(Song song)
    {
        return Commands(Tempo(song));
    }

    // The song's song volume as regions, MML w.
    public static IReadOnlyList<LaneRegion> MasterVolume(Song song)
    {
        return FromBaseAndChanges(song.BaseMasterVolume, song.VolumeChanges, song.RollLengthTicks);
    }

    // The driver commands the song's song volume is written as, in tick order.
    public static IEnumerable<LaneCommand> MasterVolumeCommands(Song song)
    {
        return Commands(MasterVolume(song));
    }

    // One channel's volume as regions, MML v, with each overriding note's own chain laid over the lane - which is what the song plays.
    public static IReadOnlyList<LaneRegion> ChannelVolume(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var (mergedBase, mergedChanges) = NoteVolume.Lane.Merged(song, channel, notes);
        return FromBaseAndChanges(mergedBase, mergedChanges, song.RollLengthTicks);
    }

    // One channel's volume as the lane alone says it, with no note's override laid over it.
    public static IReadOnlyList<LaneRegion> ChannelVolumeOwn(Song song, int channel)
    {
        return FromBaseAndChanges(song.BaseChannelVolume(channel), song.ChannelVolumeChanges(channel), song.RollLengthTicks);
    }

    // The driver commands one channel's volume is written as, in tick order.
    public static IEnumerable<LaneCommand> ChannelVolumeCommands(Song song, int channel)
    {
        return Commands(ChannelVolume(song, channel));
    }

    // One channel's volume as the driver is given the whole of it: the merged byte it keys on at and the commands laid over that.
    public static (int Base, IReadOnlyList<LaneCommand> Commands) ChannelVolumeWritten(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var regions = ChannelVolume(song, channel, notes);
        return (regions[0].Value, [.. Commands(regions)]);
    }

    // One channel's amplify as regions, $FA $03, every one of them flat.
    public static IReadOnlyList<LaneRegion> Amplify(Song song, int channel)
    {
        return FromBaseAndChanges(song.BaseAmplify(channel), song.AmplifyChanges(channel).Select(change => new LaneDivider<int>(change.Tick, change.Value, false, 0)), song.RollLengthTicks);
    }

    // A base and the changes laid over it as regions, in tick order, always at least the first.
    public static IReadOnlyList<LaneRegion> FromBaseAndChanges(int baseValue, IEnumerable<LaneDivider<int>> changes, int songTicks)
    {
        return [.. FromBaseAndChangesIndexed(baseValue, changes, songTicks).Select(indexed => indexed.Region)];
    }

    // A base and the changes laid over it as regions, each paired with the index of the change that opened it, or a marker of its own for the one
    // the base opens.
    public static IReadOnlyList<(LaneRegion Region, int OpeningChangeIndex)> FromBaseAndChangesIndexed(int baseValue, IEnumerable<LaneDivider<int>> changes, int songTicks)
    {
        var changeList = changes as IReadOnlyList<LaneDivider<int>> ?? [.. changes];
        var regions = new List<(LaneRegion Region, int OpeningChangeIndex)>();
        int regionStart = 0;
        int regionValue = baseValue;
        int openingChangeIndex = BaseOpenedRegion;

        // Whether the first change sits at tick 0, where it carries the first region's fade rather than opening a region of its own.
        bool isFirstChangeAtTickZero = changeList.Count > 0 && changeList[0].Tick == 0;

        var tickZeroChange = isFirstChangeAtTickZero ? changeList[0] : default;
        int firstOpeningChangeIndex = isFirstChangeAtTickZero ? 1 : 0;
        bool regionFades = tickZeroChange.Fades;
        int regionFadeTo = tickZeroChange.FadeTo;
        for (int changeIndex = firstOpeningChangeIndex; changeIndex < changeList.Count; changeIndex++)
        {
            regions.Add((new LaneRegion(regionStart, changeList[changeIndex].Tick, regionValue, regionFades, regionFadeTo), openingChangeIndex));
            regionStart = changeList[changeIndex].Tick;
            regionValue = changeList[changeIndex].Value;
            regionFades = changeList[changeIndex].Fades;
            regionFadeTo = changeList[changeIndex].FadeTo;
            openingChangeIndex = changeIndex;
        }

        int lastRegionEnd = Math.Max(songTicks, regionStart + 1);
        regions.Add((new LaneRegion(regionStart, lastRegionEnd, regionValue, regionFades, regionFadeTo), openingChangeIndex));

        return regions;
    }

    // The driver commands a run of regions is written as, in tick order: a set only where the region before did not leave the value in force, and the pieces of each fade.
    public static IEnumerable<LaneCommand> Commands(IReadOnlyList<LaneRegion> regions)
    {
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            foreach (var command in RegionCommands(regions, regionIndex, false))
            {
                yield return command;
            }
        }
    }

    // One region's commands: the set where it states a value, then the pieces of its fade.
    private static IEnumerable<LaneCommand> RegionCommands(IReadOnlyList<LaneRegion> regions, int regionIndex, bool mustStateValue)
    {
        var region = regions[regionIndex];
        bool doesWriteSet = regionIndex > 0 && (mustStateValue || region.Value != regions[regionIndex - 1].EndValue);
        int? setValue = doesWriteSet ? region.Value : null;
        if (!region.Fades)
        {
            if (setValue is int value)
            {
                yield return new LaneCommand(region.Start, value);
            }

            yield break;
        }

        bool isFirstPiece = true;
        foreach (var piece in SnesFade.SplitIntoPieces(region.Start, region.Value, region.LengthTicks, region.FadeTo))
        {
            yield return new LaneCommand(piece.Tick, isFirstPiece ? setValue : null, piece.Ticks, piece.To);
            isFirstPiece = false;
        }
    }

    // One channel's pan as regions, MML y, with each overriding note's own chain laid over the lane, each region with the surround pair in force across it.
    public static IReadOnlyList<(LaneRegion Region, SnesSurround Surround)> Pan(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var (mergedBase, mergedChanges) = NotePan.Merged(song, channel, notes);
        return PanFromBaseAndChanges(song, mergedBase, mergedChanges);
    }

    // One channel's pan as the lane alone says it, with no note's override laid over it.
    public static IReadOnlyList<(LaneRegion Region, SnesSurround Surround)> PanOwn(Song song, int channel)
    {
        return PanFromBaseAndChanges(song, new PanState(song.BasePan(channel), song.BaseSurround(channel)), song.PanChanges(channel));
    }

    // A pan base and a run of dividers as regions, each tagged with its own surround pair.
    private static IReadOnlyList<(LaneRegion Region, SnesSurround Surround)> PanFromBaseAndChanges(Song song, PanState panBase, IReadOnlyList<PanChange> changes)
    {
        var indexedRegions = FromBaseAndChangesIndexed(panBase.Value, changes.Select(change => new LaneDivider<int>(change.Tick, change.Value, change.Fades, change.FadeTo)), song.RollLengthTicks);
        return
        [
            .. indexedRegions.Select(indexed => (indexed.Region, indexed.OpeningChangeIndex == BaseOpenedRegion ? panBase.Surround : changes[indexed.OpeningChangeIndex].Surround)),
        ];
    }

    // The driver commands one channel's pan is written as, each with the surround pair its $DB carries.
    public static IEnumerable<(LaneCommand Command, SnesSurround Surround)> PanCommands(Song song, int channel)
    {
        return PanRegionCommands(Pan(song, channel));
    }

    // One channel's pan as the driver is given the whole of it: the merged state it keys on at, and the commands laid over that.
    public static (PanState Base, IReadOnlyList<(LaneCommand Command, SnesSurround Surround)> Commands) PanWritten(Song song, int channel, IReadOnlyList<Note>? notes = null)
    {
        var regions = Pan(song, channel, notes);
        return (new PanState(regions[0].Region.Value, regions[0].Surround), [.. PanRegionCommands(regions)]);
    }

    // The driver commands one channel's pan lane alone is written as, each with the surround pair its $DB carries.
    public static IEnumerable<(LaneCommand Command, SnesSurround Surround)> PanOwnCommands(Song song, int channel)
    {
        return PanRegionCommands(PanOwn(song, channel));
    }

    // Pan regions as commands: the plain rule, plus a set wherever the surround pair differs from the region before.
    private static IEnumerable<(LaneCommand Command, SnesSurround Surround)> PanRegionCommands(IReadOnlyList<(LaneRegion Region, SnesSurround Surround)> panRegions)
    {
        var regionsWithoutSurround = (IReadOnlyList<LaneRegion>)[.. panRegions.Select(panRegion => panRegion.Region)];
        for (int regionIndex = 0; regionIndex < panRegions.Count; regionIndex++)
        {
            bool isSurroundChanged = regionIndex > 0 && panRegions[regionIndex].Surround != panRegions[regionIndex - 1].Surround;
            foreach (var command in RegionCommands(regionsWithoutSurround, regionIndex, isSurroundChanged))
            {
                yield return (command, panRegions[regionIndex].Surround);
            }
        }
    }

    // The song's echo volume as regions, one pair per region: the left side and the right side across the same stretch.
    public static IReadOnlyList<(LaneRegion Left, LaneRegion Right)> EchoVolume(Song song)
    {
        var leftRegions = EchoVolumeSide(song, EchoSetting.VolumeLeft);
        var rightRegions = EchoVolumeSide(song, EchoSetting.VolumeRight);
        return [.. leftRegions.Zip(rightRegions)];
    }

    // One side of the echo volume as regions.
    private static IReadOnlyList<LaneRegion> EchoVolumeSide(Song song, EchoSetting side)
    {
        return FromBaseAndChanges(song.BaseEchoVolume(side), song.EchoChanges(side), song.RollLengthTicks);
    }

    // The driver commands the echo volume is written as, in tick order: an $EF where either side differs from what the region before left in force, and
    // the pieces of each fade with a target for each side.
    public static IEnumerable<EchoVolumeCommand> EchoVolumeCommands(Song song)
    {
        var regions = EchoVolume(song);
        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            var (left, right) = regions[regionIndex];
            bool doesEitherSideChange = regionIndex > 0 && (left.Value != regions[regionIndex - 1].Left.EndValue || right.Value != regions[regionIndex - 1].Right.EndValue);
            if (!left.Fades)
            {
                if (doesEitherSideChange)
                {
                    yield return new EchoVolumeCommand(left.Start, true, left.Value, right.Value);
                }

                continue;
            }

            bool isFirstPiece = true;
            int pieceStartLeft = left.Value;
            int pieceStartRight = right.Value;
            var leftPieces = SnesFade.SplitIntoPieces(left.Start, left.Value, left.LengthTicks, left.FadeTo);
            var rightPieces = SnesFade.SplitIntoPieces(right.Start, right.Value, right.LengthTicks, right.FadeTo);
            foreach (var (leftPiece, rightPiece) in leftPieces.Zip(rightPieces))
            {
                yield return new EchoVolumeCommand(leftPiece.Tick, isFirstPiece && doesEitherSideChange, pieceStartLeft, pieceStartRight, leftPiece.Ticks, leftPiece.To, rightPiece.To);

                (pieceStartLeft, pieceStartRight) = (leftPiece.To, rightPiece.To);
                isFirstPiece = false;
            }
        }
    }
}
