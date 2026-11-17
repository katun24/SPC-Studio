namespace SpcStudio.Model;

// Whether two stretches of a song play the same, and the ticks the song can be cut at with nothing sounding, handing over or fading across the cut.
public static class SongStretches
{
    // A stretch of the song: its first tick and the first tick past it.
    private readonly record struct Stretch(int StartTick, int EndTick);

    // Whether the stretches of a length starting at two ticks hold the same notes, units and changes on every lane, each lane entering both under the
    // same value, the notes read as the export writes them.
    public static bool IsSamePlayed(Song song, int firstStartTick, int secondStartTick, int lengthTicks)
    {
        var first = new Stretch(firstStartTick, firstStartTick + lengthTicks);
        var second = new Stretch(secondStartTick, secondStartTick + lengthTicks);

        // Whether a lane of dividers plays the same over both stretches, entering each under its base where no divider stands before it.
        bool IsSameDividers(IReadOnlyList<LaneDivider<int>> changes, int baseValue)
        {
            return IsSameChanges(changes, new LaneDivider<int>(0, baseValue, false, 0), first, second);
        }

        if (!IsSameDividers(song.TempoChanges, song.BaseTempo)
            || !IsSameDividers(song.VolumeChanges, song.BaseMasterVolume)
            || !IsSameUnits(song.StaccatoUnits, first, second)
            || !IsSameUnits(song.EchoRuns, first, second)
            || !SnesEcho.VolumeSettings.All(setting => IsSameDividers(song.EchoChanges(setting), song.BaseEchoVolume(setting))))
        {
            return false;
        }

        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            var basePan = new PanChange(0, song.BasePan(channel), Surround: song.BaseSurround(channel));
            var baseEcho = new LaneValueChange(0, song.IsBaseChannelEchoOn(channel) ? Song.ChannelEchoOn : Song.ChannelEchoOff);
            if (!IsSameUnits(song.NotesInsideSong(channel).ToList(), first, second)
                || !IsSameDividers(song.ChannelVolumeChanges(channel), song.BaseChannelVolume(channel))
                || !IsSameChanges(song.PanChanges(channel), basePan, first, second)
                || !IsSameChanges(song.ChannelEchoChanges(channel), baseEcho, first, second))
            {
                return false;
            }
        }

        return true;
    }

    // Whether the song can be cut at each tick from its start to its end marker: no note sounds across the tick, rings on across it held or hands over
    // to the note starting there, and no lane fades across it.
    public static bool[] CleanCuts(Song song)
    {
        var isCleanCut = Enumerable.Repeat(true, song.EndTick + 1).ToArray();

        // Marks the ticks between two ticks, neither of them included, as no cut.
        void MarkInside(int fromTick, int toTick)
        {
            for (int tick = Math.Max(fromTick + 1, 0); tick < Math.Min(toTick, isCleanCut.Length); tick++)
            {
                isCleanCut[tick] = false;
            }
        }

        // Marks every tick a lane of changes fades across as no cut, a fading last region running to the end marker.
        void MarkFades<TChange>(IReadOnlyList<TChange> changes, Func<TChange, bool> doesFade) where TChange : struct, ILaneChange
        {
            for (int changeIndex = 0; changeIndex < changes.Count; changeIndex++)
            {
                if (doesFade(changes[changeIndex]))
                {
                    MarkInside(changes[changeIndex].Tick, changeIndex + 1 < changes.Count ? changes[changeIndex + 1].Tick : song.EndTick);
                }
            }
        }

        MarkFades(song.TempoChanges, change => change.Fades);
        MarkFades(song.VolumeChanges, change => change.Fades);
        foreach (var setting in SnesEcho.VolumeSettings)
        {
            MarkFades(song.EchoChanges(setting), change => change.Fades);
        }

        for (int channel = 0; channel < song.ChannelCount; channel++)
        {
            MarkFades(song.ChannelVolumeChanges(channel), change => change.Fades);
            MarkFades(song.PanChanges(channel), change => change.Fades);

            var notes = song.NotesInsideSong(channel).ToList();
            for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
            {
                var note = notes[noteIndex];
                int nextStartTick = noteIndex + 1 < notes.Count ? notes[noteIndex + 1].StartTick : song.EndTick;
                MarkInside(note.StartTick, note.IsHeld ? nextStartTick : note.EndTick());
                if (LegatoToggles.DoesBindNext(notes, noteIndex))
                {
                    isCleanCut[note.EndTick()] = false;
                }
            }
        }

        return isCleanCut;
    }

    // Whether a lane of changes plays the same over two stretches: the change each enters under, and every change inside each at the same distance from
    // its start.
    private static bool IsSameChanges<TChange>(IReadOnlyList<TChange> changes, TChange baseChange, Stretch first, Stretch second) where TChange : struct, ILaneChange<TChange>
    {
        return ChangesOver(changes, baseChange, first).SequenceEqual(ChangesOver(changes, baseChange, second));
    }

    // A lane of changes over a stretch, each counted from the stretch's start: the change it enters under at the start, then every change inside it.
    private static IEnumerable<TChange> ChangesOver<TChange>(IReadOnlyList<TChange> changes, TChange baseChange, Stretch stretch) where TChange : struct, ILaneChange<TChange>
    {
        var enteringChange = changes.LastOrDefault(change => change.Tick < stretch.StartTick, baseChange);
        yield return enteringChange.WithTick(0);

        foreach (var change in changes.Where(change => change.Tick >= stretch.StartTick && change.Tick < stretch.EndTick))
        {
            yield return change.WithTick(change.Tick - stretch.StartTick);
        }
    }

    // Whether a lane of units plays the same over two stretches: every unit reaching into each, cut to it and counted from its start.
    private static bool IsSameUnits<TUnit>(IReadOnlyList<TUnit> units, Stretch first, Stretch second) where TUnit : struct, ILaneUnit<TUnit>
    {
        return UnitsOver(units, first).SequenceEqual(UnitsOver(units, second));
    }

    // Every unit of a lane reaching into a stretch, cut to the stretch and counted from its start.
    private static IEnumerable<TUnit> UnitsOver<TUnit>(IReadOnlyList<TUnit> units, Stretch stretch) where TUnit : struct, ILaneUnit<TUnit>
    {
        foreach (var unit in units.Where(unit => unit.StartTick < stretch.EndTick && unit.EndTick() > stretch.StartTick))
        {
            int startTick = Math.Max(unit.StartTick, stretch.StartTick);
            yield return unit.WithSpan(startTick - stretch.StartTick, Math.Min(unit.EndTick(), stretch.EndTick) - startTick);
        }
    }
}
