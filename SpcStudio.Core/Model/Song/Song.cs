namespace SpcStudio.Model;

// The song: the samples imported into it, the instruments built on those, its monophonic channels of notes measured in ticks, and the lanes laid over
// them. This part holds the song's own settings, how long the roll is and the two markers.
public sealed partial class Song
{
    public const int GridTicks = SnesTiming.TicksPerSixteenthNote;
    public const int RollLengthStepTicks = SnesTiming.TicksPerWholeNote;
    public const int EmptyRollLengthTicks = EmptyRollLengthSteps * RollLengthStepTicks;
    public const int LowestSemitone = SnesPitch.MinNoteValue - SnesPitch.C4NoteValue;
    public const int HighestSemitone = SnesPitch.MaxPlayableNoteValue - SnesPitch.C4NoteValue;
    public const int RollRowCount = HighestSemitone - LowestSemitone + 1;
    public const int TickBeforeSong = -1;
    public const string OptimizedSampleGroup = "optimized";
    private const int EmptyRollLengthSteps = 4;

    public static readonly ValueRange SemitoneRange = new(LowestSemitone, HighestSemitone);

    public int SnapTicks { get; set; } = SnapSize.Default.Ticks;
    public TimeSignature TimeSignature { get; set; } = TimeSignature.Default;
    public double? HorizontalZoom { get; set; }
    public double VerticalZoom { get; set; } = 1;
    public Dictionary<string, double> LaneHeights { get; set; } = [];
    public List<string> LaneOrder { get; set; } = [];
    public PanelLayout PanelLayout { get; set; } = new();
    public int EndTick { get; private set; } = EmptyRollLengthTicks;
    public int LoopTick { get; private set; }
    public bool IsLooping { get; set; } = true;
    public bool HasTempoImmunity { get; set; }
    public bool UsesSmwVelocities { get; set; }
    public string SamplePath { get; set; } = "";
    public string? SampleGroup { get; set; } = OptimizedSampleGroup;
    public SpcTags SpcTags { get; set; } = SpcTags.Empty;
    public bool IsEndEnabled { get; set; } = true;

    // How long the piano roll is: to the whole note the furthest of what is on it ends in, and never shorter than an empty roll or than the end marker reaches.
    public int RollLengthTicks
    {
        get
        {
            int rollTicks = Math.Max(EmptyRollLengthTicks, RollLengthStepAtOrAfter(EndTick));
            rollTicks = Math.Max(rollTicks, RollLengthStepAtOrAfter(LastNoteEndTick));
            rollTicks = Math.Max(rollTicks, RollLengthHeldByChanges(_tempoChanges));
            rollTicks = Math.Max(rollTicks, RollLengthHeldByChanges(_volumeChanges));
            rollTicks = Math.Max(rollTicks, RollLengthHeldByUnits(_staccatoUnits));
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                rollTicks = Math.Max(rollTicks, RollLengthHeldByChanges(_channelVolumeChanges[channel]));
                rollTicks = Math.Max(rollTicks, RollLengthHeldByChanges(_panChanges[channel]));
                rollTicks = Math.Max(rollTicks, RollLengthHeldByChanges(_amplifyChanges[channel]));
                rollTicks = Math.Max(rollTicks, RollLengthHeldByChanges(_channelEchoChanges[channel]));
            }

            foreach (var changes in _echoVolumeChanges)
            {
                rollTicks = Math.Max(rollTicks, RollLengthHeldByChanges(changes));
            }

            rollTicks = Math.Max(rollTicks, RollLengthHeldByUnits(_echoRuns));
            if (_liftedReachTick is int liftedReachTick)
            {
                rollTicks = Math.Max(rollTicks, RollLengthStepAtOrAfter(liftedReachTick));
            }

            return rollTicks;
        }
    }

    // How many grid columns the roll is.
    public int RollColumnCount
    {
        get
        {
            return RollLengthTicks / GridTicks;
        }
    }

    // Where the last note stops sounding, the lifted notes included, and zero for a song with none.
    public int LastNoteEndTick
    {
        get
        {
            int lastEndTick = 0;
            foreach (var notes in _notes)
            {
                if (notes.Count > 0)
                {
                    lastEndTick = Math.Max(lastEndTick, notes[^1].EndTick());
                }
            }

            foreach (var lifted in _liftedNotes)
            {
                lastEndTick = Math.Max(lastEndTick, lifted.EndTick());
            }

            return lastEndTick;
        }
    }

    // How long the song lasts up to its end marker, across every tempo it plays at.
    public double TotalSeconds
    {
        get
        {
            return TempoMap.For(this).SecondsAt(EndTick);
        }
    }

    // How close the two markers may stand: one snap step.
    public int MinimumMarkerGapTicks
    {
        get
        {
            return SnapTicks;
        }
    }

    // The snap boundary nearest a tick, a midpoint rounding up.
    public int SnapToNearest(int tick)
    {
        return (int)Math.Round(tick / (double)SnapTicks, MidpointRounding.AwayFromZero) * SnapTicks;
    }

    // The snap boundary at or before a tick.
    public int SnapDown(int tick)
    {
        return tick / SnapTicks * SnapTicks;
    }

    // A snap boundary held inside a range: itself where it is inside, the last boundary in the range where it is past the range, and the first where it is
    // before it - or null where no boundary falls in the range.
    public int? SnapInside(int snappedTick, int lowestTick, int highestTick)
    {
        int heldTick = snappedTick > highestTick ? SnapDown(highestTick)
            : snappedTick < lowestTick ? SnapDown(lowestTick + SnapTicks - 1)
            : snappedTick;
        return heldTick >= lowestTick && heldTick <= highestTick ? heldTick : null;
    }

    // Whether a tick can carry either marker, which is anywhere on the roll with its far end included.
    public bool IsMarkerTick(int tick)
    {
        return tick >= 0 && tick <= RollLengthTicks;
    }

    // Where a run starting at a tick stops: the end marker, or the end of the roll where the marker is off or the run starts at it or past it.
    public int PlaybackEndTick(double startTick)
    {
        if (!IsEndEnabled || startTick >= EndTick)
        {
            return RollLengthTicks;
        }

        return EndTick;
    }

    // Moves the end marker to the nearest snap boundary, never closer to the loop marker than the gap, which opens the roll out to it.
    public void SetEndTick(int tick)
    {
        EndTick = Math.Max(SnapToNearest(tick), LoopTick + MinimumMarkerGapTicks);
    }

    // Moves the loop marker to the nearest snap boundary, never closer to the end marker than the gap.
    public void SetLoopTick(int tick)
    {
        LoopTick = Math.Clamp(SnapToNearest(tick), 0, Math.Max(0, EndTick - MinimumMarkerGapTicks));
    }

    // Sets both markers at once as given.
    public void SetMarkers(int endTick, int loopTick)
    {
        EndTick = endTick;
        LoopTick = loopTick;
    }

    // Carries an end marker that stood at the end of the roll out with it after the roll grew, and stretches the last stretch of each unit lane that was pinned to the old end.
    private void FollowRoll(int previousRollTicks)
    {
        int rollTicks = RollLengthTicks;
        if (rollTicks <= previousRollTicks)
        {
            return;
        }

        if (EndTick == previousRollTicks)
        {
            EndTick = rollTicks;
        }

        if (_liftedReachTick is not null)
        {
            return;
        }

        StretchLastUnitPinnedToEnd(_staccatoUnits, previousRollTicks, rollTicks);
    }

    // Stretches a lane's last unit to a new end of the roll, where it ended exactly at the old one.
    private static void StretchLastUnitPinnedToEnd<TUnit>(List<TUnit> units, int previousRollTicks, int rollTicks) where TUnit : struct, ILaneUnit<TUnit>
    {
        if (units.Count == 0)
        {
            return;
        }

        var lastUnit = units[^1];
        if (lastUnit.EndTick() == previousRollTicks)
        {
            units[^1] = lastUnit.WithSpan(lastUnit.StartTick, rollTicks - lastUnit.StartTick);
        }
    }

    // A copy of the song holding everything it plays and exports, sharing its samples.
    public Song Copy()
    {
        var copy = new Song { SamplePath = SamplePath };
        SongSnapshot.Capture(this).Restore(copy);
        copy.SetSamples(Samples);
        return copy;
    }

    // The whole-note boundary at or after a tick.
    private static int RollLengthStepAtOrAfter(int tick)
    {
        return (tick + RollLengthStepTicks - 1) / RollLengthStepTicks * RollLengthStepTicks;
    }

    // How long a lane's changes hold the roll open: to the whole note after the tick past its last change, and not at all where it has none.
    private static int RollLengthHeldByChanges<TChange>(IReadOnlyList<TChange> changes) where TChange : struct, ILaneChange
    {
        return changes.Count == 0 ? 0 : RollLengthStepAtOrAfter(changes[^1].Tick + 1);
    }

    // How long a lane's units hold the roll open: to the whole note its last unit ends in, and not at all where it has none.
    private static int RollLengthHeldByUnits<TUnit>(IReadOnlyList<TUnit> units) where TUnit : struct, ILaneUnit<TUnit>
    {
        return units.Count == 0 ? 0 : RollLengthStepAtOrAfter(units[^1].EndTick());
    }

    // Replaces a list's contents with the given items.
    private static void ReplaceContents<TItem>(List<TItem> list, IEnumerable<TItem> items)
    {
        list.Clear();
        list.AddRange(items);
    }
}
